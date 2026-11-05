using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Overview;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FeedbackItem = KnowledgeAgent.Features.Feedback.Feedback;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Overview;

// The API scenarios run the same queries on PostgreSQL.
public sealed class OverviewRepositoryTests : IAsyncLifetime
{
    private const int Days = 7;
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"overview-{Guid.NewGuid():N}",
            },
            configure: builder => builder.Services.AddScoped<IOverviewRepository, OverviewRepository>());
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Counts_the_conversations_started_in_the_period_and_those_that_went_to_a_person()
    {
        var shop = NewWidget("Shop help");
        var docs = NewWidget("Docs help");
        // A playground test, which counts nowhere.
        var portal = NewConversation(null, startedDaysAgo: 1);
        var resolved = NewConversation(shop, startedDaysAgo: 2);
        var cancelled = NewConversation(shop, startedDaysAgo: 3);
        var expired = NewConversation(docs, startedDaysAgo: 6);
        var olderButActive = NewConversation(shop, startedDaysAgo: 10, activeDaysAgo: 1);
        var older = NewConversation(docs, startedDaysAgo: 12);
        var outOfRange = NewConversation(docs, startedDaysAgo: 20);
        await SaveAsync(db =>
        {
            db.Widgets.AddRange(shop, docs);
            db.Conversations.AddRange(portal, resolved, cancelled, expired, olderButActive, older, outOfRange);
            db.Handovers.AddRange(
                NewRequest(resolved, HandoverStatus.Resolved),
                NewRequest(cancelled, HandoverStatus.Cancelled),
                NewRequest(expired, HandoverStatus.Cancelled),
                NewRequest(expired, HandoverStatus.Expired),
                NewRequest(older, HandoverStatus.Resolved));
        });

        var overview = await GetAsync();

        overview.Days.ShouldBe(Days);
        overview.From.ShouldBe(Now.AddDays(-Days));
        overview.Conversations.ShouldBe(3);
        overview.WebsiteConversations.ShouldBe(3);
        overview.HandedToPerson.ShouldBe(2, "the expired request still asked for a person; the cancelled one went back to the assistant");
        overview.AnsweredByAi.ShouldBe(1);
        overview.PreviousConversations.ShouldBe(2);
    }

    [Fact]
    public async Task Averages_the_conversation_ratings_given_in_the_period()
    {
        var conversation = NewConversation(NewWidget("Shop help"), startedDaysAgo: 1);
        var test = NewConversation(null, startedDaysAgo: 1);
        await SaveAsync(db =>
        {
            db.Conversations.AddRange(conversation, test);
            db.Feedback.AddRange(
                NewRating(test, FeedbackKind.Conversation, 1, daysAgo: 1),
                NewRating(conversation, FeedbackKind.Conversation, 5, daysAgo: 1),
                NewRating(conversation, FeedbackKind.Conversation, 4, daysAgo: 2),
                NewRating(conversation, FeedbackKind.Conversation, 4, daysAgo: 3),
                NewRating(conversation, FeedbackKind.Conversation, 1, daysAgo: 9),
                NewRating(conversation, FeedbackKind.Answer, -1, daysAgo: 1));
        });

        var overview = await GetAsync();

        overview.Ratings.ShouldBe(3);
        overview.AverageRating.ShouldBe(4.33);
    }

    [Fact]
    public async Task Has_nothing_to_count_on_a_new_installation()
    {
        var overview = await GetAsync();

        (overview.Conversations, overview.Ratings, overview.AverageRating).ShouldBe((0, 0, (double?)null));
        overview.Widgets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reports_each_widgets_conversations_in_the_period_and_its_last_activity()
    {
        var shop = NewWidget("Shop help");
        var docs = NewWidget("Docs help");
        var unused = NewWidget("New widget");
        await SaveAsync(db =>
        {
            db.Widgets.AddRange(shop, docs, unused);
            db.Conversations.AddRange(
                NewConversation(shop, startedDaysAgo: 2),
                NewConversation(shop, startedDaysAgo: 3),
                NewConversation(shop, startedDaysAgo: 10, activeDaysAgo: 1),
                NewConversation(docs, startedDaysAgo: 30),
                NewConversation(null, startedDaysAgo: 1),
                NewConversation(shop, startedDaysAgo: 0, test: true));
        });

        var widgets = (await GetAsync()).Widgets.ToDictionary(item => item.WidgetId);

        widgets.Count.ShouldBe(3);
        widgets[shop.Id].ShouldBe(new WidgetActivityDto(shop.Id, 2, Now.AddDays(-1)));
        widgets[docs.Id].ShouldBe(new WidgetActivityDto(docs.Id, 0, Now.AddDays(-30)));
        widgets[unused.Id].ShouldBe(new WidgetActivityDto(unused.Id, 0, null));
    }

    private static Widget NewWidget(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        PublicKey = Widget.KeyPrefix + Guid.NewGuid().ToString("N")[..24],
        Name = name,
        AgentId = Guid.CreateVersion7(),
        CreatedAt = Now.AddDays(-60),
        UpdatedAt = Now.AddDays(-60),
    };

    private static Conversation NewConversation(Widget? widget, int startedDaysAgo, int? activeDaysAgo = null, bool test = false) => new()
    {
        Channel = widget is null || test ? ConversationChannel.Portal : ConversationChannel.Widget,
        WidgetId = widget?.Id,
        Title = "Parcel",
        CreatedAt = Now.AddDays(-startedDaysAgo),
        UpdatedAt = Now.AddDays(-(activeDaysAgo ?? startedDaysAgo)),
    };

    private static HandoverRequest NewRequest(Conversation conversation, HandoverStatus status) => new()
    {
        ConversationId = conversation.Id,
        WidgetId = conversation.WidgetId,
        Channel = WidgetChoices.HandoverPortal,
        Status = status,
        Source = HandoverSource.Visitor,
        Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
        RequestedAt = conversation.CreatedAt,
        UpdatedAt = conversation.CreatedAt,
    };

    private static FeedbackItem NewRating(Conversation conversation, FeedbackKind kind, int rating, int daysAgo) => new()
    {
        ConversationId = conversation.Id,
        Kind = kind,
        Rating = rating,
        Author = FeedbackAuthor.Visitor,
        AuthorKey = Guid.NewGuid().ToString(),
        CreatedAt = Now.AddDays(-daysAgo),
        UpdatedAt = Now.AddDays(-daysAgo),
    };

    private async Task SaveAsync(Action<KnowledgeDbContext> add)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        add(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<OverviewDto> GetAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOverviewRepository>().GetAsync(Now, Days, Ct);
    }
}
