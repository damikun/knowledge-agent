using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedbackItem = KnowledgeAgent.Features.Feedback.Feedback;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Alerts;

// The database is shared per class, so each test clears the tables it counts over.
public abstract class AlertRepositoryContractTests<TFixture>
    where TFixture : PersistenceFixture
{
    private static readonly DateTimeOffset Now = new(2099, 6, 1, 12, 0, 0, TimeSpan.Zero);

    protected AlertRepositoryContractTests(TFixture fixture)
    {
        _ = fixture.Services;
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Source_facts_count_failed_and_waiting_sources_with_the_oldest_change()
    {
        await ClearAsync();
        await SeedAsync(db =>
        {
            db.Sources.Add(NewSource(SourceStatus.Failed, Now.AddHours(-3)));
            db.Sources.Add(NewSource(SourceStatus.Failed, Now.AddHours(-1)));
            db.Sources.Add(NewSource(SourceStatus.Waiting, Now.AddDays(-2)));
            db.Sources.Add(NewSource(SourceStatus.Ready, Now.AddDays(-9)));
            db.Sources.Add(NewSource(SourceStatus.Processing, Now.AddDays(-9)));
        });

        var facts = await InScopeAsync(repository => repository.GetSourceFactsAsync(Ct));

        facts.Failed.ShouldBe(new AlertCount(2, Now.AddHours(-3)));
        facts.Waiting.ShouldBe(new AlertCount(1, Now.AddDays(-2)));
    }

    [Fact]
    public async Task Waiting_counts_pending_requests_older_than_the_threshold_and_missed_ones_nobody_answered()
    {
        await ClearAsync();
        var waitingLong = await AddConversationAsync();
        var waitingShort = await AddConversationAsync();
        var missed = await AddConversationAsync();
        var answeredLater = await AddConversationAsync();
        var missedLongAgo = await AddConversationAsync();
        await SeedAsync(db =>
        {
            db.Handovers.Add(NewHandover(waitingLong, HandoverStatus.Pending, Now.AddMinutes(-12)));
            db.Handovers.Add(NewHandover(waitingShort, HandoverStatus.Pending, Now.AddMinutes(-2)));
            db.Handovers.Add(NewHandover(missed, HandoverStatus.Expired, Now.AddHours(-3), resolvedAt: Now.AddHours(-2)));
            db.Handovers.Add(NewHandover(answeredLater, HandoverStatus.Expired, Now.AddHours(-4), resolvedAt: Now.AddHours(-3)));
            db.Handovers.Add(NewHandover(missedLongAgo, HandoverStatus.Expired, Now.AddDays(-3), resolvedAt: Now.AddDays(-3)));
            db.ConversationMessages.Add(new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = answeredLater,
                Author = MessageAuthor.Staff,
                Text = "Sorry for the wait.",
                CreatedAt = Now.AddHours(-1),
            });
        });

        (await InScopeAsync(repository => repository.CountWaitingForPersonAsync(Now.AddMinutes(-5), Ct)))
            .ShouldBe(new AlertCount(1, Now.AddMinutes(-12)));
        (await InScopeAsync(repository => repository.CountWaitingForPersonAsync(Now, Ct))).Count.ShouldBe(2);
        (await InScopeAsync(repository => repository.CountMissedRequestsAsync(Now.AddHours(-24), Ct)))
            .ShouldBe(new AlertCount(1, Now.AddHours(-3)), "a staff reply after the request answers it, and older ones are left out");
    }

    [Fact]
    public async Task Delivery_failures_are_split_into_Slack_and_email_by_what_did_not_go_out()
    {
        await ClearAsync();
        var slackFailed = await AddConversationAsync();
        var slackPosted = await AddConversationAsync();
        var emailFailed = await AddConversationAsync();
        var expiredNotEmailed = await AddConversationAsync();
        var oldFailure = await AddConversationAsync();
        await SeedAsync(db =>
        {
            db.Handovers.Add(NewHandover(slackFailed, HandoverStatus.Pending, Now.AddHours(-2), WidgetChoices.HandoverSlack, error: "channel_not_found", updatedAt: Now.AddHours(-2)));
            db.Handovers.Add(NewHandover(slackPosted, HandoverStatus.Pending, Now.AddHours(-2), WidgetChoices.HandoverSlack, error: "rate limited", updatedAt: Now.AddHours(-1), slackThread: "1.2"));
            db.Handovers.Add(NewHandover(emailFailed, HandoverStatus.Pending, Now.AddHours(-5), WidgetChoices.HandoverEmail, error: "535 auth failed", updatedAt: Now.AddHours(-5)));
            db.Handovers.Add(NewHandover(expiredNotEmailed, HandoverStatus.Expired, Now.AddHours(-1), WidgetChoices.HandoverSlack, error: "SMTP down", updatedAt: Now.AddMinutes(-30), slackThread: "3.4"));
            db.Handovers.Add(NewHandover(oldFailure, HandoverStatus.Pending, Now.AddDays(-3), WidgetChoices.HandoverEmail, error: "old", updatedAt: Now.AddDays(-3)));
        });

        var failures = await InScopeAsync(repository => repository.CountDeliveryFailuresAsync(Now.AddHours(-24), Ct));

        failures.Slack.ShouldBe(new AlertCount(1, Now.AddHours(-2)));
        failures.Email.ShouldBe(new AlertCount(2, Now.AddHours(-5)));
        failures.LastSlackError.ShouldBe("channel_not_found");
        failures.LastEmailError.ShouldBe("SMTP down");
    }

    [Fact]
    public async Task Feedback_is_tallied_for_the_period_only()
    {
        await ClearAsync();
        var conversation = await AddConversationAsync();
        await SeedAsync(db =>
        {
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 2, Now.AddHours(-1)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 3, Now.AddHours(-2)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Answer, 1, Now.AddHours(-2)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Answer, -1, Now.AddHours(-3)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Answer, -1, Now.AddHours(-4)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Agent, -1, Now.AddHours(-4)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 5, Now.AddDays(-2)));
        });

        var tally = await InScopeAsync(repository => repository.TallyFeedbackAsync(Now.AddHours(-24), Now, Ct));

        tally.ShouldBe(new FeedbackTally(2, 2.5, 1, 2));
        tally.ThumbsUpShare.ShouldBe(1.0 / 3);
        (await InScopeAsync(repository => repository.TallyFeedbackAsync(Now.AddDays(-3), Now.AddHours(-24), Ct))).ShouldBe(new FeedbackTally(1, 5, 0, 0));
    }

    [Fact]
    public async Task Snoozes_are_added_replaced_and_deleted_by_key()
    {
        await ClearAsync();
        await InScopeAsync(repository => repository.SaveSnoozeAsync(NewSnooze("sources_failed", 2, Now.AddHours(4)), Ct));
        await InScopeAsync(repository => repository.SaveSnoozeAsync(NewSnooze("model_missing.chat", 1, until: null), Ct));
        await InScopeAsync(repository => repository.SaveSnoozeAsync(NewSnooze("sources_failed", 5, Now.AddHours(8)), Ct));

        var snoozes = await InScopeAsync(repository => repository.ListSnoozesAsync(Ct));
        snoozes.Select(item => (item.Key, item.Count, item.Until)).ShouldBe(
            [("model_missing.chat", 1, (DateTimeOffset?)null), ("sources_failed", 5, Now.AddHours(8))],
            ignoreOrder: true);
        snoozes.Single(item => item.Key == "sources_failed").Severity.ShouldBe(AlertSeverity.Warning);

        await InScopeAsync(repository => repository.DeleteSnoozesAsync(["sources_failed", "unknown"], Ct));
        (await InScopeAsync(repository => repository.ListSnoozesAsync(Ct))).Select(item => item.Key).ShouldBe(["model_missing.chat"]);
    }

    [Fact]
    public async Task States_are_added_and_replaced_by_key()
    {
        await ClearAsync();
        await InScopeAsync(repository => repository.SaveStatesAsync(
            [
                new AlertState { Key = "visitors_waiting", Kind = "visitors_waiting", RaisedAt = Now, LastSeenAt = Now },
                new AlertState { Key = "sources_failed", Kind = "sources_failed", RaisedAt = Now, LastSeenAt = Now, NotifiedAt = Now },
            ],
            Ct));
        await InScopeAsync(repository => repository.SaveStatesAsync(
            [new AlertState { Key = "visitors_waiting", Kind = "visitors_waiting", RaisedAt = Now, LastSeenAt = Now.AddMinutes(5), ResolvedAt = Now.AddMinutes(5) }],
            Ct));

        var states = (await InScopeAsync(repository => repository.ListStatesAsync(Ct))).ToDictionary(state => state.Key);
        states.Count.ShouldBe(2);
        states["visitors_waiting"].ResolvedAt.ShouldBe(Now.AddMinutes(5));
        states["visitors_waiting"].IsActive.ShouldBeFalse();
        states["sources_failed"].NotifiedForCurrent.ShouldBeTrue();
    }

    private async Task ClearAsync()
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.Feedback.RemoveRange(await db.Feedback.ToListAsync(Ct));
        db.Handovers.RemoveRange(await db.Handovers.ToListAsync(Ct));
        db.ConversationMessages.RemoveRange(await db.ConversationMessages.ToListAsync(Ct));
        db.Conversations.RemoveRange(await db.Conversations.ToListAsync(Ct));
        db.Sources.RemoveRange(await db.Sources.ToListAsync(Ct));
        db.AlertSnoozes.RemoveRange(await db.AlertSnoozes.ToListAsync(Ct));
        db.AlertStates.RemoveRange(await db.AlertStates.ToListAsync(Ct));
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedAsync(Action<KnowledgeDbContext> seed)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        seed(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Guid> AddConversationAsync()
    {
        var conversation = new Conversation { Channel = ConversationChannel.Widget, CreatedAt = Now.AddDays(-5), UpdatedAt = Now.AddDays(-5) };
        await SeedAsync(db => db.Conversations.Add(conversation));
        return conversation.Id;
    }

    private async Task<TResult> InScopeAsync<TResult>(Func<IAlertRepository, Task<TResult>> action)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await action(Repository(scope.ServiceProvider));
    }

    private async Task InScopeAsync(Func<IAlertRepository, Task> action)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        await action(Repository(scope.ServiceProvider));
    }

    private static AlertRepository Repository(IServiceProvider services) =>
        new(services.GetRequiredService<KnowledgeDbContext>(), services.GetRequiredService<DbContextGate>());

    private static Source NewSource(SourceStatus status, DateTimeOffset updatedAt) => new()
    {
        Type = SourceType.Text,
        Status = status,
        Title = $"Source {Guid.NewGuid():N}",
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    private static HandoverRequest NewHandover(
        Guid conversationId,
        HandoverStatus status,
        DateTimeOffset requestedAt,
        string channel = "portal",
        DateTimeOffset? resolvedAt = null,
        string? error = null,
        DateTimeOffset? updatedAt = null,
        string? slackThread = null) => new()
    {
        ConversationId = conversationId,
        Channel = channel,
        Status = status,
        Source = HandoverSource.Visitor,
        Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
        RequestedAt = requestedAt,
        ResolvedAt = resolvedAt,
        LastError = error,
        SlackThreadTs = slackThread,
        UpdatedAt = updatedAt ?? requestedAt,
    };

    private static FeedbackItem NewFeedback(Guid conversationId, FeedbackKind kind, int rating, DateTimeOffset at) => new()
    {
        ConversationId = conversationId,
        MessageId = kind == FeedbackKind.Answer ? Guid.NewGuid() : null,
        Kind = kind,
        Rating = rating,
        Author = FeedbackAuthor.Visitor,
        AuthorKey = Guid.NewGuid().ToString("N"),
        CreatedAt = at,
        UpdatedAt = at,
    };

    private static AlertSnooze NewSnooze(string key, int count, DateTimeOffset? until) => new()
    {
        Key = key,
        Kind = key.Split('.')[0],
        Count = count,
        Severity = AlertSeverity.Warning,
        Until = until,
        SnoozedBy = "Dana",
        CreatedAt = Now,
    };
}

public sealed class InMemoryAlertRepositoryTests(InMemoryPersistenceFixture fixture)
    : AlertRepositoryContractTests<InMemoryPersistenceFixture>(fixture), IClassFixture<InMemoryPersistenceFixture>;

public sealed class PostgresAlertRepositoryTests(PostgresPersistenceFixture fixture)
    : AlertRepositoryContractTests<PostgresPersistenceFixture>(fixture), IClassFixture<PostgresPersistenceFixture>;
