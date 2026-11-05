using System.Security.Claims;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

// Tests share one database per class, so they isolate themselves with unique ids and timestamps.
public abstract class RepositoryContractTests<TFixture>
    where TFixture : PersistenceFixture
{
    private static readonly DateTimeOffset BaseTime = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    protected RepositoryContractTests(TFixture fixture)
    {
        // Skip here when the database is unavailable, before Should.ThrowAsync can mistake the skip for a failure.
        _ = fixture.Services;
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Source_round_trips_every_field()
    {
        var createdAt = new DateTimeOffset(2025, 3, 4, 10, 11, 12, TimeSpan.FromHours(2));
        var source = new Source
        {
            Type = SourceType.Pdf,
            Status = SourceStatus.Ready,
            Title = "Příručka 📄 \"quoted\"",
            Url = "https://example.com/docs?page=1&lang=cs",
            FileName = "handbook.pdf",
            ContentType = "application/pdf",
            SizeBytes = 123_456_789_012,
            ContentHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            ChunkCount = 42,
            PageCount = 7,
            TokenEstimate = 9001,
            EmbeddingModel = "hash:hash-v1:384",
            Error = "previous attempt failed",
            Attempts = 2,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(5),
            ProcessedAt = createdAt.AddMinutes(10),
            Metadata = new()
            {
                ["finalUrl"] = "https://example.com/docs/final",
                ["siteName"] = "Example – Docs",
                ["quote\"key"] = "line 1\nline 2 \"x\" ✓",
                ["empty"] = string.Empty,
            },
        };

        await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
        var loaded = await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct));

        loaded.ShouldNotBeNull();
        loaded.ShouldSatisfyAllConditions(
            () => loaded.Type.ShouldBe(SourceType.Pdf),
            () => loaded.Status.ShouldBe(SourceStatus.Ready),
            () => loaded.Title.ShouldBe(source.Title),
            () => loaded.Url.ShouldBe(source.Url),
            () => loaded.FileName.ShouldBe(source.FileName),
            () => loaded.ContentType.ShouldBe(source.ContentType),
            () => loaded.SizeBytes.ShouldBe(source.SizeBytes),
            () => loaded.ContentHash.ShouldBe(source.ContentHash),
            () => loaded.ChunkCount.ShouldBe(42),
            () => loaded.PageCount.ShouldBe(7),
            () => loaded.TokenEstimate.ShouldBe(9001),
            () => loaded.EmbeddingModel.ShouldBe(source.EmbeddingModel),
            () => loaded.Error.ShouldBe(source.Error),
            () => loaded.Attempts.ShouldBe(2),
            () => loaded.CreatedAt.ShouldBe(createdAt),
            () => loaded.CreatedAt.Offset.ShouldBe(TimeSpan.Zero),
            () => loaded.UpdatedAt.ShouldBe(createdAt.AddMinutes(5)),
            () => loaded.ProcessedAt.ShouldBe(createdAt.AddMinutes(10)),
            () => loaded.Metadata.ShouldBe(source.Metadata, ignoreOrder: true));
    }

    [Fact]
    public async Task Website_pages_are_listed_found_moved_and_counted()
    {
        var folder = new SourceFolder { Name = "Crawl " + Guid.NewGuid().ToString("N")[..8] };
        await InScopeAsync((ISourceFolderRepository folders) => folders.AddAsync(folder, Ct));
        var website = new Source
        {
            Type = SourceType.Website,
            Title = "Docs",
            Url = "https://docs.example.com/guide",
            Crawl = new WebsiteCrawlSettings { Scope = CrawlScope.Section, MaxPages = 40, Exclude = ["/guide/old/*"] },
            CreatedAt = BaseTime,
            UpdatedAt = BaseTime,
        };
        var first = new Source { Type = SourceType.Url, Title = "One", Url = "https://docs.example.com/guide/one", ParentId = website.Id, CreatedAt = BaseTime.AddSeconds(1), UpdatedAt = BaseTime, LastFetchedAt = BaseTime };
        var second = new Source { Type = SourceType.Url, Title = "Two", Url = "https://docs.example.com/guide/two", ParentId = website.Id, CreatedAt = BaseTime.AddSeconds(2), UpdatedAt = BaseTime };
        foreach (var source in new[] { website, first, second })
        {
            await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
        }

        var pages = await InScopeAsync((ISourceRepository sources) => sources.ListPagesAsync(website.Id, Ct));
        var found = await InScopeAsync((ISourceRepository sources) => sources.FindPageAsync(website.Id, "https://docs.example.com/guide/two", Ct));
        var children = await InScopeAsync((ISourceRepository sources) => sources.FindAsync(new SourceQuery(Parent: new ParentFilter(website.Id)), Ct));
        var topLevel = await InScopeAsync((ISourceRepository sources) => sources.FindAsync(new SourceQuery(Ids: [website.Id, first.Id], Parent: ParentFilter.TopLevel), Ct));

        pages.Select(page => page.Id).ShouldBe([first.Id, second.Id]);
        pages[0].LastFetchedAt.ShouldBe(BaseTime);
        found!.Id.ShouldBe(second.Id);
        children.Total.ShouldBe(2);
        topLevel.Items.Select(source => source.Id).ShouldBe([website.Id]);

        await InScopeAsync((ISourceRepository sources) => sources.UpdateCrawlProgressAsync(website.Id, 5, 2, 1, Ct));
        await InScopeAsync((ISourceRepository sources) => sources.UpdateCrawlProgressAsync(website.Id, 3, 1, 0, Ct));
        var progressed = (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(website.Id, Ct)))!;
        progressed.ShouldSatisfyAllConditions(
            () => progressed.PagesFound.ShouldBe(5),
            () => progressed.PagesDone.ShouldBe(3),
            () => progressed.PagesFailed.ShouldBe(1),
            () => progressed.Crawl!.Scope.ShouldBe(CrawlScope.Section),
            () => progressed.Crawl!.MaxPages.ShouldBe(40),
            () => progressed.Crawl!.Exclude.ShouldBe(["/guide/old/*"]));

        await InScopeAsync((ISourceRepository sources) => sources.MoveToFolderAsync([website.Id], folder.Id, Ct));
        var moved = await InScopeAsync((ISourceRepository sources) => sources.ListPagesAsync(website.Id, Ct));
        moved.ShouldAllBe(page => page.FolderId == folder.Id);
        var counts = await InScopeAsync((ISourceRepository sources) => sources.CountByFolderAsync(Ct));
        counts.ByFolder[folder.Id].ShouldBe(1);
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_source()
    {
        (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
        (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task List_returns_sources_newest_first()
    {
        var start = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldest = NewSource(createdAt: start);
        var middle = NewSource(createdAt: start.AddHours(1));
        var newest = NewSource(createdAt: start.AddHours(2));
        foreach (var source in new[] { middle, newest, oldest })
        {
            await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
        }

        var listed = await InScopeAsync((ISourceRepository sources) => sources.ListAsync(Ct));

        var mine = new HashSet<Guid> { oldest.Id, middle.Id, newest.Id };
        listed.Where(source => mine.Contains(source.Id)).Select(source => source.Id)
            .ShouldBe(new[] { newest.Id, middle.Id, oldest.Id });
    }

    [Fact]
    public async Task ListByStatus_returns_only_the_requested_statuses_oldest_first()
    {
        var start = new DateTimeOffset(2100, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var pending = NewSource(SourceStatus.Pending, start);
        var processing = NewSource(SourceStatus.Processing, start.AddMinutes(1));
        var ready = NewSource(SourceStatus.Ready, start.AddMinutes(2));
        var failed = NewSource(SourceStatus.Failed, start.AddMinutes(3));
        var laterPending = NewSource(SourceStatus.Pending, start.AddMinutes(4));
        var all = new[] { laterPending, ready, processing, failed, pending };
        foreach (var source in all)
        {
            await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
        }

        var mine = all.Select(source => source.Id).ToHashSet();
        var inFlight = await InScopeAsync((ISourceRepository sources) =>
            sources.ListByStatusAsync(new HashSet<SourceStatus> { SourceStatus.Pending, SourceStatus.Processing }, Ct));
        var readyOnly = await InScopeAsync((ISourceRepository sources) => sources.ListByStatusAsync([SourceStatus.Ready], Ct));
        var none = await InScopeAsync((ISourceRepository sources) => sources.ListByStatusAsync([], Ct));

        inFlight.ShouldAllBe(source => source.Status == SourceStatus.Pending || source.Status == SourceStatus.Processing);
        inFlight.Where(source => mine.Contains(source.Id)).Select(source => source.Id)
            .ShouldBe(new[] { pending.Id, processing.Id, laterPending.Id });
        readyOnly.ShouldAllBe(source => source.Status == SourceStatus.Ready);
        readyOnly.Where(source => mine.Contains(source.Id)).Select(source => source.Id).ShouldBe(new[] { ready.Id });
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_persists_a_detached_entity_from_another_scope_and_sets_UpdatedAt()
    {
        var source = NewSource();
        await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));

        var loaded = (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct)))!;
        loaded.Status = SourceStatus.Processing;
        loaded.Attempts = 1;
        loaded.Error = null;
        loaded.ProcessedAt = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5));
        loaded.Metadata["language"] = "en";
        var updatedAt = Tick();

        await InScopeAsync((ISourceRepository sources) => sources.UpdateAsync(loaded, Ct));

        loaded.UpdatedAt.ShouldBe(updatedAt);
        var reloaded = (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct)))!;
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Status.ShouldBe(SourceStatus.Processing),
            () => reloaded.Attempts.ShouldBe(1),
            () => reloaded.ProcessedAt.ShouldBe(loaded.ProcessedAt),
            () => reloaded.Metadata.ShouldContainKeyAndValue("language", "en"),
            () => reloaded.UpdatedAt.ShouldBe(updatedAt),
            () => reloaded.CreatedAt.ShouldBe(source.CreatedAt));
    }

    [Fact]
    public async Task Repeated_updates_in_one_scope_do_not_conflict_and_the_last_write_wins()
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var sources = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
        var source = await sources.AddAsync(NewSource(), cancellationToken: Ct);

        source.Title = "updated through the added instance";
        await sources.UpdateAsync(source, Ct);

        var first = (await sources.GetAsync(source.Id, Ct))!;
        first.Status = SourceStatus.Processing;
        await sources.UpdateAsync(first, Ct);

        var second = (await sources.GetAsync(source.Id, Ct))!;
        second.Status = SourceStatus.Ready;
        second.ChunkCount = 12;
        await sources.UpdateAsync(second, Ct);

        first.Title = "stale instance";
        await sources.UpdateAsync(first, Ct);

        var stored = (await sources.GetAsync(source.Id, Ct))!;
        stored.Title.ShouldBe("stale instance");
        stored.Status.ShouldBe(SourceStatus.Processing);
        stored.ChunkCount.ShouldBe(0);
    }

    [Fact]
    public async Task Update_of_an_unknown_source_throws_KeyNotFoundException()
    {
        var missing = NewSource();

        await Should.ThrowAsync<KeyNotFoundException>(() =>
            InScopeAsync((ISourceRepository sources) => sources.UpdateAsync(missing, Ct)));
    }

    [Fact]
    public async Task Content_added_with_the_source_is_merged_by_SaveContent()
    {
        var rawBytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        var source = NewSource();
        await InScopeAsync((ISourceRepository sources) =>
            sources.AddAsync(source, new SourceContent { RawBytes = rawBytes }, Ct));

        var initial = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        initial.SourceId.ShouldBe(source.Id);
        initial.RawBytes.ShouldBe(rawBytes);
        initial.ExtractedText.ShouldBeNull();

        const string text = "Page one\fPage two – ünïcødé";
        await InScopeAsync((ISourceRepository sources) => sources.SaveContentAsync(source.Id, text, cancellationToken: Ct));
        var withText = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        withText.ExtractedText.ShouldBe(text);
        withText.RawBytes.ShouldBe(rawBytes);

        await InScopeAsync((ISourceRepository sources) => sources.SaveContentAsync(source.Id, null, [1, 2, 3], Ct));
        var withNewBytes = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        withNewBytes.ExtractedText.ShouldBe(text);
        withNewBytes.RawBytes.ShouldBe(new byte[] { 1, 2, 3 });

        await InScopeAsync((ISourceRepository sources) => sources.SaveContentAsync(source.Id, null, null, Ct));
        var unchanged = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        unchanged.ExtractedText.ShouldBe(text);
        unchanged.RawBytes.ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task SaveContent_creates_the_row_when_missing()
    {
        var source = NewSource();
        await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));

        await InScopeAsync((ISourceRepository sources) => sources.SaveContentAsync(source.Id, "extracted", cancellationToken: Ct));

        var content = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        content.ExtractedText.ShouldBe("extracted");
        content.RawBytes.ShouldBeNull();
    }

    [Fact]
    public async Task SaveContent_for_an_unknown_source_throws_KeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            InScopeAsync((ISourceRepository sources) => sources.SaveContentAsync(Guid.NewGuid(), "text", cancellationToken: Ct)));
    }

    [Fact]
    public async Task Delete_removes_the_source_and_its_content()
    {
        var source = NewSource();
        await InScopeAsync((ISourceRepository sources) =>
            sources.AddAsync(source, new SourceContent { ExtractedText = "text", RawBytes = [42] }, Ct));

        var deleted = await InScopeAsync((ISourceRepository sources) => sources.DeleteAsync(source.Id, Ct));
        var deletedAgain = await InScopeAsync((ISourceRepository sources) => sources.DeleteAsync(source.Id, Ct));

        deleted.ShouldBeTrue();
        deletedAgain.ShouldBeFalse();
        (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct))).ShouldBeNull();
        (await InScopeAsync((KnowledgeDbContext db) => db.SourceContents.CountAsync(content => content.SourceId == source.Id, Ct)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Nul_characters_are_dropped_instead_of_failing_the_write()
    {
        var source = NewSource(title: "before\0after");
        source.Metadata["key\0"] = "va\0lue";
        await InScopeAsync((ISourceRepository sources) =>
            sources.AddAsync(source, new SourceContent { ExtractedText = "pdf\0text" }, Ct));

        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        var turn = NewTurn(conversation.Id);
        turn.CitationsJson = """[{"ref":"S1","snippet":"a\u0000b","escaped":"c\\u0000d"}]""";
        turn.UserMessage = "user\0message";
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(turn, Ct));

        var loaded = (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct)))!;
        var content = (await InScopeAsync((ISourceRepository sources) => sources.GetContentAsync(source.Id, Ct)))!;
        var storedTurn = (await GetConversationAsync(conversation.Id)).Turns.ShouldHaveSingleItem();

        loaded.Title.ShouldBe("beforeafter");
        loaded.Metadata.ShouldContainKeyAndValue("key", "value");
        content.ExtractedText.ShouldBe("pdftext");
        storedTurn.UserMessage.ShouldBe("usermessage");
        var citation = JsonNode.Parse(storedTurn.CitationsJson)!.AsArray().ShouldHaveSingleItem()!;
        citation["snippet"]!.GetValue<string>().ShouldBe("ab");
        citation["escaped"]!.GetValue<string>().ShouldBe(@"c\u0000d");
    }

    [Fact]
    public async Task Conversation_round_trips_with_turns_ordered_by_sequence()
    {
        var sourceIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var conversation = NewConversation(sourceIds: sourceIds);
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var third = NewTurn(conversation.Id, sequence: 3, userMessage: "third");
        third.Provider = "anthropic";
        third.Model = "claude-opus-5-5";
        third.InputTokens = 12_345;
        third.OutputTokens = 678;
        third.FinishReason = "stop";
        third.Error = "partial failure";
        third.DurationMs = 4_321;
        third.CreatedAt = new DateTimeOffset(2024, 6, 1, 14, 0, 0, TimeSpan.FromHours(3));
        foreach (var turn in new[] { third, NewTurn(conversation.Id, 1, "first"), NewTurn(conversation.Id, 2, "second") })
        {
            await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(turn, Ct));
        }

        var loaded = await GetConversationAsync(conversation.Id);

        loaded.ShouldSatisfyAllConditions(
            () => loaded.Title.ShouldBe(conversation.Title),
            () => loaded.AgentName.ShouldBe(conversation.AgentName),
            () => loaded.SystemPrompt.ShouldBe(conversation.SystemPrompt),
            () => loaded.PromptName.ShouldBe("orchestrator-system"),
            () => loaded.PromptVersion.ShouldBe(3),
            () => loaded.PromptOrigin.ShouldBe("langfuse"),
            () => loaded.Profile.ShouldBe("orchestrator"),
            () => loaded.SourceIds.ShouldBe(sourceIds),
            () => loaded.CreatedAt.ShouldBe(conversation.CreatedAt),
            () => loaded.Turns.Select(turn => turn.UserMessage).ShouldBe(new[] { "first", "second", "third" }),
            () => loaded.Turns.Select(turn => turn.Sequence).ShouldBe(new[] { 1, 2, 3 }));

        var storedThird = loaded.Turns[2];
        storedThird.ShouldSatisfyAllConditions(
            () => storedThird.Id.ShouldBe(third.Id),
            () => storedThird.ConversationId.ShouldBe(conversation.Id),
            () => storedThird.AssistantMessage.ShouldBe(third.AssistantMessage),
            () => storedThird.Provider.ShouldBe("anthropic"),
            () => storedThird.Model.ShouldBe("claude-opus-5-5"),
            () => storedThird.InputTokens.ShouldBe(12_345),
            () => storedThird.OutputTokens.ShouldBe(678),
            () => storedThird.FinishReason.ShouldBe("stop"),
            () => storedThird.Error.ShouldBe("partial failure"),
            () => storedThird.DurationMs.ShouldBe(4_321),
            () => storedThird.CreatedAt.ShouldBe(third.CreatedAt));
    }

    [Fact]
    public async Task Get_without_turns_returns_only_the_conversation()
    {
        var conversation = NewConversation();
        conversation.Turns.Add(NewTurn(conversation.Id));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var loaded = await InScopeAsync((IConversationRepository conversations) =>
            conversations.GetAsync(conversation.Id, includeTurns: false, Ct));
        var unknown = await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(Guid.NewGuid(), true, Ct));

        loaded.ShouldNotBeNull().Turns.ShouldBeEmpty();
        unknown.ShouldBeNull();
    }

    [Fact]
    public async Task Create_stores_initial_turns_and_numbers_those_without_a_sequence()
    {
        var conversation = NewConversation();
        conversation.Turns.Add(NewTurn(conversation.Id, userMessage: "a"));
        conversation.Turns.Add(NewTurn(conversation.Id, userMessage: "b"));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var loaded = await GetConversationAsync(conversation.Id);

        loaded.Turns.Select(turn => (turn.Sequence, turn.UserMessage)).ShouldBe(new[] { (1, "a"), (2, "b") });
    }

    [Fact]
    public async Task AddTurn_assigns_the_next_sequence_and_bumps_UpdatedAt()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var first = NewTurn(conversation.Id);
        var firstAt = Tick();
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(first, Ct));
        var afterFirst = await GetConversationAsync(conversation.Id);

        var second = NewTurn(conversation.Id);
        var secondAt = Tick();
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(second, Ct));
        var afterSecond = await GetConversationAsync(conversation.Id);

        first.Sequence.ShouldBe(1);
        second.Sequence.ShouldBe(2);
        afterFirst.UpdatedAt.ShouldBe(firstAt);
        afterSecond.UpdatedAt.ShouldBe(secondAt);
        afterSecond.CreatedAt.ShouldBe(conversation.CreatedAt);
        afterSecond.Turns.Select(turn => turn.Id).ShouldBe(new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task AddTurn_keeps_an_explicit_sequence()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var explicitTurn = NewTurn(conversation.Id, sequence: 10);
        var nextTurn = NewTurn(conversation.Id);
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(explicitTurn, Ct));
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(nextTurn, Ct));

        explicitTurn.Sequence.ShouldBe(10);
        nextTurn.Sequence.ShouldBe(11);
    }

    [Fact]
    public async Task AddTurn_to_an_unknown_conversation_throws_KeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(NewTurn(Guid.NewGuid()), Ct)));
    }

    [Fact]
    public async Task ListIdle_returns_a_widgets_quiet_conversations_in_one_status_least_recently_updated_first()
    {
        var widgetId = Guid.CreateVersion7();
        var cutoff = BaseTime.AddHours(1);
        var visitorLast = await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, BaseTime, MessageAuthor.Visitor);
        var staffLast = await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, BaseTime.AddMinutes(1), MessageAuthor.Staff);
        var noticeLast = await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, BaseTime.AddMinutes(2), MessageAuthor.System);
        await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, cutoff, MessageAuthor.Staff);
        var withAgent = await AddWidgetConversationAsync(widgetId, ConversationStatus.Ai, BaseTime, MessageAuthor.Assistant);
        await AddWidgetConversationAsync(Guid.CreateVersion7(), ConversationStatus.Human, BaseTime, MessageAuthor.Staff);

        Task<IReadOnlyList<Guid>> IdleAsync(ConversationStatus status, MessageAuthor[]? lastAuthors, int take) =>
            InScopeAsync((IConversationRepository conversations) => conversations.ListIdleAsync(widgetId, status, cutoff, lastAuthors, take, Ct));

        (await IdleAsync(ConversationStatus.Human, [MessageAuthor.Staff, MessageAuthor.System], 10)).ShouldBe([staffLast, noticeLast]);
        (await IdleAsync(ConversationStatus.Human, null, 10)).ShouldBe([visitorLast, staffLast, noticeLast]);
        (await IdleAsync(ConversationStatus.Human, null, 2)).ShouldBe([visitorLast, staffLast]);
        (await IdleAsync(ConversationStatus.Ai, null, 10)).ShouldBe([withAgent]);
    }

    [Fact]
    public async Task TryUpdateStatus_changes_only_the_expected_status_of_a_conversation_quiet_since_the_cutoff()
    {
        var conversationId = await AddWidgetConversationAsync(Guid.CreateVersion7(), ConversationStatus.Ai, BaseTime, MessageAuthor.Assistant);

        Task<bool> TryAsync(ConversationStatus expected, ConversationStatus status, DateTimeOffset? updatedBefore) =>
            InScopeAsync((IConversationRepository conversations) => conversations.TryUpdateStatusAsync(conversationId, expected, status, updatedBefore, Ct));

        (await TryAsync(ConversationStatus.Human, ConversationStatus.Closed, null)).ShouldBeFalse("it has another status");
        (await TryAsync(ConversationStatus.Ai, ConversationStatus.Closed, BaseTime)).ShouldBeFalse("it was updated at the cutoff");
        var closedAt = Tick();
        (await TryAsync(ConversationStatus.Ai, ConversationStatus.Closed, BaseTime.AddMinutes(1))).ShouldBeTrue();
        var closed = await GetConversationAsync(conversationId);
        closed.Status.ShouldBe(ConversationStatus.Closed);
        closed.UpdatedAt.ShouldBe(closedAt);
        (await TryAsync(ConversationStatus.Closed, ConversationStatus.Ai, null)).ShouldBeTrue();
        (await InScopeAsync((IConversationRepository conversations) =>
            conversations.TryUpdateStatusAsync(Guid.NewGuid(), ConversationStatus.Ai, ConversationStatus.Closed, cancellationToken: Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task TryEndPending_ends_a_request_for_a_person_only_while_nobody_has_joined()
    {
        var conversationId = await AddWidgetConversationAsync(Guid.CreateVersion7(), ConversationStatus.HandoverPending, BaseTime, MessageAuthor.System);
        var pending = await InScopeAsync((IHandoverRepository handovers) => handovers.AddAsync(NewHandover(conversationId, HandoverStatus.Pending, BaseTime), Ct));
        var active = await InScopeAsync((IHandoverRepository handovers) => handovers.AddAsync(NewHandover(conversationId, HandoverStatus.Active, BaseTime), Ct));
        var endedAt = Tick();

        pending.Status = HandoverStatus.Expired;
        pending.ResolvedAt = endedAt;
        pending.UpdatedAt = endedAt;
        active.Status = HandoverStatus.Cancelled;

        (await InScopeAsync((IHandoverRepository handovers) => handovers.TryEndPendingAsync(pending, Ct))).ShouldBeTrue();
        (await InScopeAsync((IHandoverRepository handovers) => handovers.TryEndPendingAsync(pending, Ct))).ShouldBeFalse("it ended already");
        (await InScopeAsync((IHandoverRepository handovers) => handovers.TryEndPendingAsync(active, Ct))).ShouldBeFalse("someone joined");
        var expired = (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(pending.Id, Ct))).ShouldNotBeNull();
        (expired.Status, expired.ResolvedAt, expired.UpdatedAt).ShouldBe((HandoverStatus.Expired, (DateTimeOffset?)endedAt, endedAt));
        (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(active.Id, Ct))).ShouldNotBeNull().Status.ShouldBe(HandoverStatus.Active);
        (await InScopeAsync((IHandoverRepository handovers) => handovers.FindOpenAsync(conversationId, Ct))).ShouldNotBeNull().Id.ShouldBe(active.Id);
    }

    [Fact]
    public async Task Saving_the_status_of_a_request_and_where_it_went_never_undoes_the_other()
    {
        var conversationId = await AddWidgetConversationAsync(Guid.CreateVersion7(), ConversationStatus.HandoverPending, BaseTime, MessageAuthor.System);
        var added = await InScopeAsync((IHandoverRepository handovers) => handovers.AddAsync(NewHandover(conversationId, HandoverStatus.Pending, BaseTime), Ct));

        // A job posting the request to Slack and a person taking it both loaded it while it was pending.
        var posting = (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(added.Id, Ct))).ShouldNotBeNull();
        var taking = (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(added.Id, Ct))).ShouldNotBeNull();
        taking.Status = HandoverStatus.Active;
        taking.Assignee = "Dana";
        taking.AcceptedAt = Tick();
        await InScopeAsync((IHandoverRepository handovers) => handovers.SaveStatusAsync(taking, Ct));
        posting.SlackChannelId = "C0123456789";
        posting.SlackThreadTs = "1700000000.000100";
        await InScopeAsync((IHandoverRepository handovers) => handovers.SaveDeliveryAsync(posting, Ct));

        var stored = (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(added.Id, Ct))).ShouldNotBeNull();
        (stored.Status, stored.Assignee, stored.AcceptedAt).ShouldBe((HandoverStatus.Active, "Dana", taking.AcceptedAt));
        (stored.SlackChannelId, stored.SlackThreadTs).ShouldBe(("C0123456789", "1700000000.000100"));
    }

    [Fact]
    public async Task UpdateTitle_changes_only_the_title()
    {
        var conversation = NewConversation(sourceIds: [Guid.CreateVersion7()]);
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        await InScopeAsync((IConversationRepository conversations) => conversations.UpdateTitleAsync(conversation.Id, "Renamed ✓", Ct));

        var loaded = await GetConversationAsync(conversation.Id);
        loaded.Title.ShouldBe("Renamed ✓");
        loaded.UpdatedAt.ShouldBe(conversation.UpdatedAt);
        loaded.SystemPrompt.ShouldBe(conversation.SystemPrompt);
        loaded.SourceIds.ShouldBe(conversation.SourceIds);
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            InScopeAsync((IConversationRepository conversations) => conversations.UpdateTitleAsync(Guid.NewGuid(), "x", Ct)));
    }

    [Fact]
    public async Task The_active_specialist_and_staff_only_notices_are_stored()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        var specialistId = Guid.CreateVersion7();

        await InScopeAsync((IConversationRepository conversations) => conversations.SetActiveAgentAsync(conversation.Id, specialistId, "Billing", Ct));
        var handedOff = await GetConversationAsync(conversation.Id);
        handedOff.UpdatedAt.ShouldBe(conversation.UpdatedAt, "a hand-off is part of the turn, not activity of its own");
        handedOff.Title.ShouldBe(conversation.Title);

        var notice = NewMessage(conversation.Id, 0, MessageAuthor.System, BaseTime);
        notice.StaffOnly = true;
        await InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(notice, Ct));

        var loaded = await GetConversationAsync(conversation.Id);
        (loaded.ActiveAgentId, loaded.ActiveAgentName).ShouldBe((specialistId, "Billing"));
        loaded.Messages.ShouldHaveSingleItem().StaffOnly.ShouldBeTrue();

        await InScopeAsync((IConversationRepository conversations) => conversations.SetActiveAgentAsync(conversation.Id, null, "Billing", Ct));
        var back = await GetConversationAsync(conversation.Id);
        (back.ActiveAgentId, back.ActiveAgentName).ShouldBe(((Guid?)null, (string?)null));
    }

    [Fact]
    public async Task Agent_specialists_round_trip()
    {
        var billing = new KnowledgeAgent.Features.Agents.AgentConfig { Id = Guid.CreateVersion7(), Key = $"billing-{UniqueToken()}".ToLowerInvariant(), Name = "Billing", CreatedAt = BaseTime, UpdatedAt = BaseTime };
        var front = new KnowledgeAgent.Features.Agents.AgentConfig
        {
            Id = Guid.CreateVersion7(),
            Key = $"front-{UniqueToken()}".ToLowerInvariant(),
            Name = "Front",
            Specialists = [new KnowledgeAgent.Features.Agents.AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices ✓" }],
            ShowSpecialistName = true,
            CreatedAt = BaseTime,
            UpdatedAt = BaseTime,
        };
        await InScopeAsync(async (KnowledgeDbContext db) =>
        {
            db.Set<KnowledgeAgent.Features.Agents.AgentConfig>().AddRange(billing, front);
            return await db.SaveChangesAsync(Ct);
        });

        var loaded = (await InScopeAsync((KnowledgeDbContext db) => db.Set<KnowledgeAgent.Features.Agents.AgentConfig>().AsNoTracking().SingleOrDefaultAsync(agent => agent.Id == front.Id, Ct))).ShouldNotBeNull();
        var other = (await InScopeAsync((KnowledgeDbContext db) => db.Set<KnowledgeAgent.Features.Agents.AgentConfig>().AsNoTracking().SingleOrDefaultAsync(agent => agent.Id == billing.Id, Ct))).ShouldNotBeNull();

        loaded.ShowSpecialistName.ShouldBeTrue();
        loaded.Specialists.Select(specialist => (specialist.AgentId, specialist.WhenToUse)).ShouldBe([(billing.Id, "Invoices ✓")]);
        other.Specialists.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetPinned_changes_only_the_pin()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        (await InScopeAsync((IConversationRepository conversations) => conversations.SetPinnedAsync(conversation.Id, true, Ct))).ShouldBeTrue();

        var loaded = await GetConversationAsync(conversation.Id);
        loaded.Pinned.ShouldBeTrue();
        loaded.UpdatedAt.ShouldBe(conversation.UpdatedAt, "pinning does not count as activity");
        loaded.Title.ShouldBe(conversation.Title);
        (await InScopeAsync((IConversationRepository conversations) => conversations.SetPinnedAsync(Guid.NewGuid(), true, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Labels_are_listed_by_name_with_their_counts_and_names_are_unique_ignoring_case()
    {
        var token = UniqueToken();
        var beta = await AddLabelAsync($"{token} beta", autoApply: true);
        var alpha = await AddLabelAsync($"{token} Alpha");
        await Should.ThrowAsync<LabelNameTakenException>(() => AddLabelAsync($"{token} ALPHA"));
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(conversation.Id, [alpha.Id], BaseTime, Ct));

        var listed = (await InScopeAsync((ILabelRepository labels) => labels.ListAsync(Ct))).Where(label => label.Name.StartsWith(token, StringComparison.Ordinal)).ToList();

        listed.Select(label => (label.Name, label.ConversationCount, label.AutoApply)).ShouldBe([($"{token} Alpha", 1, false), ($"{token} beta", 0, true)]);
        (await InScopeAsync((ILabelRepository labels) => labels.NameTakenAsync($" {token} BETA ", null, Ct))).ShouldBeTrue();
        (await InScopeAsync((ILabelRepository labels) => labels.NameTakenAsync($"{token} beta", beta.Id, Ct))).ShouldBeFalse();
        (await InScopeAsync((ILabelRepository labels) => labels.ListAutoApplyAsync(Ct))).ShouldContain(label => label.Id == beta.Id);

        beta.Name = $"{token} alpha";
        await Should.ThrowAsync<LabelNameTakenException>(() => InScopeAsync((ILabelRepository labels) => labels.UpdateAsync(beta, Ct)));
        beta.Name = $"{token} Gamma";
        beta.Color = "teal";
        await InScopeAsync((ILabelRepository labels) => labels.UpdateAsync(beta, Ct));
        var renamed = (await InScopeAsync((ILabelRepository labels) => labels.GetAsync(beta.Id, Ct))).ShouldNotBeNull();
        (renamed.Name, renamed.NormalizedName, renamed.Color).ShouldBe(($"{token} Gamma", $"{token} GAMMA".ToUpperInvariant(), "teal"));
    }

    [Fact]
    public async Task Setting_labels_keeps_auto_labels_and_remembers_the_auto_labels_staff_removed()
    {
        var token = UniqueToken();
        var question = await AddLabelAsync($"{token} question", autoApply: true);
        var problem = await AddLabelAsync($"{token} problem", autoApply: true);
        var billing = await AddLabelAsync($"{token} billing");
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        (await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(conversation.Id, [question.Id, problem.Id], BaseTime, Ct))).ShouldBeTrue();
        (await LabelsOfAsync(conversation.Id)).ShouldBe([($"{token} problem", "auto"), ($"{token} question", "auto")]);
        (await GetConversationAsync(conversation.Id)).AutoLabeledAt.ShouldBe(BaseTime);

        var set = await InScopeAsync((ILabelRepository labels) => labels.SetAsync(conversation.Id, [question.Id, billing.Id], BaseTime, Ct));
        set.Select(label => (label.Name, label.Source)).ShouldBe([($"{token} billing", "manual"), ($"{token} question", "auto")]);
        (await InScopeAsync((ILabelRepository labels) => labels.ListRowsAsync(conversation.Id, Ct)))
            .Single(row => row.LabelId == problem.Id).Removed.ShouldBeTrue("the job must not put it back");

        // A second run replaces the auto labels but never brings back a removed one or takes over a manual one.
        await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(conversation.Id, [problem.Id, billing.Id], BaseTime.AddHours(1), Ct));
        (await LabelsOfAsync(conversation.Id)).ShouldBe([($"{token} billing", "manual")]);

        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(conversation.Id, [problem.Id, billing.Id], BaseTime, Ct));
        (await LabelsOfAsync(conversation.Id)).ShouldBe([($"{token} billing", "manual"), ($"{token} problem", "manual")]);

        await Should.ThrowAsync<KeyNotFoundException>(() => InScopeAsync((ILabelRepository labels) => labels.SetAsync(Guid.NewGuid(), [billing.Id], BaseTime, Ct)));
        (await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(Guid.NewGuid(), [billing.Id], BaseTime, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_label_or_its_conversation_takes_the_label_off()
    {
        var token = UniqueToken();
        var kept = await AddLabelAsync($"{token} kept");
        var deleted = await AddLabelAsync($"{token} deleted");
        var first = NewConversation();
        var second = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(first, Ct));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(second, Ct));
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(first.Id, [kept.Id, deleted.Id], BaseTime, Ct));
        await InScopeAsync((ILabelRepository labels) => labels.SetAsync(second.Id, [kept.Id], BaseTime, Ct));

        (await InScopeAsync((ILabelRepository labels) => labels.DeleteAsync(deleted.Id, Ct))).ShouldBeTrue();
        (await InScopeAsync((ILabelRepository labels) => labels.DeleteAsync(deleted.Id, Ct))).ShouldBeFalse();
        (await LabelsOfAsync(first.Id)).ShouldBe([($"{token} kept", "manual")]);

        (await InScopeAsync((IConversationRepository conversations) => conversations.DeleteAsync(first.Id, Ct))).ShouldBeTrue();
        (await InScopeAsync((ILabelRepository labels) => labels.ListAsync(Ct))).Single(label => label.Id == kept.Id).ConversationCount.ShouldBe(1);
    }

    [Fact]
    public async Task ResetAutoLabeled_marks_recent_conversations_with_messages_most_recent_first()
    {
        // Far ahead of the other tests' conversations, so only these are recent enough; deleted at the end.
        var since = new DateTimeOffset(2300, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var widgetId = Guid.NewGuid();
        var older = await AddWidgetConversationAsync(widgetId, ConversationStatus.Closed, since.AddHours(1), MessageAuthor.Assistant);
        var newer = await AddWidgetConversationAsync(widgetId, ConversationStatus.Closed, since.AddHours(2), MessageAuthor.Assistant);
        var empty = NewConversation(since.AddHours(3));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(empty, Ct));
        var stale = await AddWidgetConversationAsync(widgetId, ConversationStatus.Closed, since.AddHours(-1), MessageAuthor.Assistant);
        foreach (var id in new[] { older, newer, stale })
        {
            await InScopeAsync((ILabelRepository labels) => labels.ApplyAutoAsync(id, [], BaseTime, Ct));
        }

        (await InScopeAsync((ILabelRepository labels) => labels.ResetAutoLabeledAsync(since, 1, Ct))).ShouldBe([newer]);
        (await InScopeAsync((ILabelRepository labels) => labels.ResetAutoLabeledAsync(since, 10, Ct))).ShouldBe([newer, older]);

        (await GetConversationAsync(older)).AutoLabeledAt.ShouldBeNull();
        (await GetConversationAsync(stale)).AutoLabeledAt.ShouldBe(BaseTime);

        // The other tests list the most recent conversations, so these go.
        foreach (var id in new[] { older, newer, empty.Id, stale })
        {
            await InScopeAsync((IConversationRepository conversations) => conversations.DeleteAsync(id, Ct));
        }
    }

    [Fact]
    public async Task Delete_removes_the_conversation_and_its_turns()
    {
        var conversation = NewConversation();
        conversation.Turns.AddRange([NewTurn(conversation.Id), NewTurn(conversation.Id)]);
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var deleted = await InScopeAsync((IConversationRepository conversations) => conversations.DeleteAsync(conversation.Id, Ct));
        var deletedAgain = await InScopeAsync((IConversationRepository conversations) => conversations.DeleteAsync(conversation.Id, Ct));

        deleted.ShouldBeTrue();
        deletedAgain.ShouldBeFalse();
        (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(conversation.Id, true, Ct))).ShouldBeNull();
        (await InScopeAsync((KnowledgeDbContext db) => db.ConversationTurns.CountAsync(turn => turn.ConversationId == conversation.Id, Ct)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task ModelMessagesJson_round_trips_byte_for_byte()
    {
        // Key order, duplicate keys, whitespace, escapes and number formatting would all be normalized by jsonb.
        const string modelMessages = """
            [
              {"role":"assistant",   "contents":[{"$type":"reasoning","text":"Let me think…","protectedData":"EuYBCkQIBhgCIkB/+=="},
                {"$type":"functionCall","callId":"toolu_01","name":"search_knowledge_base","arguments":{"zeta":1,"alpha":"a","topK":6}}]},
            	{"zeta" : 1.50, "alpha":2, "dup":1, "dup":2, "esc":"é\n\"q\"\/", "emoji":"🦓", "empty":{}  }
            ]

            """;
        const string toolCalls = """[{"callId":"toolu_01","toolName":"search_knowledge_base","agent":"orchestrator","parentCallId":null,"arguments":{"query":"zebras"},"summary":"3 hits","isError":false,"durationMs":12}]""";
        const string citations = """[{"ref":"S1","sourceId":"0199a8f2-4c1e-7b3a-9d5e-123456789abc","snippet":"Zebras…","score":0.87}]""";
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        var turn = NewTurn(conversation.Id);
        turn.ModelMessagesJson = modelMessages;
        turn.ToolCallsJson = toolCalls;
        turn.CitationsJson = citations;
        await InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(turn, Ct));

        var stored = (await GetConversationAsync(conversation.Id)).Turns.ShouldHaveSingleItem();

        string.Equals(stored.ModelMessagesJson, modelMessages, StringComparison.Ordinal).ShouldBeTrue(
            $"ModelMessagesJson changed:{Environment.NewLine}{stored.ModelMessagesJson}");
        JsonNode.DeepEquals(JsonNode.Parse(stored.ToolCallsJson), JsonNode.Parse(toolCalls)).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse(stored.CitationsJson), JsonNode.Parse(citations)).ShouldBeTrue();
    }

    [Fact]
    public async Task Overrides_support_upsert_get_list_and_delete_with_case_insensitive_names()
    {
        var name = $"profile-{Guid.NewGuid():N}";
        var created = new ModelProfileOverride
        {
            Name = name,
            Provider = "anthropic",
            Model = "claude-opus-5-5",
            MaxOutputTokens = 16_000,
            ReasoningEffort = "high",
            ReasoningOutput = "summary",
        };
        var createdAt = Tick();
        await InScopeAsync((IModelProfileOverrideStore store) => store.UpsertAsync(created, Ct));
        var afterCreate = await InScopeAsync((IModelProfileOverrideStore store) => store.GetAsync(name, Ct));

        var replacement = new ModelProfileOverride
        {
            Name = name.ToUpperInvariant(),
            Provider = "openai",
            Model = "gpt-5-mini",
            Temperature = 0.25f,
            Dimensions = 1536,
        };
        var replacedAt = Tick();
        await InScopeAsync((IModelProfileOverrideStore store) => store.UpsertAsync(replacement, Ct));
        var afterReplace = await InScopeAsync((IModelProfileOverrideStore store) => store.GetAsync(name.ToUpperInvariant(), Ct));
        var listed = await InScopeAsync((IModelProfileOverrideStore store) => store.ListAsync(Ct));

        created.UpdatedAt.ShouldBe(createdAt);
        afterCreate.ShouldNotBeNull().ShouldSatisfyAllConditions(
            stored => stored.Name.ShouldBe(name),
            stored => stored.Provider.ShouldBe("anthropic"),
            stored => stored.Model.ShouldBe("claude-opus-5-5"),
            stored => stored.Temperature.ShouldBeNull(),
            stored => stored.MaxOutputTokens.ShouldBe(16_000),
            stored => stored.ReasoningEffort.ShouldBe("high"),
            stored => stored.ReasoningOutput.ShouldBe("summary"),
            stored => stored.Dimensions.ShouldBeNull(),
            stored => stored.UpdatedAt.ShouldBe(createdAt));
        afterReplace.ShouldNotBeNull().ShouldSatisfyAllConditions(
            stored => stored.Name.ShouldBe(name),
            stored => stored.Provider.ShouldBe("openai"),
            stored => stored.Model.ShouldBe("gpt-5-mini"),
            stored => stored.Temperature.ShouldBe(0.25f),
            stored => stored.MaxOutputTokens.ShouldBeNull(),
            stored => stored.ReasoningEffort.ShouldBeNull(),
            stored => stored.Dimensions.ShouldBe(1536),
            stored => stored.UpdatedAt.ShouldBe(replacedAt));
        listed.Count(stored => string.Equals(stored.Name, name, StringComparison.OrdinalIgnoreCase)).ShouldBe(1);

        (await InScopeAsync((IModelProfileOverrideStore store) => store.DeleteAsync(name.ToUpperInvariant(), Ct))).ShouldBeTrue();
        (await InScopeAsync((IModelProfileOverrideStore store) => store.DeleteAsync(name, Ct))).ShouldBeFalse();
        (await InScopeAsync((IModelProfileOverrideStore store) => store.GetAsync(name, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Find_matches_every_term_in_title_url_or_file_name_case_insensitively()
    {
        var token = UniqueToken();
        var handbook = await AddSearchableAsync($"employee handbook {token}", fileName: "handbook.pdf");
        var holiday = await AddSearchableAsync($"holiday policy {token}", url: $"https://intranet.example.com/{token}/holiday-HANDBOOK");
        var pricing = await AddSearchableAsync($"pricing {token}", fileName: $"pricing-{token}.md");
        await AddSearchableAsync("unrelated notes", url: "https://example.com/other");

        var byTwoTerms = await FindAsync(new SourceQuery(Text: $"Handbook {token}"));
        var byToken = await FindAsync(new SourceQuery(Text: token.ToUpperInvariant()));
        var byFileName = await FindAsync(new SourceQuery(Text: $"pricing-{token}.md"));
        var noMatch = await FindAsync(new SourceQuery(Text: $"{token} missingword"));

        byTwoTerms.Items.Select(source => source.Id).ShouldBe([handbook.Id, holiday.Id]);
        byTwoTerms.Total.ShouldBe(2);
        byToken.Total.ShouldBe(3);
        byToken.Items.Select(source => source.Id).ShouldBe([handbook.Id, holiday.Id, pricing.Id], ignoreOrder: true);
        byFileName.Items.ShouldHaveSingleItem().Id.ShouldBe(pricing.Id);
        noMatch.Total.ShouldBe(0);
        noMatch.Items.ShouldBeEmpty();
        noMatch.NextOffset.ShouldBeNull();
    }

    [Fact]
    public async Task Find_puts_title_prefix_matches_first_then_sorts_by_title()
    {
        var token = UniqueToken();
        var betaNotes = await AddSearchableAsync($"{token} beta notes");
        var alphaInside = await AddSearchableAsync($"alpha {token}");
        var alphaGuide = await AddSearchableAsync($"{token} alpha guide");

        var page = await FindAsync(new SourceQuery(Text: token));

        page.Items.Select(source => source.Id).ShouldBe([alphaGuide.Id, betaNotes.Id, alphaInside.Id]);
    }

    [Fact]
    public async Task Find_treats_like_wildcards_literally()
    {
        var token = UniqueToken();
        var percent = await AddSearchableAsync($"{token} 100% done");
        await AddSearchableAsync($"{token} 1000 items");
        var underscore = await AddSearchableAsync($"{token} a_b");
        await AddSearchableAsync($"{token} axb");

        (await FindAsync(new SourceQuery(Text: $"{token} 100%"))).Items.ShouldHaveSingleItem().Id.ShouldBe(percent.Id);
        (await FindAsync(new SourceQuery(Text: $"{token} a_b"))).Items.ShouldHaveSingleItem().Id.ShouldBe(underscore.Id);
    }

    [Fact]
    public async Task Find_filters_by_type_status_and_ids_and_pages_newest_first()
    {
        var ids = new List<Guid>();
        foreach (var (status, type) in new[]
        {
            (SourceStatus.Ready, SourceType.Pdf), (SourceStatus.Ready, SourceType.Url), (SourceStatus.Failed, SourceType.Pdf),
            (SourceStatus.Ready, SourceType.Pdf), (SourceStatus.Ready, SourceType.Text),
        })
        {
            var source = NewSource(status, Tick(), title: $"paging {Guid.NewGuid():N}");
            source.Type = type;
            ids.Add((await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct))).Id);
        }

        var readyNewestFirst = ids.Where((_, index) => index != 2).Reverse().ToList();
        var first = await FindAsync(new SourceQuery(Statuses: [SourceStatus.Ready], Ids: ids, Limit: 2));
        var second = await FindAsync(new SourceQuery(Statuses: [SourceStatus.Ready], Ids: ids, Offset: 2, Limit: 2));
        var pastTheEnd = await FindAsync(new SourceQuery(Statuses: [SourceStatus.Ready], Ids: ids, Offset: 10));
        var readyPdfs = await FindAsync(new SourceQuery(Type: SourceType.Pdf, Statuses: [SourceStatus.Ready], Ids: ids));
        var anyStatus = await FindAsync(new SourceQuery(Ids: ids, Limit: 1000));

        first.Total.ShouldBe(4);
        first.Items.Select(source => source.Id).ShouldBe(readyNewestFirst.Take(2));
        first.NextOffset.ShouldBe(2);
        second.Items.Select(source => source.Id).ShouldBe(readyNewestFirst.Skip(2));
        second.NextOffset.ShouldBeNull();
        pastTheEnd.Items.ShouldBeEmpty();
        pastTheEnd.Total.ShouldBe(4);
        readyPdfs.Items.Select(source => source.Id).ShouldBe([ids[3], ids[0]]);
        anyStatus.Total.ShouldBe(5);
        anyStatus.Items.Count.ShouldBe(5, "the limit is clamped to SourceQuery.MaxLimit, not rejected");
    }

    [Fact]
    public async Task Find_filters_by_site_and_sites_are_counted_for_a_query()
    {
        var token = UniqueToken();
        await AddOnSiteAsync($"{token} intro", "docs.example.com");
        await AddOnSiteAsync($"{token} reference", "docs.example.com");
        var post = await AddOnSiteAsync($"{token} release post", "blog.example.com");
        await AddOnSiteAsync($"{token} notes", null);

        var onBlog = await FindAsync(new SourceQuery(Text: token, Site: "blog.example.com"));
        var sites = await InScopeAsync((ISourceRepository sources) => sources.CountBySiteAsync(new SourceQuery(Text: token), 10, Ct));
        var top = await InScopeAsync((ISourceRepository sources) => sources.CountBySiteAsync(new SourceQuery(Text: token), 1, Ct));

        onBlog.Items.ShouldHaveSingleItem().Id.ShouldBe(post.Id);
        sites.ShouldBe([new SiteCount("docs.example.com", 2), new SiteCount("blog.example.com", 1)]);
        top.ShouldHaveSingleItem().Site.ShouldBe("docs.example.com");
        (await FindAsync(new SourceQuery(Text: token, Site: "missing.example.com"))).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Stats_count_sources_by_status_and_ready_sources_by_type_and_embedding_model()
    {
        var before = await InScopeAsync((ISourceRepository sources) => sources.GetStatsAsync(Ct));
        var small = $"hash:stats-{Guid.NewGuid():N}:64";
        var large = $"openai:stats-{Guid.NewGuid():N}:1536";

        foreach (var (status, type, chunks, model) in new (SourceStatus, SourceType, int, string?)[]
        {
            (SourceStatus.Ready, SourceType.Url, 5, small), (SourceStatus.Ready, SourceType.Url, 5, small),
            (SourceStatus.Ready, SourceType.Pdf, 7, large), (SourceStatus.Ready, SourceType.Markdown, 2, null),
            (SourceStatus.Pending, SourceType.Pdf, 0, small), (SourceStatus.Failed, SourceType.Text, 0, null),
        })
        {
            var source = NewSource(status, title: $"stats {Guid.NewGuid():N}");
            source.Type = type;
            source.ChunkCount = chunks;
            source.EmbeddingModel = model;
            await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
        }

        var after = await InScopeAsync((ISourceRepository sources) => sources.GetStatsAsync(Ct));

        (after.Count(SourceStatus.Ready) - before.Count(SourceStatus.Ready)).ShouldBe(4);
        (after.Count(SourceStatus.Pending) - before.Count(SourceStatus.Pending)).ShouldBe(1);
        (after.Count(SourceStatus.Failed) - before.Count(SourceStatus.Failed)).ShouldBe(1);
        (after.Count(SourceStatus.Processing) - before.Count(SourceStatus.Processing)).ShouldBe(0);
        (TypeCount(after, SourceType.Url) - TypeCount(before, SourceType.Url)).ShouldBe(2);
        (TypeCount(after, SourceType.Pdf) - TypeCount(before, SourceType.Pdf)).ShouldBe(1);
        (TypeCount(after, SourceType.Markdown) - TypeCount(before, SourceType.Markdown)).ShouldBe(1);
        (TypeCount(after, SourceType.Text) - TypeCount(before, SourceType.Text)).ShouldBe(0, "only ready sources count per type");
        (after.ReadyChunkCount - before.ReadyChunkCount).ShouldBe(19);

        before.ReadyCountByEmbeddingModel.ShouldNotContainKey(small);
        after.ReadyCountByEmbeddingModel[small].ShouldBe(2, "only ready sources count per embedding model");
        after.ReadyCountByEmbeddingModel[large].ShouldBe(1);
        (after.ReadyCountByEmbeddingModel.Values.Sum() - before.ReadyCountByEmbeddingModel.Values.Sum())
            .ShouldBe(3, "ready sources without an embedding model are not counted per model");
        after.ReadyCountByEmbeddingModel.Keys.ShouldAllBe(key => !string.IsNullOrEmpty(key));

        static int TypeCount(SourceStats stats, SourceType type) => stats.ReadyCountByType.GetValueOrDefault(type);
    }

    [Fact]
    public async Task Provider_settings_support_upsert_get_list_and_delete_with_case_insensitive_keys()
    {
        var key = $"provider-{Guid.NewGuid():N}";
        var created = new ProviderSettingsOverride
        {
            Key = key,
            ProtectedApiKey = "CfDJ8-protected-payload",
            ApiKeyHint = "…a1b2",
            Endpoint = "https://proxy.example.com",
            DefaultChatModel = "claude-opus-5-5",
        };
        var createdAt = Tick();
        await InScopeAsync((IProviderSettingsStore store) => store.UpsertAsync(created, Ct));
        var afterCreate = await InScopeAsync((IProviderSettingsStore store) => store.GetAsync(key.ToUpperInvariant(), Ct));

        var replacement = new ProviderSettingsOverride
        {
            Key = key.ToUpperInvariant(),
            DefaultEmbeddingModel = "text-embedding-3-small",
            DefaultEmbeddingDimensions = 1536,
        };
        var replacedAt = Tick();
        await InScopeAsync((IProviderSettingsStore store) => store.UpsertAsync(replacement, Ct));
        var afterReplace = await InScopeAsync((IProviderSettingsStore store) => store.GetAsync(key, Ct));
        var listed = await InScopeAsync((IProviderSettingsStore store) => store.ListAsync(Ct));

        created.UpdatedAt.ShouldBe(createdAt);
        afterCreate.ShouldNotBeNull().ShouldSatisfyAllConditions(
            stored => stored.Key.ShouldBe(key),
            stored => stored.ProtectedApiKey.ShouldBe("CfDJ8-protected-payload"),
            stored => stored.ApiKeyHint.ShouldBe("…a1b2"),
            stored => stored.Endpoint.ShouldBe("https://proxy.example.com"),
            stored => stored.DefaultChatModel.ShouldBe("claude-opus-5-5"),
            stored => stored.UpdatedAt.ShouldBe(createdAt));
        afterReplace.ShouldNotBeNull().ShouldSatisfyAllConditions(
            stored => stored.Key.ShouldBe(key),
            stored => stored.ProtectedApiKey.ShouldBeNull(),
            stored => stored.Endpoint.ShouldBeNull(),
            stored => stored.DefaultEmbeddingModel.ShouldBe("text-embedding-3-small"),
            stored => stored.DefaultEmbeddingDimensions.ShouldBe(1536),
            stored => stored.UpdatedAt.ShouldBe(replacedAt));
        listed.Count(stored => string.Equals(stored.Key, key, StringComparison.OrdinalIgnoreCase)).ShouldBe(1);

        (await InScopeAsync((IProviderSettingsStore store) => store.DeleteAsync(key.ToUpperInvariant(), Ct))).ShouldBeTrue();
        (await InScopeAsync((IProviderSettingsStore store) => store.DeleteAsync(key, Ct))).ShouldBeFalse();
        (await InScopeAsync((IProviderSettingsStore store) => store.GetAsync(key, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task The_data_protection_key_ring_is_stored_in_the_database()
    {
        var protector = Fixture.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("persistence-tests");
        var secret = $"secret-{Guid.NewGuid():N}";

        var protectedValue = protector.Protect(secret);

        protectedValue.ShouldNotContain(secret);
        protector.Unprotect(protectedValue).ShouldBe(secret);
        await using var scope = Fixture.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().DataProtectionKeys.CountAsync(Ct)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Overrides_are_listed_by_name()
    {
        var suffix = Guid.NewGuid().ToString("N");
        foreach (var name in new[] { $"b-{suffix}", $"A-{suffix}", $"c-{suffix}" })
        {
            await InScopeAsync((IModelProfileOverrideStore store) =>
                store.UpsertAsync(new ModelProfileOverride { Name = name, Provider = "auto" }, Ct));
        }

        var listed = await InScopeAsync((IModelProfileOverrideStore store) => store.ListAsync(Ct));

        listed.Select(stored => stored.Name).Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ShouldBe(new[] { $"A-{suffix}", $"b-{suffix}", $"c-{suffix}" });
    }

    [Fact]
    public async Task Repositories_sharing_a_scope_can_be_called_concurrently()
    {
        // Agent tools running in parallel within one request share that scope's DbContext.
        await using var scope = Fixture.Services.CreateAsyncScope();
        var sources = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var overrides = scope.ServiceProvider.GetRequiredService<IModelProfileOverrideStore>();
        var source = await sources.AddAsync(NewSource(), new SourceContent { ExtractedText = "text" }, Ct);
        var marker = Guid.NewGuid().ToString("N");

        var calls = Enumerable.Range(0, 8).SelectMany(i => new Func<Task>[]
        {
            () => sources.GetAsync(source.Id, Ct),
            () => sources.GetContentAsync(source.Id, Ct),
            () => sources.ListByStatusAsync([SourceStatus.Pending], Ct),
            () => sources.AddAsync(NewSource(title: $"{marker} {i}"), cancellationToken: Ct),
            () => conversations.GetAsync(Guid.NewGuid(), includeTurns: false, Ct),
            () => overrides.ListAsync(Ct),
        });
        await Task.WhenAll(calls.Select(call => Task.Run(call, Ct)));

        (await sources.ListAsync(Ct)).Count(stored => stored.Title.StartsWith(marker, StringComparison.Ordinal)).ShouldBe(8);
    }

    [Fact]
    public async Task Database_health_check_reports_healthy()
    {
        var healthChecks = Fixture.Services.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(registration => registration.Name == nameof(KnowledgeDbContext), Ct);

        report.Entries.ShouldContainKey(nameof(KnowledgeDbContext));
        report.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Eval_runs_round_trip_and_list_newest_first_per_dataset()
    {
        var dataset = UniqueToken();
        var older = EvalRun(dataset, BaseTime.AddYears(50), EvalRunStatusNames.Completed);
        var newer = EvalRun(dataset, BaseTime.AddYears(50).AddMinutes(1), EvalRunStatusNames.Running);
        older.ItemsJson = """[{"itemId":"a","metrics":{"answer_fact_recall":0.5}}]""";
        older.GateJson = """{"passed":true,"checks":[]}""";
        older.Judge = true;
        older.LangfuseUrl = "https://langfuse.example/project/p/datasets/d/runs/r";
        await InScopeAsync((IEvalRunRepository runs) => runs.AddAsync(older, Ct));
        await InScopeAsync((IEvalRunRepository runs) => runs.AddAsync(newer, Ct));

        var loaded = await InScopeAsync((IEvalRunRepository runs) => runs.GetAsync(older.Id, Ct));
        var listed = await InScopeAsync((IEvalRunRepository runs) => runs.ListAsync(10, dataset, Ct));

        loaded.ShouldNotBeNull().ShouldSatisfyAllConditions(
            run => run.Name.ShouldBe(older.Name),
            run => run.Judge.ShouldBeTrue(),
            run => run.ItemsJson.ShouldNotBeNull().ShouldContain("answer_fact_recall"),
            run => run.GateJson.ShouldNotBeNull().ShouldContain("passed"),
            run => run.LangfuseUrl.ShouldBe(older.LangfuseUrl),
            run => run.CreatedAt.ShouldBe(older.CreatedAt));
        listed.Select(run => run.Id).ShouldBe([newer.Id, older.Id]);
        (await InScopeAsync((IEvalRunRepository runs) => runs.ListAsync(1, dataset, Ct))).Single().Id.ShouldBe(newer.Id);
    }

    [Fact]
    public async Task Eval_runs_update_by_status_and_delete()
    {
        var dataset = UniqueToken();
        var running = EvalRun(dataset, BaseTime, EvalRunStatusNames.Running);
        var queued = EvalRun(dataset, BaseTime.AddMinutes(1), EvalRunStatusNames.Queued);
        await InScopeAsync((IEvalRunRepository runs) => runs.AddAsync(running, Ct));
        await InScopeAsync((IEvalRunRepository runs) => runs.AddAsync(queued, Ct));

        var active = await InScopeAsync((IEvalRunRepository runs) => runs.ListByStatusAsync([EvalRunStatusNames.Running, EvalRunStatusNames.Queued], Ct));
        active.Where(run => run.Dataset == dataset).Select(run => run.Id).ShouldBe([running.Id, queued.Id]);

        running.Status = EvalRunStatusNames.Completed;
        running.CompletedItems = 3;
        running.MetricsJson = """{"citation_validity":{"mean":1,"count":3}}""";
        await InScopeAsync((IEvalRunRepository runs) => runs.UpdateAsync(running, Ct));
        var updated = await InScopeAsync((IEvalRunRepository runs) => runs.GetAsync(running.Id, Ct));
        updated.ShouldNotBeNull().Status.ShouldBe(EvalRunStatusNames.Completed);
        updated.CompletedItems.ShouldBe(3);
        updated.MetricsJson.ShouldNotBeNull().ShouldContain("citation_validity");
        updated.UpdatedAt.ShouldBeGreaterThan(running.CreatedAt);

        (await InScopeAsync((IEvalRunRepository runs) => runs.DeleteAsync(queued.Id, Ct))).ShouldBeTrue();
        (await InScopeAsync((IEvalRunRepository runs) => runs.DeleteAsync(queued.Id, Ct))).ShouldBeFalse();
        await Should.ThrowAsync<KeyNotFoundException>(() => InScopeAsync((IEvalRunRepository runs) => runs.UpdateAsync(queued, Ct)));
    }

    private static EvalRunRecord EvalRun(string dataset, DateTimeOffset createdAt, string status) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = $"{dataset} run",
        Dataset = dataset,
        DatasetSource = "builtin",
        Status = status,
        TotalItems = 3,
        RequestJson = $$"""{"dataset":"{{dataset}}"}""",
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    private static class EvalRunStatusNames
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string Completed = "completed";
    }

    [Fact]
    public async Task Staff_members_are_recorded_when_seen_and_refreshed_when_their_profile_changes()
    {
        var id = "kc-" + Guid.NewGuid().ToString("N");
        var firstSeen = Tick();
        await InScopeAsync((IStaffDirectory staff) => staff.SeenAsync(StaffPrincipal(id, "Dana Novak", AuthRoles.Support), Ct));
        await InScopeAsync((IStaffDirectory staff) => staff.SeenAsync(StaffPrincipal("kc-" + Guid.NewGuid().ToString("N"), "No Role"), Ct));

        Tick();
        await InScopeAsync((IStaffDirectory staff) => staff.SeenAsync(StaffPrincipal(id, "Dana Nováková", AuthRoles.Admin, AuthRoles.Support), Ct));

        var member = (await InScopeAsync((IStaffDirectory staff) => staff.GetAsync(id, Ct))).ShouldNotBeNull();
        member.ShouldSatisfyAllConditions(
            () => member.Name.ShouldBe("Dana Nováková"),
            () => member.Email.ShouldBe($"{id}@shop.test"),
            () => member.Role.ShouldBe(AuthRoles.Admin),
            () => member.FirstSeenAt.ShouldBe(firstSeen),
            () => member.LastSeenAt.ShouldBe(Fixture.Time.GetUtcNow()));
        var listed = await InScopeAsync((IStaffDirectory staff) => staff.ListAsync(Ct));
        listed.ShouldContain(item => item.Id == id && item.Name == "Dana Nováková");
        listed.ShouldNotContain(item => item.Name == "No Role", "only admins and support staff can be assigned");
    }

    [Fact]
    public async Task Read_state_only_moves_forward_and_mark_unread_goes_back_to_the_latest_visitor_or_agent_message()
    {
        var user = "kc-" + Guid.NewGuid().ToString("N");
        var conversation = NewConversation(Tick());
        conversation.Channel = ConversationChannel.Widget;
        conversation.Messages.Add(NewMessage(conversation.Id, 1, MessageAuthor.Visitor, conversation.UpdatedAt));
        conversation.Messages.Add(NewMessage(conversation.Id, 2, MessageAuthor.Assistant, conversation.UpdatedAt));
        conversation.Messages.Add(NewMessage(conversation.Id, 3, MessageAuthor.Staff, conversation.UpdatedAt));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(user, conversation.Id, 1, Ct))).ShouldBe(new ConversationReadDto(1, true));
        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(user, conversation.Id, null, Ct))).ShouldBe(new ConversationReadDto(3, false));
        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(user, conversation.Id, 2, Ct))).ShouldBe(new ConversationReadDto(3, false), "a late request does not undo a newer one");
        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(user, conversation.Id, 99, Ct))).ShouldBe(new ConversationReadDto(3, false), "messages that do not exist yet are not read");
        (await InScopeAsync((IConversationReads reads) => reads.MarkUnreadAsync(user, conversation.Id, Ct))).ShouldBe(new ConversationReadDto(1, true));
        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync("someone-else", conversation.Id, null, Ct))).ShouldBe(new ConversationReadDto(3, false));
        (await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(user, Guid.NewGuid(), null, Ct))).ShouldBeNull();
        (await InScopeAsync((IConversationReads reads) => reads.MarkUnreadAsync(user, Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Lists_and_counts_follow_the_assignee_and_what_the_viewer_read()
    {
        var widgetId = Guid.NewGuid();
        var viewer = "kc-" + Guid.NewGuid().ToString("N");
        await InScopeAsync((IStaffDirectory staff) => staff.SeenAsync(StaffPrincipal(viewer, "Dana Novak", AuthRoles.Support), Ct));

        // From before the viewer first signed in: never unread to them.
        var old = Fixture.Time.GetUtcNow().AddDays(-1);
        var slack = await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, old, MessageAuthor.Visitor);
        var closed = await AddWidgetConversationAsync(widgetId, ConversationStatus.Closed, old, MessageAuthor.Visitor);
        var agent = await AddWidgetConversationAsync(widgetId, ConversationStatus.Ai, old, MessageAuthor.Assistant);
        var mine = await AddWidgetConversationAsync(widgetId, ConversationStatus.HandoverPending, Tick(), MessageAuthor.Visitor);
        var waiting = await AddWidgetConversationAsync(widgetId, ConversationStatus.Human, Tick(), MessageAuthor.Visitor);
        await InScopeAsync((IHandoverRepository handovers) => handovers.SetAssigneeAsync(mine, viewer, "Dana Novak", Tick(), Ct));
        await InScopeAsync((IHandoverRepository handovers) => handovers.SetAssigneeAsync(closed, viewer, "Dana Novak", Tick(), Ct));
        await InScopeAsync((IHandoverRepository handovers) => handovers.SetAssigneeAsync(slack, null, "Sam from Slack", Tick(), Ct));

        var all = await ListAsync(new ConversationSearchQuery { WidgetId = widgetId, ViewerId = viewer });
        all.Items.Single(item => item.Id == mine).ShouldSatisfyAllConditions(
            item => item.Assignee.ShouldBe(new ConversationAssigneeDto(viewer, "Dana Novak")),
            item => item.Unread.ShouldBeTrue());
        all.Items.Single(item => item.Id == slack).ShouldSatisfyAllConditions(
            item => item.Assignee.ShouldBe(new ConversationAssigneeDto(null, "Sam from Slack")),
            item => item.Unread.ShouldBeFalse("its messages came before the viewer first signed in"));
        all.Items.Single(item => item.Id == agent).Assignee.ShouldBeNull();
        (await ListAsync(new ConversationSearchQuery { WidgetId = widgetId })).Items.ShouldAllBe(item => !item.Unread, "nothing is unread without a viewer");

        var open = new[] { ConversationStatus.Ai, ConversationStatus.HandoverPending, ConversationStatus.Human };
        (await ListAsync(new ConversationSearchQuery { WidgetId = widgetId, AssigneeId = viewer, Statuses = open })).Items.Select(item => item.Id).ShouldBe([mine]);
        (await ListAsync(new ConversationSearchQuery { WidgetId = widgetId, Unassigned = true, Statuses = [ConversationStatus.HandoverPending, ConversationStatus.Human] }))
            .Items.Select(item => item.Id).ShouldBe([waiting]);

        var counts = await InScopeAsync((IConversationCounts repository) => repository.CountAsync(new ConversationCountQuery { WidgetId = widgetId, ViewerId = viewer }, Ct));
        counts.ShouldSatisfyAllConditions(
            () => counts.Mine.ShouldBe(1, "the closed one is not open"),
            () => counts.Unassigned.ShouldBe(1, "a name from Slack counts as owned, and the agent's conversation needs nobody"),
            () => counts.Unread.ShouldBe(new UnreadCountsDto(HandoverPending: 1, Human: 1, Ai: 0, Closed: 0, All: 2, Pinned: 0, Mine: 1, Unassigned: 1)));

        await InScopeAsync((IConversationReads reads) => reads.MarkReadAsync(viewer, mine, null, Ct));
        (await ListAsync(new ConversationSearchQuery { WidgetId = widgetId, ViewerId = viewer, Unread = true })).Items.Select(item => item.Id).ShouldBe([waiting]);
        (await ListAsync(new ConversationSearchQuery { WidgetId = widgetId, ViewerId = viewer, Unread = false })).Items.Count.ShouldBe(4);
        var after = await InScopeAsync((IConversationCounts repository) => repository.CountAsync(new ConversationCountQuery { WidgetId = widgetId, ViewerId = viewer }, Ct));
        (after.Unread.All, after.Unread.Mine).ShouldBe((1, 0));

        await InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(NewMessage(mine, 0, MessageAuthor.Visitor, Tick()), Ct));
        (await ListAsync(new ConversationSearchQuery { Id = mine, ViewerId = viewer })).Items.Single().Unread.ShouldBeTrue();

        await InScopeAsync((IHandoverRepository handovers) => handovers.SetAssigneeAsync(mine, null, null, null, Ct));
        (await ListAsync(new ConversationSearchQuery { Id = mine, ViewerId = viewer })).Items.Single().Assignee.ShouldBeNull();
        (await InScopeAsync((IHandoverRepository handovers) => handovers.SetAssigneeAsync(Guid.NewGuid(), viewer, "Dana", Tick(), Ct))).ShouldBeFalse();
    }

    protected Task<ConversationListPageDto> ListAsync(ConversationSearchQuery query) =>
        InScopeAsync((IConversationSearch search) => search.ListAsync(query, Ct));

    protected static ClaimsPrincipal StaffPrincipal(string id, string name, params string[] roles) =>
        new(new ClaimsIdentity(
            [new Claim("sub", id), new Claim("name", name), new Claim("email", $"{id}@shop.test"), .. roles.Select(role => new Claim("roles", role))],
            "test",
            "name",
            "roles"));

    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected async Task<Conversation> GetConversationAsync(Guid id) =>
        (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(id, true, Ct))).ShouldNotBeNull();

    protected Task<Label> AddLabelAsync(string name, bool autoApply = false) =>
        InScopeAsync((ILabelRepository labels) => labels.AddAsync(
            new Label { Name = name, Color = "blue", Description = $"About {name}", AutoApply = autoApply, CreatedAt = BaseTime, UpdatedAt = BaseTime },
            Ct));

    protected async Task<List<(string Name, string Source)>> LabelsOfAsync(Guid conversationId)
    {
        var labels = await InScopeAsync((ILabelRepository repository) => repository.ForConversationsAsync([conversationId], Ct));
        return labels.TryGetValue(conversationId, out var found) ? found.Select(label => (label.Name, label.Source)).ToList() : [];
    }

    protected static string UniqueToken() => "tk" + Guid.NewGuid().ToString("N")[..10];

    protected Task<SourcePage> FindAsync(SourceQuery query) =>
        InScopeAsync((ISourceRepository sources) => sources.FindAsync(query, Ct));

    protected Task<Source> AddSearchableAsync(string title, string? url = null, string? fileName = null)
    {
        var source = NewSource(SourceStatus.Ready, Tick(), title);
        source.Url = url;
        source.FileName = fileName;
        return InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
    }

    protected Task<Source> AddOnSiteAsync(string title, string? site)
    {
        var source = NewSource(SourceStatus.Ready, Tick(), title);
        source.Site = site;
        return InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
    }

    protected DateTimeOffset Tick()
    {
        Fixture.Time.Advance(TimeSpan.FromMinutes(1));
        return Fixture.Time.GetUtcNow();
    }

    protected static Source NewSource(SourceStatus status = SourceStatus.Pending, DateTimeOffset? createdAt = null, string title = "Test source") => new()
    {
        Type = SourceType.Url,
        Status = status,
        Title = title,
        Url = "https://example.com/article",
        CreatedAt = createdAt ?? BaseTime,
        UpdatedAt = createdAt ?? BaseTime,
    };

    protected static Conversation NewConversation(DateTimeOffset? updatedAt = null, string title = "Test conversation", Guid[]? sourceIds = null) => new()
    {
        Title = title,
        SystemPrompt = "You are the Knowledge Agent.\nCite sources as [S1].",
        PromptName = "orchestrator-system",
        PromptVersion = 3,
        PromptOrigin = "langfuse",
        Profile = "orchestrator",
        SourceIds = sourceIds?.ToList() ?? [],
        CreatedAt = BaseTime,
        UpdatedAt = updatedAt ?? BaseTime,
    };

    protected async Task<Guid> AddWidgetConversationAsync(Guid widgetId, ConversationStatus status, DateTimeOffset updatedAt, MessageAuthor lastAuthor)
    {
        var conversation = NewConversation(updatedAt);
        conversation.Channel = ConversationChannel.Widget;
        conversation.WidgetId = widgetId;
        conversation.Status = status;
        conversation.Messages.Add(NewMessage(conversation.Id, 1, MessageAuthor.Visitor, updatedAt));
        conversation.Messages.Add(NewMessage(conversation.Id, 2, lastAuthor, updatedAt));
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        return conversation.Id;
    }

    protected static ConversationMessage NewMessage(Guid conversationId, int sequence, MessageAuthor author, DateTimeOffset createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        ConversationId = conversationId,
        Sequence = sequence,
        Author = author,
        Text = $"Message {sequence}",
        CreatedAt = createdAt,
    };

    protected static Handover NewHandover(Guid conversationId, HandoverStatus status, DateTimeOffset requestedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        ConversationId = conversationId,
        Channel = "portal",
        Status = status,
        Source = HandoverSource.Visitor,
        Token = Guid.NewGuid().ToString("N")[..Handover.TokenLength],
        RequestedAt = requestedAt,
        UpdatedAt = requestedAt,
    };

    protected static ConversationTurn NewTurn(Guid conversationId, int sequence = 0, string userMessage = "Question?") => new()
    {
        ConversationId = conversationId,
        Sequence = sequence,
        UserMessage = userMessage,
        AssistantMessage = "Answer [S1].",
        CreatedAt = BaseTime,
    };
}
