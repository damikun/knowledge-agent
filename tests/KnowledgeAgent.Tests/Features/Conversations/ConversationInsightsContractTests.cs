using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using FeedbackItem = KnowledgeAgent.Features.Feedback.Feedback;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Conversations;

// The database is shared per class: tests keep apart with unique words and widgets,
// and each analytics test looks at a period of its own.
public abstract class ConversationInsightsContractTests<TFixture>
    where TFixture : PersistenceFixture
{
    private static readonly DateTimeOffset Start = new(2099, 3, 1, 12, 0, 0, TimeSpan.Zero);

    protected ConversationInsightsContractTests(TFixture fixture)
    {
        _ = fixture.Services;
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_matches_every_term_in_the_title_or_any_message_ignoring_case()
    {
        var word = Word();
        var site = Guid.NewGuid();
        var inTitle = await AddAsync(site, title: $"Refund for {word}");
        var inMessage = await AddAsync(site, texts: ["Hello", $"Where is my REFUND? Order {word} never came."]);
        var wordOnly = await AddAsync(site, texts: [$"Shipping question {word}"]);

        (await SearchIdsAsync(new ConversationSearchQuery { Text = $"{word} refund" })).ShouldBe([inMessage, inTitle], ignoreOrder: true);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = word })).ShouldBe([inTitle, inMessage, wordOnly], ignoreOrder: true);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = $"{word} invoice" })).ShouldBeEmpty();

        var page = await SearchAsync(new ConversationSearchQuery { Text = $"{word} refund" });
        page.Items.Single(item => item.Id == inMessage).Match.ShouldBe($"Where is my REFUND? Order {word} never came.");
        page.Items.Single(item => item.Id == inTitle).Match.ShouldBeNull("only the title matched");
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        var word = Word();
        var site = Guid.NewGuid();
        var percent = await AddAsync(site, title: $"100% {word}");
        await AddAsync(site, title: $"1000 {word}");
        await AddAsync(site, title: $"1_0 {word}");

        (await SearchIdsAsync(new ConversationSearchQuery { Text = $"{word} 100%" })).ShouldBe([percent]);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = $"{word} 1_" })).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Search_leaves_playground_tests_out_unless_asked_for()
    {
        var word = Word();
        var live = await AddAsync(Guid.NewGuid(), title: $"Live {word}");
        var test = await AddAsync(widgetId: null, title: $"Test {word}");

        var page = await SearchAsync(new ConversationSearchQuery { Text = word });
        page.Items.Select(item => (item.Id, item.Test)).ShouldBe([(live, false)]);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = word, IncludeTests = true })).ShouldBe([live, test], ignoreOrder: true);
        (await SearchAsync(new ConversationSearchQuery { Text = word, Channel = ConversationChannel.Portal })).Items.Single().Test.ShouldBeTrue();
    }

    [Fact]
    public async Task Filters_narrow_by_channel_widget_status_request_rating_thumbs_and_start()
    {
        var widget = Guid.NewGuid();
        var portal = await AddAsync(widgetId: null, createdAt: Start.AddDays(-1), title: $"portal {widget:N}");
        var handedOver = await AddAsync(widgetId: widget, status: ConversationStatus.Human, createdAt: Start.AddDays(-2));
        var rated = await AddAsync(widgetId: widget, createdAt: Start.AddDays(-3));
        var thumbsDown = await AddAsync(widgetId: widget, status: ConversationStatus.Closed, createdAt: Start.AddDays(-10));
        await SeedAsync(db =>
        {
            db.Handovers.Add(NewHandover(handedOver, HandoverStatus.Active, Start.AddDays(-2)));
            db.Feedback.Add(NewFeedback(rated, FeedbackKind.Conversation, 2, Start.AddDays(-3)));
            db.Feedback.Add(NewFeedback(thumbsDown, FeedbackKind.Answer, -1, Start.AddDays(-10)));
            db.Feedback.Add(NewFeedback(thumbsDown, FeedbackKind.Conversation, 5, Start.AddDays(-10)));
        });
        var mine = new ConversationSearchQuery { WidgetId = widget };

        (await SearchIdsAsync(mine)).ShouldBe([handedOver, rated, thumbsDown], ignoreOrder: true);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = widget.ToString("N"), Channel = ConversationChannel.Portal })).ShouldBe([portal]);
        (await SearchIdsAsync(mine with { Channel = ConversationChannel.Portal })).ShouldBeEmpty();
        (await SearchIdsAsync(mine with { Status = ConversationStatus.Human })).ShouldBe([handedOver]);
        (await SearchIdsAsync(mine with { HasHandover = true })).ShouldBe([handedOver]);
        (await SearchIdsAsync(mine with { HasHandover = false })).ShouldBe([rated, thumbsDown], ignoreOrder: true);
        (await SearchIdsAsync(mine with { MaxRating = 2 })).ShouldBe([rated]);
        (await SearchIdsAsync(mine with { MinRating = 4 })).ShouldBe([thumbsDown]);
        (await SearchIdsAsync(mine with { ThumbsDown = true })).ShouldBe([thumbsDown]);
        (await SearchIdsAsync(mine with { From = Start.AddDays(-5) })).ShouldBe([handedOver, rated], ignoreOrder: true);
        (await SearchIdsAsync(mine with { From = Start.AddDays(-5), To = Start.AddDays(-2).AddHours(-1) })).ShouldBe([rated]);

        var item = (await SearchAsync(mine with { ThumbsDown = true })).Items.Single();
        (item.Rating, item.ThumbsDown, item.Status, item.Channel).ShouldBe(((int?)5, 1, "closed", "widget"));
        (await SearchAsync(mine with { HasHandover = true })).Items.Single().HandoverStatus.ShouldBe("active");
    }

    [Theory]
    [InlineData(ConversationSortKey.Updated, true)]
    [InlineData(ConversationSortKey.Updated, false)]
    [InlineData(ConversationSortKey.Created, true)]
    [InlineData(ConversationSortKey.Created, false)]
    public async Task Keyset_pages_list_every_match_once_in_order(ConversationSortKey sort, bool descending)
    {
        var widget = Guid.NewGuid();
        var created = new List<(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();
        for (var index = 0; index < 7; index++)
        {
            // Pairs share a timestamp, so the id breaks the tie.
            var createdAt = Start.AddHours(-index / 2);
            var updatedAt = Start.AddMinutes(index % 3);
            created.Add((await AddAsync(widgetId: widget, createdAt: createdAt, updatedAt: updatedAt), createdAt, updatedAt));
        }

        var seen = new List<Guid>();
        ConversationCursor? after = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var page = await SearchAsync(new ConversationSearchQuery { WidgetId = widget, Sort = sort, Descending = descending, Limit = 3, After = after });
            seen.AddRange(page.Items.Select(item => item.Id));
            if (page.NextCursor is null)
            {
                break;
            }

            ConversationCursor.TryDecode(page.NextCursor, out after).ShouldBeTrue();
        }

        seen.Count.ShouldBe(7);
        seen.Distinct().Count().ShouldBe(7);
        var keys = seen.Select(id => created.Single(item => item.Id == id)).Select(item => sort == ConversationSortKey.Created ? item.CreatedAt : item.UpdatedAt).ToList();
        keys.ShouldBe(descending ? keys.OrderByDescending(key => key).ToList() : keys.Order().ToList());
    }

    [Fact]
    public async Task The_list_puts_pinned_conversations_first_and_pages_every_conversation_once()
    {
        var widget = Guid.NewGuid();
        var created = new List<(Guid Id, DateTimeOffset UpdatedAt, bool Pinned)>();
        for (var index = 0; index < 9; index++)
        {
            // Pairs share a timestamp, so the id breaks the tie; every third one is pinned.
            var updatedAt = Start.AddMinutes(-(index / 2));
            var id = await AddAsync(widgetId: widget, createdAt: Start.AddDays(-1), updatedAt: updatedAt);
            var pinned = index % 3 == 1;
            if (pinned)
            {
                await InScopeAsync((IConversationRepository conversations) => conversations.SetPinnedAsync(id, true, Ct));
            }

            created.Add((id, updatedAt, pinned));
        }

        var seen = new List<ConversationSummaryDto>();
        ConversationCursor? after = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var page = await ListAsync(new ConversationSearchQuery { WidgetId = widget, Limit = 2, After = after });
            seen.AddRange(page.Items);
            if (page.NextCursor is null)
            {
                break;
            }

            ConversationCursor.TryDecode(page.NextCursor, out after).ShouldBeTrue();
            after.ShouldNotBeNull().Pinned.ShouldNotBeNull();
        }

        var expected = created.OrderByDescending(item => item.Pinned).ThenByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id).Select(item => item.Id);
        seen.Select(item => item.Id).ShouldBe(expected);
        seen.Take(3).ShouldAllBe(item => item.Pinned);
        (await ListAsync(new ConversationSearchQuery { WidgetId = widget, Pinned = true })).Items.Count.ShouldBe(3);
        (await ListAsync(new ConversationSearchQuery { WidgetId = widget, Pinned = false })).Items.Count.ShouldBe(6);
        (await ListAsync(new ConversationSearchQuery { Id = created[0].Id })).Items.ShouldHaveSingleItem().Id.ShouldBe(created[0].Id);
    }

    [Fact]
    public async Task The_list_and_the_search_filter_by_labels_and_show_them()
    {
        var word = Word();
        var widget = Guid.NewGuid();
        var question = await AddLabelAsync($"{word} question");
        var problem = await AddLabelAsync($"{word} problem");
        var asked = await AddAsync(widgetId: widget, title: $"Asked {word}");
        var broken = await AddAsync(widgetId: widget, texts: [$"It is broken {word}"]);
        var plain = await AddAsync(widgetId: widget, texts: [$"Hello {word}"]);
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(asked, [question.Id], Start, Ct));
        await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(broken, [problem.Id, question.Id], Start, Ct));
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(broken, [problem.Id], Start, Ct));

        (await ListIdsAsync(new ConversationSearchQuery { WidgetId = widget, LabelIds = [question.Id] })).ShouldBe([asked], "an auto label staff removed does not count");
        (await ListIdsAsync(new ConversationSearchQuery { WidgetId = widget, LabelIds = [question.Id, problem.Id] })).ShouldBe([asked, broken], ignoreOrder: true);
        (await ListIdsAsync(new ConversationSearchQuery { Text = $"{word} broken" })).ShouldBe([broken]);
        (await SearchIdsAsync(new ConversationSearchQuery { Text = word, LabelIds = [problem.Id] })).ShouldBe([broken]);

        var listed = (await ListAsync(new ConversationSearchQuery { WidgetId = widget })).Items;
        listed.Single(item => item.Id == broken).Labels.ShouldBe([new ConversationLabelDto(problem.Id, problem.Name, "blue", "auto")]);
        listed.Single(item => item.Id == plain).Labels.ShouldBeEmpty();
        var found = (await SearchAsync(new ConversationSearchQuery { Text = word })).Items;
        found.Single(item => item.Id == asked).Labels.ShouldBe([new ConversationLabelDto(question.Id, question.Name, "blue", "manual")]);
        (await InScopeAsync((IConversationSearch search) => search.GetHistoryAsync(asked, Ct))).ShouldNotBeNull().Labels.Single().Id.ShouldBe(question.Id);
    }

    [Fact]
    public async Task Analytics_count_conversations_per_label_against_the_previous_period()
    {
        var now = new DateTimeOffset(2202, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var word = Word();
        var question = await AddLabelAsync($"{word} question");
        var problem = await AddLabelAsync($"{word} problem");
        foreach (var (createdAt, label) in new[] { (now.AddDays(-1), question), (now.AddDays(-2), question), (now.AddDays(-3), problem), (now.AddDays(-9), problem), (now.AddDays(-10), problem), (now.AddDays(-30), question) })
        {
            var id = await AddAsync(createdAt: createdAt);
            await InScopeAsync((ILabelRepository labels) => labels.SetAsync(id, [label.Id], createdAt, Ct));
        }

        var analytics = await InScopeAsync((IAnalyticsRepository repository) => repository.GetAsync(now, 7, Ct));

        analytics.Labels.Where(item => item.Name.StartsWith(word, StringComparison.Ordinal))
            .Select(item => (item.LabelId, item.Conversations, item.PreviousConversations))
            .ShouldBe([(question.Id, 2, 0), (problem.Id, 1, 2)]);
    }

    [Fact]
    public async Task History_lists_every_rating_and_request_for_a_person_newest_first()
    {
        var conversation = await AddAsync(widgetId: Guid.NewGuid());
        await SeedAsync(db =>
        {
            db.Handovers.Add(NewHandover(conversation, HandoverStatus.Expired, Start.AddHours(-3)));
            db.Handovers.Add(NewHandover(conversation, HandoverStatus.Resolved, Start.AddHours(-1)));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Answer, -1, Start.AddHours(-2), comment: "Wrong price"));
            db.Feedback.Add(NewFeedback(conversation, FeedbackKind.Conversation, 4, Start));
        });

        var history = (await InScopeAsync((IConversationSearch search) => search.GetHistoryAsync(conversation, Ct))).ShouldNotBeNull();

        history.Feedback.Select(item => (item.Kind, item.Rating)).ShouldBe([("conversation", 4), ("answer", -1)]);
        history.Feedback[1].Comment.ShouldBe("Wrong price");
        history.Handovers.Select(item => item.Status).ShouldBe(["resolved", "expired"]);
        (await InScopeAsync((IConversationSearch search) => search.GetHistoryAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Analytics_count_the_period_by_day_and_widget_and_group_the_questions_the_agent_could_not_answer()
    {
        // A period of its own, so the other tests' data stays out.
        var now = new DateTimeOffset(2201, 5, 10, 15, 0, 0, TimeSpan.Zero);
        var widget = Guid.NewGuid();
        var other = Guid.NewGuid();

        // New messages bump a conversation's last activity to the clock, which must not fall before the period.
        if (Fixture.Time.GetUtcNow() < now.AddDays(-3))
        {
            Fixture.Time.SetUtcNow(now.AddDays(-3));
        }

        var handed = await AddAsync(widgetId: widget, createdAt: now.AddHours(-2), texts: ["Can I pay by invoice?"]);
        var cancelled = await AddAsync(widgetId: widget, createdAt: now.AddDays(-1), texts: ["can I pay by  INVOICE"]);
        var disliked = await AddAsync(widgetId: other, createdAt: now.AddDays(-1));

        // A playground test with every signal, which counts nowhere.
        var tested = await AddAsync(widgetId: null, createdAt: now.AddHours(-3));
        var unsourced = await AddAsync(widgetId: widget, createdAt: now.AddDays(-2));
        await AddAsync(widgetId: widget, createdAt: now.AddDays(-9));

        var dislikedAnswer = await AddExchangeAsync(disliked, "What does shipping to Norway cost?", "It is free.", now.AddDays(-1), citations: "[{\"ref\":\"S1\"}]", tools: "[]");
        var unsourcedAnswer = await AddExchangeAsync(
            unsourced,
            "Do you sell gift cards?",
            "I could not find anything about gift cards.",
            now.AddDays(-2),
            citations: "[]",
            tools: $$"""[{"callId":"c1","toolName":"{{ToolNames.SearchKnowledgeBase}}"}]""");
        await AddExchangeAsync(unsourced, "Thanks!", "You are welcome.", now.AddDays(-2).AddMinutes(1), citations: "[]", tools: "[]");
        var testedAnswer = await AddExchangeAsync(
            tested,
            "Is this only a test?",
            "I found nothing.",
            now.AddHours(-3),
            citations: "[]",
            tools: $$"""[{"callId":"c1","toolName":"{{ToolNames.SearchKnowledgeBase}}"}]""");
        await SeedAsync(db =>
        {
            db.Handovers.Add(NewHandover(handed, HandoverStatus.Resolved, now.AddHours(-1), widget));
            db.Handovers.Add(NewHandover(cancelled, HandoverStatus.Cancelled, now.AddDays(-1).AddMinutes(5), widget));
            db.Feedback.Add(NewFeedback(disliked, FeedbackKind.Answer, -1, now.AddDays(-1), messageId: dislikedAnswer, comment: "It costs 20 EUR", widgetId: other));
            db.Feedback.Add(NewFeedback(tested, FeedbackKind.Answer, -1, now.AddHours(-3), messageId: testedAnswer));
            db.Feedback.Add(NewFeedback(tested, FeedbackKind.Conversation, 1, now.AddHours(-3)));
            db.Feedback.Add(NewFeedback(handed, FeedbackKind.Answer, 1, now.AddHours(-1), widgetId: widget));
            db.Feedback.Add(NewFeedback(handed, FeedbackKind.Conversation, 5, now.AddHours(-1), widgetId: widget));
            db.Feedback.Add(NewFeedback(cancelled, FeedbackKind.Conversation, 2, now.AddDays(-1), widgetId: widget));
        });

        var analytics = await InScopeAsync((IAnalyticsRepository repository) => repository.GetAsync(now, 7, Ct));

        analytics.From.ShouldBe(new DateTimeOffset(2201, 5, 4, 0, 0, 0, TimeSpan.Zero));
        analytics.Totals.ShouldBe(new AnalyticsTotalsDto(4, 3, 1, 2, 3.5, 1, 1, 1));
        analytics.Daily.Count.ShouldBe(7);
        analytics.Daily[^1].ShouldBe(new AnalyticsDayDto(new DateOnly(2201, 5, 10), 1, 1, 1, 0, 1, 5));
        analytics.Daily[^2].ShouldBe(new AnalyticsDayDto(new DateOnly(2201, 5, 9), 2, 0, 0, 1, 1, 2));
        var website = analytics.Widgets.Single(item => item.WidgetId == widget);
        (website.Conversations, website.HandedToPerson, website.Ratings, website.AverageRating).ShouldBe((3, 1, 2, (double?)3.5));
        analytics.Widgets.Single(item => item.WidgetId == other).ThumbsDown.ShouldBe(1);
        analytics.Widgets.ShouldNotContain(item => item.WidgetId == null, "playground tests count nowhere");

        analytics.Unanswered.Count.ShouldBe(3);
        var invoice = analytics.Unanswered[0];
        (invoice.Question, invoice.Count, invoice.ConversationId).ShouldBe(("Can I pay by invoice?", 2, handed));
        invoice.Reasons.ShouldBe([UnansweredReasons.Handover]);
        var shipping = analytics.Unanswered.Single(item => item.Reasons.Contains(UnansweredReasons.ThumbsDown));
        (shipping.Question, shipping.MessageId, shipping.Answer, shipping.Comment).ShouldBe(("What does shipping to Norway cost?", (Guid?)dislikedAnswer, "It is free.", "It costs 20 EUR"));
        var giftCards = analytics.Unanswered.Single(item => item.Reasons.Contains(UnansweredReasons.NoSources));
        (giftCards.Question, giftCards.MessageId, giftCards.WidgetId).ShouldBe(("Do you sell gift cards?", (Guid?)unsourcedAnswer, (Guid?)widget));
    }

    [Fact]
    public async Task Analytics_of_an_empty_period_have_a_row_for_every_day()
    {
        var analytics = await InScopeAsync((IAnalyticsRepository repository) =>
            repository.GetAsync(new DateTimeOffset(2202, 1, 1, 0, 0, 0, TimeSpan.Zero), 30, Ct));

        analytics.Totals.ShouldBe(new AnalyticsTotalsDto(0, 0, 0, 0, null, 0, 0, 0));
        analytics.Daily.Count.ShouldBe(30);
        analytics.Widgets.ShouldBeEmpty();
        analytics.Unanswered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Custom_datasets_keep_names_unique_ignoring_case_and_delete_with_their_questions()
    {
        var name = "Support " + Word();
        var dataset = new CustomDatasetRecord { Name = name, CreatedAt = Start, UpdatedAt = Start };
        var item = new CustomDatasetItemRecord { Question = "How long is the warranty?", ExpectedFacts = ["2 years"], Tags = ["warranty"], CreatedAt = Start, UpdatedAt = Start };

        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.AddAsync(dataset, [item], Ct))).ShouldBeTrue();
        (await InScopeAsync((ICustomDatasetRepository datasets) =>
            datasets.AddAsync(new CustomDatasetRecord { Name = name.ToUpperInvariant(), CreatedAt = Start, UpdatedAt = Start }, [], Ct))).ShouldBeFalse();

        var added = new CustomDatasetItemRecord { DatasetId = dataset.Id, Question = "Can I return a gift?", Answerable = false, CreatedAt = Start.AddMinutes(1), UpdatedAt = Start };
        await InScopeAsync((ICustomDatasetRepository datasets) => datasets.AddItemAsync(added, Ct));
        var items = await InScopeAsync((ICustomDatasetRepository datasets) => datasets.ListItemsAsync(dataset.Id, Ct));
        items.Select(entry => entry.Question).ShouldBe(["How long is the warranty?", "Can I return a gift?"]);
        items[0].ExpectedFacts.ShouldBe(["2 years"]);
        items[1].Answerable.ShouldBeFalse();
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.FindByNameAsync(name.ToLowerInvariant(), Ct))).ShouldNotBeNull().Id.ShouldBe(dataset.Id);
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.ListAsync(Ct))).Single(entry => entry.Dataset.Id == dataset.Id)
            .ShouldSatisfyAllConditions(entry => entry.ItemCount.ShouldBe(2), entry => entry.Tags.ShouldBe(["warranty"]));

        var renamed = new CustomDatasetRecord { Id = dataset.Id, Name = name + " v2", Description = "Edited", CreatedAt = Start };
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.UpdateAsync(renamed, Ct))).ShouldBeTrue();
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.GetAsync(dataset.Id, Ct))).ShouldNotBeNull().Name.ShouldBe(name + " v2");

        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.DeleteItemAsync(dataset.Id, added.Id, Ct))).ShouldBeTrue();
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.DeleteAsync(dataset.Id, Ct))).ShouldBeTrue();
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.ListItemsAsync(dataset.Id, Ct))).ShouldBeEmpty();
        (await InScopeAsync((ICustomDatasetRepository datasets) => datasets.DeleteAsync(dataset.Id, Ct))).ShouldBeFalse();
    }

    private async Task<ConversationListPageDto> ListAsync(ConversationSearchQuery query) =>
        await InScopeAsync((IConversationSearch search) => search.ListAsync(query, Ct));

    private async Task<List<Guid>> ListIdsAsync(ConversationSearchQuery query) =>
        (await ListAsync(query with { Limit = ConversationSearchQuery.MaxLimit })).Items.Select(item => item.Id).ToList();

    private Task<Label> AddLabelAsync(string name) =>
        InScopeAsync((ILabelRepository labels) => labels.AddAsync(new Label { Name = name, Color = "blue", CreatedAt = Start, UpdatedAt = Start }, Ct));

    private async Task<ConversationSearchPageDto> SearchAsync(ConversationSearchQuery query) =>
        await InScopeAsync((IConversationSearch search) => search.SearchAsync(query, Ct));

    private async Task<List<Guid>> SearchIdsAsync(ConversationSearchQuery query) =>
        (await SearchAsync(query with { Limit = ConversationSearchQuery.MaxLimit })).Items.Select(item => item.Id).ToList();

    private async Task<Guid> AddAsync(
        Guid? widgetId = null,
        ConversationStatus status = ConversationStatus.Ai,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string title = "Conversation",
        string[]? texts = null)
    {
        var started = createdAt ?? Start;
        var conversation = new Conversation
        {
            Title = title,
            Channel = widgetId is null ? ConversationChannel.Portal : ConversationChannel.Widget,
            WidgetId = widgetId,
            Status = status,
            CreatedAt = started,
            UpdatedAt = updatedAt ?? started,
        };
        var sequence = 0;
        foreach (var text in texts ?? [])
        {
            conversation.Messages.Add(new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Sequence = ++sequence,
                Author = widgetId is null ? MessageAuthor.User : MessageAuthor.Visitor,
                Text = text,
                CreatedAt = started,
            });
        }

        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        return conversation.Id;
    }

    private async Task<Guid> AddExchangeAsync(Guid conversationId, string question, string answer, DateTimeOffset at, string citations, string tools)
    {
        var turn = new ConversationTurn
        {
            ConversationId = conversationId,
            UserMessage = question,
            AssistantMessage = answer,
            CitationsJson = citations,
            ToolCallsJson = tools,
            CreatedAt = at,
        };
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(turn, Ct));
        await InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(
            new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversationId, Author = MessageAuthor.User, Text = question, TurnId = turn.Id, CreatedAt = at },
            Ct));
        var reply = await InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(
            new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversationId, Author = MessageAuthor.Assistant, Text = answer, TurnId = turn.Id, CreatedAt = at },
            Ct));
        return reply.Id;
    }

    private async Task SeedAsync(Action<KnowledgeDbContext> seed)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        seed(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static string Word() => "zq" + Guid.NewGuid().ToString("N")[..10];

    private static HandoverRequest NewHandover(Guid conversationId, HandoverStatus status, DateTimeOffset requestedAt, Guid? widgetId = null) => new()
    {
        ConversationId = conversationId,
        WidgetId = widgetId,
        Channel = "portal",
        Status = status,
        Source = HandoverSource.Visitor,
        Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
        RequestedAt = requestedAt,
        UpdatedAt = requestedAt,
    };

    private static FeedbackItem NewFeedback(
        Guid conversationId,
        FeedbackKind kind,
        int rating,
        DateTimeOffset at,
        Guid? messageId = null,
        string? comment = null,
        Guid? widgetId = null) => new()
    {
        ConversationId = conversationId,
        MessageId = messageId,
        Kind = kind,
        Rating = rating,
        Comment = comment,
        Author = FeedbackAuthor.Visitor,
        AuthorKey = Guid.NewGuid().ToString("N"),
        WidgetId = widgetId,
        CreatedAt = at,
        UpdatedAt = at,
    };
}

