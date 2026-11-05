using System.Net;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using FeedbackItem = KnowledgeAgent.Features.Feedback.Feedback;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Alerts;

public sealed class AlertsTests : IAsyncLifetime
{
    private static readonly EmailSettings WorkingEmail = new()
    {
        Enabled = true,
        SmtpHost = "smtp.shop.test",
        FromAddress = "help@shop.test",
        SupportAddress = "team@shop.test",
        PortalUrl = "https://support.shop.test",
    };

    private static readonly SlackSettings WorkingSlack = new() { Enabled = true, BotToken = "xoxb-test", ChannelId = "C0123456789" };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeIntegrations _integrations = new();
    private readonly FakeSlack _slack = new();
    private readonly RecordingEmail _email = new();
    private readonly FakeProfiles _profiles = new();
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => _time.GetUtcNow();

    public async ValueTask InitializeAsync()
    {
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"alerts-{Guid.NewGuid():N}",
            },
            _time,
            builder =>
            {
                var services = builder.Services;
                services.AddSingleton<IIntegrationSettings>(_integrations);
                services.AddSingleton<ISlackConnectionState>(_integrations);
                services.AddSingleton<ISlackGateway>(_slack);
                services.AddSingleton<IEmailTransport>(_email);
                services.AddScoped<IPublicAddress, PublicAddressStore>();
                services.AddSingleton<IModelProfileResolver>(_profiles);
                services.AddSingleton<IProviderErrorLog, ProviderErrorLog>();
                builder.AddAlerts();
            });
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Support_sees_only_waiting_visitors_and_missed_requests()
    {
        await AddWaitingAsync(TimeSpan.FromMinutes(7));
        await SeedAsync(db => db.Sources.Add(new Source { Type = SourceType.Text, Status = SourceStatus.Failed, Title = "Prices", UpdatedAt = Now.AddHours(-1) }));

        var admin = await ListAsync(AlertAudience.Admin);
        admin.Alerts.Select(alert => alert.Kind).ShouldBe([AlertKinds.VisitorsWaiting, AlertKinds.SourcesFailed]);
        var waiting = admin.Alerts[0];
        (waiting.Title, waiting.Detail, waiting.Count, waiting.Since, waiting.Link, waiting.Severity).ShouldBe((
            "A visitor is waiting for a person",
            "They have waited 7 minutes. Join the conversation in the Inbox.",
            1,
            Now.AddMinutes(-7),
            "/inbox",
            AlertSeverity.Warning));
        admin.Alerts[1].ShouldBe(new AlertDto(
            AlertKinds.SourcesFailed,
            AlertKinds.SourcesFailed,
            AlertSeverity.Warning,
            "A source could not be indexed",
            "The agent cannot answer from them. See why in Sources and try again.",
            1,
            Now.AddHours(-1),
            "/sources"));

        (await ListAsync(AlertAudience.Support)).Alerts.Select(alert => alert.Kind).ShouldBe([AlertKinds.VisitorsWaiting]);
    }

    [Fact]
    public async Task A_visitor_waits_past_the_threshold_before_it_is_an_alert_and_three_times_as_long_before_it_is_critical()
    {
        await AddWaitingAsync(TimeSpan.FromMinutes(4));
        (await ListAsync(AlertAudience.Support)).Alerts.ShouldBeEmpty("the default threshold is 5 minutes");

        _time.Advance(TimeSpan.FromMinutes(2));
        (await ListAsync(AlertAudience.Support)).Alerts.Single().Severity.ShouldBe(AlertSeverity.Warning);

        await AddWaitingAsync(TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(10));
        var alert = (await ListAsync(AlertAudience.Support)).Alerts.Single();
        (alert.Title, alert.Count, alert.Severity).ShouldBe(("2 visitors are waiting for a person", 2, AlertSeverity.Critical));
        alert.Detail.ShouldBe("The first has waited 16 minutes. Join the conversation in the Inbox.");

        await InScopeAsync((AlertService service) => service.SaveSettingsAsync(new SaveAlertSettingsRequest(false, false, null, 30), Ct));
        (await ListAsync(AlertAudience.Support)).Alerts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_snooze_hides_an_alert_until_its_count_grows_or_its_time_ends()
    {
        await AddWaitingAsync(TimeSpan.FromMinutes(6));
        await InScopeAsync((AlertService service) => service.SnoozeAsync(AlertKinds.VisitorsWaiting, 1, "Dana", Ct));

        var list = await ListAsync(AlertAudience.Admin);
        (list.Alerts.Count, list.Snoozed).ShouldBe((0, 1));

        _time.Advance(TimeSpan.FromMinutes(61));
        (await ListAsync(AlertAudience.Admin)).Alerts.Count.ShouldBe(1, "the snooze ended");

        await InScopeAsync((AlertService service) => service.SnoozeAsync(AlertKinds.VisitorsWaiting, null, "Dana", Ct));
        _time.Advance(TimeSpan.FromDays(3));
        (await ListAsync(AlertAudience.Admin)).Alerts.ShouldBeEmpty("dismissed until it changes");

        await AddWaitingAsync(TimeSpan.FromMinutes(10));
        (await ListAsync(AlertAudience.Admin)).Alerts.Single().Count.ShouldBe(2, "a second visitor waits");

        await Should.ThrowAsync<AlertNotActiveException>(() => InScopeAsync((AlertService service) => service.SnoozeAsync(AlertKinds.SourcesFailed, 1, null, Ct)));
        var invalid = await Should.ThrowAsync<ValidationFailedException>(() => InScopeAsync((AlertService service) => service.SnoozeAsync(AlertKinds.VisitorsWaiting, 0, null, Ct)));
        invalid.Errors.Keys.ShouldBe(["hours"]);
    }

    [Fact]
    public async Task Missing_models_and_rejected_keys_are_critical_until_fixed()
    {
        _profiles.ChatConnected = false;
        var errors = _host.Services.GetRequiredService<IProviderErrorLog>();
        errors.RecordFailure("anthropic", new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        errors.RecordFailure("openai", new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError));

        var alerts = (await ListAsync(AlertAudience.Admin)).Alerts;
        alerts.Select(alert => (alert.Key, alert.Severity, alert.Link)).ShouldBe(
        [
            ("model_missing.chat", AlertSeverity.Critical, "/models#providers"),
            ("provider_rejected.anthropic", AlertSeverity.Critical, "/models#providers"),
        ]);
        alerts[1].Title.ShouldBe("The anthropic API key was rejected");
        alerts[1].Detail.ShouldBe("The provider answered 401 Unauthorized to a model call. Add a working key in Models.");

        _profiles.ChatConnected = true;
        errors.RecordSuccess("anthropic");
        (await ListAsync(AlertAudience.Admin)).Alerts.ShouldBeEmpty();

        // A key saved after the rejection is given the benefit of the doubt until the next call.
        errors.RecordFailure("anthropic", new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));
        (await ListAsync(AlertAudience.Admin)).Alerts.Single().Key.ShouldBe("provider_rejected.anthropic");
        _time.Advance(TimeSpan.FromMinutes(1));
        await InScopeAsync((IProviderSettingsStore store) => store.UpsertAsync(new ProviderSettingsOverride { Key = "anthropic", UpdatedAt = Now }, Ct));
        (await ListAsync(AlertAudience.Admin)).Alerts.ShouldBeEmpty();
    }

    [Fact]
    public void Rejections_are_recognized_by_the_status_any_provider_exception_carries()
    {
        ProviderErrorLog.RejectedStatus(new HttpRequestException("x", null, HttpStatusCode.Unauthorized)).ShouldBe(401);
        ProviderErrorLog.RejectedStatus(new InvalidOperationException("wrapped", new StatusException(403))).ShouldBe(403);
        ProviderErrorLog.RejectedStatus(new StatusException(429)).ShouldBeNull();
        ProviderErrorLog.RejectedStatus(new TimeoutException()).ShouldBeNull();
    }

    [Fact]
    public async Task Slack_down_longer_than_a_short_drop_and_failed_sends_are_integration_alerts()
    {
        _integrations.Slack = WorkingSlack with { AppToken = "xapp-test" };
        (await ListAsync(AlertAudience.Admin)).Alerts.ShouldBeEmpty("Slack may be reconnecting");

        _time.Advance(TimeSpan.FromMinutes(3));
        var slack = (await ListAsync(AlertAudience.Admin)).Alerts.Single();
        (slack.Key, slack.Title, slack.Link).ShouldBe((AlertKinds.SlackFailing, "Slack is not connected", "/notifications"));

        _integrations.IsConnected = true;
        var conversation = await AddConversationAsync();
        await SeedAsync(db => db.Handovers.Add(NewHandover(conversation, HandoverStatus.Pending, Now, "email", "535 Authentication failed")));
        var email = (await ListAsync(AlertAudience.Admin)).Alerts.Single();
        (email.Key, email.Title).ShouldBe((AlertKinds.EmailFailing, "Requests could not be emailed"));
        email.Detail.ShouldBe("1 request in the last 24 hours could not be sent. Last error: 535 Authentication failed. They are still in the Inbox.");
    }

    [Fact]
    public async Task Ratings_well_below_the_week_before_raise_an_alert_only_with_enough_ratings()
    {
        var conversation = await AddConversationAsync();
        await SeedAsync(db =>
        {
            for (var day = 2; day < 7; day++)
            {
                db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 5, Now.AddDays(-day)));
                db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 4, Now.AddDays(-day).AddHours(1)));
            }

            for (var index = 0; index < 4; index++)
            {
                db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 3, Now.AddHours(-index - 1)));
            }
        });
        (await ListAsync(AlertAudience.Admin)).Alerts.ShouldBeEmpty("4 ratings are too few to tell");

        await SeedAsync(db => db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 3, Now.AddMinutes(-5))));
        _time.Advance(AlertRules.QualityCacheLifetime);
        var alert = (await ListAsync(AlertAudience.Admin)).Alerts.Single();
        (alert.Key, alert.Title, alert.Link).ShouldBe((AlertKinds.RatingDrop, "Conversation ratings dropped", "/feedback"));
        alert.Detail.ShouldBe("The last 24 hours average 3 out of 5 from 5 ratings, against 4.5 in the 7 days before. Read the low ratings in Feedback.");
    }

    [Fact]
    public async Task A_spike_of_questions_the_agent_could_not_answer_is_an_alert()
    {
        for (var index = 0; index < 5; index++)
        {
            var conversation = await AddConversationAsync(Now.AddHours(-1));
            await SeedAsync(db => db.Handovers.Add(NewHandover(conversation, HandoverStatus.Resolved, Now.AddMinutes(-50))));
        }

        var alert = (await ListAsync(AlertAudience.Admin)).Alerts.Single();
        (alert.Key, alert.Link).ShouldBe((AlertKinds.UnansweredSpike, "/analytics"));
        alert.Detail.ShouldStartWith("5 today against 0 on an average day of the week before.");
    }

    [Fact]
    public async Task A_new_alert_is_sent_once_to_Slack_and_email_until_it_resolves_and_comes_back()
    {
        _integrations.Slack = WorkingSlack;
        _integrations.Email = WorkingEmail;
        await SaveSettingsAsync(slack: true, email: true);
        var request = await AddWaitingAsync(TimeSpan.FromMinutes(6));

        await RunJobAsync();
        _slack.Posted.Single().ShouldBe((
            "C0123456789",
            ":large_orange_circle: *A visitor is waiting for a person*\nThey have waited 6 minutes. Join the conversation in the Inbox.\n<https://support.shop.test/inbox|Fix it>"));
        var mail = _email.Sent.Single();
        mail.Subject.ShouldBe("Knowledge Agent: A visitor is waiting for a person");
        mail.To.Mailboxes.Single().Address.ShouldBe("team@shop.test");
        mail.TextBody!.ShouldContain("Fix it: https://support.shop.test/inbox");

        _time.Advance(TimeSpan.FromMinutes(5));
        await RunJobAsync();
        (_slack.Posted.Count, _email.Sent.Count).ShouldBe((1, 1), "one message per alert");

        await SeedAsync(db =>
        {
            var stored = db.Handovers.Single(item => item.Id == request);
            stored.Status = HandoverStatus.Active;
        });
        await RunJobAsync();
        var state = (await InScopeAsync((IAlertRepository repository) => repository.ListStatesAsync(Ct))).Single();
        state.ResolvedAt.ShouldBe(Now);

        _time.Advance(AlertNotifyJob.PerKindInterval);
        await AddWaitingAsync(TimeSpan.FromMinutes(6));
        await RunJobAsync();
        (_slack.Posted.Count, _email.Sent.Count).ShouldBe((2, 2), "it came back");
    }

    [Fact]
    public async Task Sending_skips_snoozed_and_unselected_kinds_and_waits_out_the_rate_limit_of_a_kind()
    {
        _integrations.Slack = WorkingSlack;
        await SaveSettingsAsync(slack: true, email: false, kinds: [AlertKinds.MissedRequests, AlertKinds.ProviderRejected]);
        await SeedAsync(db => db.Sources.Add(new Source { Type = SourceType.Text, Status = SourceStatus.Failed, Title = "Prices", UpdatedAt = Now }));
        var errors = _host.Services.GetRequiredService<IProviderErrorLog>();
        errors.RecordFailure("anthropic", new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        errors.RecordFailure("openai", new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        var missed = await AddConversationAsync();
        await SeedAsync(db =>
        {
            var request = NewHandover(missed, HandoverStatus.Expired, Now.AddHours(-2));
            request.ResolvedAt = Now.AddHours(-1);
            db.Handovers.Add(request);
        });
        await InScopeAsync((AlertService service) => service.SnoozeAsync(AlertKinds.MissedRequests, 4, "Dana", Ct));

        await RunJobAsync();
        _slack.Posted.Select(post => post.Text).ShouldHaveSingleItem().ShouldContain("The anthropic API key was rejected");

        _time.Advance(TimeSpan.FromMinutes(30));
        await RunJobAsync();
        _slack.Posted.Count.ShouldBe(1, "one message per kind per hour");

        _time.Advance(TimeSpan.FromMinutes(30));
        await RunJobAsync();
        _slack.Posted.Count.ShouldBe(2);
        _slack.Posted[1].Text.ShouldContain("The openai API key was rejected");
    }

    [Fact]
    public async Task Without_a_channel_nothing_is_sent_and_a_failed_send_is_tried_again()
    {
        _integrations.Slack = WorkingSlack;
        await AddWaitingAsync(TimeSpan.FromMinutes(6));
        await RunJobAsync();
        _slack.Posted.ShouldBeEmpty("notifications are off by default");
        (await InScopeAsync((IAlertRepository repository) => repository.ListStatesAsync(Ct))).Single().NotifiedAt.ShouldBeNull();

        await SaveSettingsAsync(slack: true, email: false);
        _slack.Failure = new SlackApiException("chat.postMessage", "channel_not_found");
        await RunJobAsync();
        _slack.Posted.ShouldBeEmpty();

        _slack.Failure = null;
        _time.Advance(TimeSpan.FromMinutes(5));
        await RunJobAsync();
        _slack.Posted.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Settings_keep_known_kinds_in_order_and_say_whether_the_channels_can_send()
    {
        _integrations.Email = WorkingEmail;
        var defaults = await InScopeAsync((AlertService service) => service.GetSettingsAsync(Ct));
        (defaults.Slack, defaults.Email, defaults.WaitingMinutes, defaults.SlackReady, defaults.EmailReady, defaults.SlackChannel, defaults.EmailTo)
            .ShouldBe((false, false, 5, false, true, (string?)null, (string?)"team@shop.test"));
        defaults.Kinds.ShouldBe(AlertKinds.All);
        defaults.AllKinds.ShouldBe(AlertKinds.All);

        var saved = await InScopeAsync((AlertService service) => service.SaveSettingsAsync(
            new SaveAlertSettingsRequest(true, true, [AlertKinds.RatingDrop, AlertKinds.VisitorsWaiting], 10), Ct));
        (saved.Slack, saved.Email, saved.WaitingMinutes).ShouldBe((true, true, 10));
        saved.Kinds.ShouldBe([AlertKinds.VisitorsWaiting, AlertKinds.RatingDrop]);
        (await InScopeAsync((AlertService service) => service.GetSettingsAsync(Ct))).Kinds.ShouldBe(saved.Kinds);

        var invalid = await Should.ThrowAsync<ValidationFailedException>(() => InScopeAsync((AlertService service) =>
            service.SaveSettingsAsync(new SaveAlertSettingsRequest(true, false, ["everything"], 0), Ct)));
        invalid.Errors.Keys.ShouldBe(["kinds", "waitingMinutes"], ignoreOrder: true);
    }

    [Fact]
    public async Task Alert_links_use_the_public_address_saved_under_Settings_System()
    {
        _integrations.Slack = WorkingSlack;
        _integrations.Email = WorkingEmail;
        await SaveSettingsAsync(slack: true, email: true);
        await InScopeAsync((IPublicAddress address) => address.SaveAsync("https://help.shop.test/portal/", Ct));
        await AddWaitingAsync(TimeSpan.FromMinutes(6));

        await RunJobAsync();

        _slack.Posted.Single().Text.ShouldEndWith("<https://help.shop.test/portal/inbox|Fix it>", Case.Sensitive, "the saved address wins over the email settings' portal address");
        _email.Sent.Single().TextBody!.ShouldContain("Fix it: https://help.shop.test/portal/inbox");
    }

    private Task SaveSettingsAsync(bool slack, bool email, IReadOnlyList<string>? kinds = null) =>
        InScopeAsync((AlertService service) => service.SaveSettingsAsync(new SaveAlertSettingsRequest(slack, email, kinds, null), Ct));

    private Task<AlertListDto> ListAsync(AlertAudience audience) => InScopeAsync((AlertService service) => service.ListAsync(audience, Ct));

    private Task RunJobAsync() =>
        InScopeAsync((AlertNotifyJob job) => job.HandleAsync(new JobContext(Guid.NewGuid().ToString("N"), AlertNotifyJob.Kind, "all", 1, 1), Ct));

    private async Task<Guid> AddWaitingAsync(TimeSpan waited)
    {
        var conversation = await AddConversationAsync();
        var request = NewHandover(conversation, HandoverStatus.Pending, Now - waited);
        await SeedAsync(db => db.Handovers.Add(request));
        return request.Id;
    }

    private async Task<Guid> AddConversationAsync(DateTimeOffset? createdAt = null)
    {
        var conversation = new Conversation
        {
            Channel = ConversationChannel.Widget,
            Status = ConversationStatus.HandoverPending,
            CreatedAt = createdAt ?? Now.AddDays(-10),
            UpdatedAt = createdAt ?? Now.AddDays(-10),
        };
        await SeedAsync(db => db.Conversations.Add(conversation));
        return conversation.Id;
    }

    private async Task SeedAsync(Action<KnowledgeDbContext> seed)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        seed(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static HandoverRequest NewHandover(Guid conversationId, HandoverStatus status, DateTimeOffset requestedAt, string channel = "portal", string? error = null) => new()
    {
        ConversationId = conversationId,
        Channel = channel,
        Status = status,
        Source = HandoverSource.Visitor,
        Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
        RequestedAt = requestedAt,
        LastError = error,
        UpdatedAt = requestedAt,
    };

    private static FeedbackItem NewFeedback(Guid conversationId, FeedbackKind kind, int rating, DateTimeOffset at) => new()
    {
        ConversationId = conversationId,
        Kind = kind,
        Rating = rating,
        Author = FeedbackAuthor.Visitor,
        AuthorKey = Guid.NewGuid().ToString("N"),
        CreatedAt = at,
        UpdatedAt = at,
    };

    private sealed class StatusException(int status) : Exception($"HTTP {status}")
    {
        public int StatusCode { get; } = status;
    }

    private sealed class FakeProfiles : IModelProfileResolver
    {
        public bool ChatConnected { get; set; } = true;

        public Task<ResolvedModelProfile> ResolveAsync(
            string profileName,
            ModelProfilePatch? promptPatch = null,
            ModelProfilePatch? requestPatch = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModelProfileState>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelProfileState>>(
            [
                State("orchestrator", ModelKind.Chat, ChatConnected),
                State("embedding", ModelKind.Embedding, connected: true),
            ]);

        private static ModelProfileState State(string name, ModelKind kind, bool connected) => new(
            name,
            kind,
            new ModelProfileOptions { Kind = kind },
            null,
            connected
                ? new ResolvedModelProfile { Name = name, Kind = kind, ProviderKey = "fake", ProviderType = "fake", Model = "m", Source = ModelProfileSource.Configuration }
                : null,
            connected ? null : "No provider has an API key.");
    }

    private sealed class FakeIntegrations : IIntegrationSettings, ISlackConnectionState
    {
        public EmailSettings Email { get; set; } = new();

        public SlackSettings Slack { get; set; } = new();

        public bool IsConnected { get; set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult(Email);

        public Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default) => Task.FromResult(Slack);

        public Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default) => Task.FromResult((false, false));

        public Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSlack : ISlackGateway
    {
        public List<(string Channel, string Text)> Posted { get; } = [];

        public Exception? Failure { get; set; }

        public Task<string> PostMessageAsync(string channel, string text, JsonArray? blocks = null, string? threadTs = null, CancellationToken cancellationToken = default)
        {
            if (Failure is { } failure)
            {
                return Task.FromException<string>(failure);
            }

            Posted.Add((channel, text));
            return Task.FromResult("1.0");
        }

        public Task UpdateMessageAsync(string channel, string ts, string text, JsonArray? blocks = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> OpenSocketAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingEmail : IEmailTransport
    {
        public List<MimeMessage> Sent { get; } = [];

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task<int> ProcessInboxAsync(
            Func<EmailEnvelope, bool> wants,
            Func<InboundEmail, CancellationToken, Task<bool>> handle,
            int maxMessages,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> TestInboxAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