public sealed class InMemoryConversationInsightsTests(InMemoryPersistenceFixture fixture)
    : ConversationInsightsContractTests<InMemoryPersistenceFixture>(fixture), IClassFixture<InMemoryPersistenceFixture>;

public sealed class PostgresConversationInsightsTests(PostgresPersistenceFixture fixture)
    : ConversationInsightsContractTests<PostgresPersistenceFixture>(fixture), IClassFixture<PostgresPersistenceFixture>
{
    [Fact]
    public async Task Search_words_are_looked_up_in_the_trigram_indexes()
    {
        await using var dataSource = NpgsqlDataSource.Create(Fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Sequential scans are off so the plan does not depend on the table's size.
        await using (var disable = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await disable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT c.id FROM conversations c WHERE c.id IN (SELECT t.id FROM conversations t WHERE t.title ILIKE '%refund%' "
            + "UNION ALL SELECT m.conversation_id FROM conversation_messages m WHERE m.text ILIKE '%refund%')",
            connection);
        var plan = string.Join('\n', await ReadAllAsync(explain));

        plan.ShouldContain("ix_conversation_messages_text");
        plan.ShouldContain("ix_conversations_title");
    }

    private static async Task<List<string>> ReadAllAsync(NpgsqlCommand command)
    {
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }
}
