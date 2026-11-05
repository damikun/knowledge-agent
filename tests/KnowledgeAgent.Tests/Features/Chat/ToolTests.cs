using System.Globalization;
using System.Threading.Channels;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.DependencyInjection;
using static KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.TestChunks;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class ToolTests : IDisposable
{
    private static readonly Guid SourceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly AgentTestHost _host;
    private readonly IServiceScope _scope;

    public ToolTests()
    {
        _host = new AgentTestHost();
        _scope = _host.Services.CreateScope();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _scope.Dispose();
        _host.Dispose();
    }

    [Fact]
    public async Task Search_result_text_has_the_exact_contract_format_in_any_culture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            _host.Search.AddHit("Pricing Guide", "  The Pro plan costs 10 EUR per month.  ", score: 0.8666, page: 3, sourceId: SourceA);
            _host.Search.AddHit("FAQ", "Support is available 24/7.\nAlso on weekends.", score: 0.5, sourceId: SourceB);

            var result = await Tool<SearchKnowledgeBaseTool>().InvokeAsync(Invocation(new() { ["query"] = "pricing" }), Ct);

            result.IsError.ShouldBeFalse();
            result.Text.ShouldBe(
                """
                Found 2 passage(s) for "pricing".

                [S1] Pricing Guide (page 3) | score 0.87 | source_id=11111111-1111-1111-1111-111111111111
                The Pro plan costs 10 EUR per month.

                [S2] FAQ | score 0.50 | source_id=22222222-2222-2222-2222-222222222222
                Support is available 24/7.
                Also on weekends.
                """.ReplaceLineEndings("\n"));
            result.Summary.ShouldBe("2 passages for \"pricing\"");
            result.Citations.ShouldNotBeNull().Select(c => (c.Ref, c.SourceId, c.Page, c.Snippet)).ShouldBe(
            [
                ("S1", SourceA, (int?)3, "The Pro plan costs 10 EUR per month."),
                ("S2", SourceB, (int?)null, "Support is available 24/7. Also on weekends."),
            ]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task Search_without_hits_says_so()
    {
        var result = await Tool<SearchKnowledgeBaseTool>().InvokeAsync(Invocation(new() { ["query"] = "  nothing here " }), Ct);

        result.IsError.ShouldBeFalse();
        result.Text.ShouldBe("No passages found for \"nothing here\".");
        result.Summary.ShouldBe("No passages for \"nothing here\"");
        result.Citations.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, 6)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(500, 20)]
    public async Task Search_clamps_top_k(int? topK, int expected)
    {
        var arguments = new Dictionary<string, object?> { ["query"] = "q" };
        if (topK is not null)
        {
            arguments["top_k"] = topK;
        }

        await Tool<SearchKnowledgeBaseTool>().InvokeAsync(Invocation(arguments), Ct);

        _host.Search.Requests.Single().TopK.ShouldBe(expected);
    }

    [Fact]
    public async Task Search_ignores_invalid_source_ids_and_never_widens_the_conversation_scope()
    {
        var tool = Tool<SearchKnowledgeBaseTool>();

        await tool.InvokeAsync(Invocation(new() { ["query"] = "q", ["source_ids"] = new[] { "nope", SourceA.ToString() } }), Ct);
        await tool.InvokeAsync(Invocation(new() { ["query"] = "q", ["source_ids"] = new[] { SourceA.ToString(), SourceB.ToString() } }, scope: [SourceB]), Ct);
        await tool.InvokeAsync(Invocation(new() { ["query"] = "q", ["source_ids"] = new[] { SourceA.ToString() } }, scope: [SourceB]), Ct);
        await tool.InvokeAsync(Invocation(new() { ["query"] = "q", ["source_ids"] = new[] { "invalid" } }), Ct);

        var requests = _host.Search.Requests;
        requests[0].SourceIds.ShouldBe([SourceA]);
        requests[1].SourceIds.ShouldBe([SourceB]);
        requests[2].SourceIds.ShouldBe([SourceB]);
        requests[3].SourceIds.ShouldBeNull();
    }

    [Fact]
    public async Task Search_shortens_passages_so_all_hits_fit_the_output_limit()
    {
        for (var i = 0; i < 20; i++)
        {
            _host.Search.AddHit($"Doc {i}", new string('a', 3000));
        }

        var result = await Tool<SearchKnowledgeBaseTool>().InvokeAsync(Invocation(new() { ["query"] = "q", ["top_k"] = 20 }), Ct);

        result.Text.Length.ShouldBeLessThanOrEqualTo(12_000);
        result.Text.ShouldContain("[S20] Doc 19");
        result.Text.ShouldContain("…");
        result.Citations!.Count.ShouldBe(20);
    }

    [Fact]
    public async Task Find_sources_returns_ready_sources_newest_first_as_a_page_with_a_paging_hint()
    {
        AddFindFixtures();
        var tool = Tool<FindSourcesTool>();
        var day = AgentTestHost.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var firstPage = await tool.InvokeAsync(Invocation(new() { ["limit"] = 2 }), Ct);
        var secondPage = await tool.InvokeAsync(Invocation(new() { ["limit"] = 2, ["offset"] = 2 }), Ct);

        firstPage.Text.ShouldBe(
            $"""
            3 sources (status ready); showing 1-2:
            - Pricing Guide (id: {SourceA}, type: url, status: ready, chunks: 12, added: {day}, url: https://example.com/pricing)
            - Employee Handbook 2026 (id: {SourceB}, type: pdf, status: ready, chunks: 7, added: {day})
            More results: call find_sources again with offset=2.
            """.ReplaceLineEndings("\n"));
        firstPage.Summary.ShouldBe("2 of 3 sources");
        secondPage.Text.ShouldStartWith("3 sources (status ready); showing 3-3:\n- Holiday handbook (id: ");
        secondPage.Text.ShouldNotContain("More results");
        secondPage.Summary.ShouldBe("1 of 3 sources");
    }

    [Fact]
    public async Task Find_sources_matches_every_word_in_title_url_or_file_name_case_insensitively()
    {
        AddFindFixtures();
        var tool = Tool<FindSourcesTool>();

        var handbooks = await tool.InvokeAsync(Invocation(new() { ["query"] = "HANDBOOK" }), Ct);
        var exact = await tool.InvokeAsync(Invocation(new() { ["query"] = "handbook 2026" }), Ct);
        var byUrl = await tool.InvokeAsync(Invocation(new() { ["query"] = "example.com/pricing" }), Ct);
        var none = await tool.InvokeAsync(Invocation(new() { ["query"] = "zebra" }), Ct);

        handbooks.Text.ShouldStartWith("2 sources match \"HANDBOOK\" (status ready); showing 1-2:");
        handbooks.Summary.ShouldBe("2 sources for \"HANDBOOK\"");
        exact.Text.ShouldStartWith("1 source match \"handbook 2026\" (status ready); showing 1-1:\n- Employee Handbook 2026");
        byUrl.Text.ShouldContain("- Pricing Guide (id: ");
        none.Text.ShouldBe("No sources match \"zebra\" (status ready).");
        none.Summary.ShouldBe("0 sources for \"zebra\"");
    }

    [Fact]
    public async Task Find_sources_filters_by_type_and_status_and_stays_inside_the_conversation_scope()
    {
        AddFindFixtures();
        var tool = Tool<FindSourcesTool>();

        var pdfs = await tool.InvokeAsync(Invocation(new() { ["type"] = "PDF" }), Ct);
        var processing = await tool.InvokeAsync(Invocation(new() { ["status"] = "processing" }), Ct);
        var any = await tool.InvokeAsync(Invocation(new() { ["status"] = "any" }), Ct);
        var failed = await tool.InvokeAsync(Invocation(new() { ["status"] = "failed" }), Ct);
        var scoped = await tool.InvokeAsync(Invocation([], scope: [SourceB]), Ct);
        var pastTheEnd = await tool.InvokeAsync(Invocation(new() { ["offset"] = 10 }), Ct);

        pdfs.Text.ShouldStartWith("2 sources (type pdf, status ready); showing 1-2:");
        processing.Text.ShouldStartWith("1 source (status processing); showing 1-1:\n- Draft notes (id: ");
        any.Text.ShouldStartWith("4 sources (any status); showing 1-4:");
        failed.Text.ShouldBe("No sources (status failed).");
        scoped.Text.ShouldStartWith("1 source (status ready); showing 1-1:\n- Employee Handbook 2026");
        pastTheEnd.Text.ShouldBe("3 sources (status ready). Offset 10 is past the last result; use a smaller offset.");
    }

    [Fact]
    public async Task Find_sources_rejects_invalid_arguments()
    {
        var tool = Tool<FindSourcesTool>();

        var badType = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["type"] = "video" }), Ct));
        var badStatus = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["status"] = "deleted" }), Ct));
        var negative = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["offset"] = -1 }), Ct));

        badType.Message.ShouldBe("Invalid arguments for find_sources: 'type' must be one of url, pdf, text, markdown.");
        badStatus.Message.ShouldBe("Invalid arguments for find_sources: 'status' must be one of pending, processing, ready, failed or any.");
        negative.Message.ShouldBe("Invalid arguments for find_sources: 'offset' must be zero or positive.");
    }

    private void AddFindFixtures()
    {
        _host.Sources.Add("Pricing Guide", type: SourceType.Url, url: "https://example.com/pricing", chunkCount: 12, createdAt: AgentTestHost.Now, id: SourceA);
        _host.Sources.Add("Employee Handbook 2026", type: SourceType.Pdf, chunkCount: 7, createdAt: AgentTestHost.Now.AddMinutes(-1), id: SourceB);
        _host.Sources.Add("Holiday handbook", type: SourceType.Pdf, chunkCount: 4, createdAt: AgentTestHost.Now.AddMinutes(-2));
        _host.Sources.Add("Draft notes", status: SourceStatus.Processing, type: SourceType.Text, chunkCount: 0, createdAt: AgentTestHost.Now.AddMinutes(-3));
    }

    [Fact]
    public async Task Read_source_returns_a_slice_with_paging_information()
    {
        var text = "Page one text.\fPage two text. " + new string('z', 50);
        _host.Sources.Add("Manual", type: SourceType.Pdf, text: text, id: SourceA);
        var tool = Tool<ReadSourceTool>();

        var first = await tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceA.ToString(), ["max_chars"] = 20 }), Ct);
        var rest = await tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceA.ToString(), ["offset"] = "20" }), Ct);

        first.Text.ShouldBe(
            $"Source: Manual (id: {SourceA}, type: pdf)\nCharacters 0-20 of {text.Length}. Next offset: 20.\n\nPage one text.\nPage ");
        first.Summary.ShouldBe($"Read characters 0-20 of {text.Length} from \"Manual\"");
        rest.Text.ShouldBe($"Source: Manual (id: {SourceA}, type: pdf)\nCharacters 20-{text.Length} of {text.Length}. End of text.\n\n{text[20..]}");
    }

    [Fact]
    public async Task Read_source_reports_problems_as_errors()
    {
        _host.Sources.Add("Manual", text: "Short text.", id: SourceA);
        _host.Sources.Add("Broken", status: SourceStatus.Failed, id: SourceB).Error = "Download failed";
        var processing = _host.Sources.Add("Pending one", status: SourceStatus.Processing);
        var empty = _host.Sources.Add("Empty");
        var tool = Tool<ReadSourceTool>();

        var unknown = await tool.InvokeAsync(Invocation(new() { ["source_id"] = Guid.Parse("33333333-3333-3333-3333-333333333333").ToString() }), Ct);
        var failed = await tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceB.ToString() }), Ct);
        var notReady = await tool.InvokeAsync(Invocation(new() { ["source_id"] = processing.Id.ToString() }), Ct);
        var noText = await tool.InvokeAsync(Invocation(new() { ["source_id"] = empty.Id.ToString() }), Ct);
        var pastEnd = await tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceA.ToString(), ["offset"] = 500 }), Ct);
        var outOfScope = await tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceA.ToString() }, scope: [SourceB]), Ct);

        unknown.ShouldBe(ToolResult.Failure("Source 33333333-3333-3333-3333-333333333333 was not found."));
        failed.Summary.ShouldBe("Source \"Broken\" is not ready (status: failed): Download failed");
        notReady.Summary.ShouldBe("Source \"Pending one\" is not ready (status: processing).");
        noText.Summary.ShouldBe("Source \"Empty\" has no extracted text.");
        pastEnd.Summary.ShouldBe("offset 500 is past the end of \"Manual\" (11 characters).");
        outOfScope.Summary.ShouldBe($"Source {SourceA} is not among the sources selected for this conversation.");
        new[] { unknown, failed, notReady, noText, pastEnd, outOfScope }.ShouldAllBe(r => r.IsError && r.Text.StartsWith("Error: "));
    }

    [Fact]
    public async Task Read_source_validates_arguments()
    {
        var tool = Tool<ReadSourceTool>();

        var missing = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation([]), Ct));
        var badId = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["source_id"] = "abc" }), Ct));
        var negative = await Should.ThrowAsync<ToolArgumentException>(() =>
            tool.InvokeAsync(Invocation(new() { ["source_id"] = SourceA.ToString(), ["offset"] = -1 }), Ct));

        missing.Message.ShouldBe("Invalid arguments for read_source: 'source_id' is required.");
        badId.Message.ShouldContain("'source_id' must be a source id (GUID)");
        negative.Message.ShouldBe("Invalid arguments for read_source: 'offset' must be zero or positive.");
    }

    [Fact]
    public async Task Grep_returns_every_passage_with_the_exact_text_in_source_order_with_citations()
    {
        _host.Sources.Add("Pricing Guide", type: SourceType.Url, url: "https://example.com/pricing", id: SourceA);
        _host.Sources.Add("FAQ", id: SourceB);
        await AddChunksAsync(SourceA, "The Pro plan costs 10 EUR per month.", "Discounts apply to yearly plans.", "Teams pay 10 eur\nper seat.");
        await AddChunksAsync(SourceB, "Support is available 24/7.", "Refunds of 10 EUR take a week.", "Enterprise costs 100 EUR or 110 EUR.");

        var result = await Tool<GrepSourcesTool>().InvokeAsync(Invocation(new() { ["text"] = "10 EUR" }), Ct);

        result.IsError.ShouldBeFalse();
        result.Text.ShouldBe(
            """
            Found "10 EUR" (whole words, ignoring case) in 3 passages from 2 sources; showing passages 1-3:

            [S1] Pricing Guide | source_id=11111111-1111-1111-1111-111111111111
            The Pro plan costs 10 EUR per month.

            [S2] Pricing Guide | source_id=11111111-1111-1111-1111-111111111111
            Teams pay 10 eur per seat.

            [S3] FAQ | source_id=22222222-2222-2222-2222-222222222222
            Refunds of 10 EUR take a week.
            """.ReplaceLineEndings("\n"));
        result.Summary.ShouldBe("3 passages in 2 sources for \"10 EUR\"");
        result.Citations.ShouldNotBeNull().Select(c => (c.Ref, c.SourceId, c.SourceUrl, c.Snippet, c.Score)).ShouldBe(
        [
            ("S1", SourceA, "https://example.com/pricing", "The Pro plan costs 10 EUR per month.", 1d),
            ("S2", SourceA, "https://example.com/pricing", "Teams pay 10 eur per seat.", 1d),
            ("S3", SourceB, (string?)null, "Refunds of 10 EUR take a week.", 1d),
        ]);
    }

    [Fact]
    public async Task Grep_lists_the_sources_that_mention_the_text_most_passages_first_page_by_page()
    {
        _host.Sources.Add("Pricing Guide", type: SourceType.Url, url: "https://example.com/pricing", id: SourceA);
        _host.Sources.Add("FAQ", id: SourceB);
        await AddChunksAsync(SourceA, "Jan Novák approved the budget.", "Nothing here.");
        await AddChunksAsync(SourceB, "Ask Jan Novák.", "JAN NOVÁK signed it.", "Only Novák.");
        var tool = Tool<GrepSourcesTool>();

        var first = await tool.InvokeAsync(Invocation(new() { ["text"] = "jan novák", ["output"] = "sources", ["limit"] = 1 }), Ct);
        var second = await tool.InvokeAsync(Invocation(new() { ["text"] = "jan novák", ["output"] = "SOURCES", ["limit"] = 1, ["offset"] = 1 }), Ct);

        first.Text.ShouldBe(
            $"""
            Found "jan novák" (whole words, ignoring case) in 3 passages from 2 sources; showing sources 1-1, most passages first:
            - FAQ (id: {SourceB}, passages: 2)
            More sources: call grep_sources again with output=sources and offset=1.
            """.ReplaceLineEndings("\n"));
        first.Summary.ShouldBe("1 of 2 sources for \"jan novák\"");
        first.Citations.ShouldBeNull();
        second.Text.ShouldBe(
            $"""
            Found "jan novák" (whole words, ignoring case) in 3 passages from 2 sources; showing sources 2-2, most passages first:
            - Pricing Guide (id: {SourceA}, passages: 1, url: https://example.com/pricing)
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Grep_pages_passages_and_flags_an_offset_past_the_end()
    {
        _host.Sources.Add("Manual", id: SourceA);
        await AddChunksAsync(SourceA, "Joint 1 torque.", "Joint 2 torque.", "Joint 3 torque.");
        var tool = Tool<GrepSourcesTool>();

        var first = await tool.InvokeAsync(Invocation(new() { ["text"] = "torque", ["limit"] = 2 }), Ct);
        var rest = await tool.InvokeAsync(Invocation(new() { ["text"] = "torque", ["limit"] = 2, ["offset"] = 2 }), Ct);
        var pastEnd = await tool.InvokeAsync(Invocation(new() { ["text"] = "torque", ["offset"] = 5 }), Ct);

        first.Text.ShouldStartWith("Found \"torque\" (whole words, ignoring case) in 3 passages from 1 source; showing passages 1-2:");
        first.Text.ShouldEndWith("Joint 2 torque.\n\nMore passages: call grep_sources again with offset=2.");
        // Each invocation here gets a fresh citation registry, so tags restart at S1.
        rest.Text.ShouldStartWith("Found \"torque\" (whole words, ignoring case) in 3 passages from 1 source; showing passages 3-3:\n\n[S1] Manual");
        rest.Text.ShouldNotContain("More passages");
        pastEnd.Text.ShouldBe("Found \"torque\" (whole words, ignoring case) in 3 passages from 1 source. Offset 5 is past the last result; use a smaller offset.");
        pastEnd.Citations.ShouldBeNull();
    }

    [Fact]
    public async Task Grep_without_matches_suggests_what_to_try_next()
    {
        _host.Sources.Add("FAQ", id: SourceB);
        await AddChunksAsync(SourceB, "The article covers art history.");
        var tool = Tool<GrepSourcesTool>();

        var wholeWords = await tool.InvokeAsync(Invocation(new() { ["text"] = "artic" }), Ct);
        var insideWords = await tool.InvokeAsync(Invocation(new() { ["text"] = "artic", ["whole_words"] = false }), Ct);
        var nowhere = await tool.InvokeAsync(Invocation(new() { ["text"] = "zebra", ["whole_words"] = "false" }), Ct);

        wholeWords.Text.ShouldBe(
            "No passages contain \"artic\" (whole words, ignoring case). Try another spelling or fewer words, or set whole_words to false; "
            + "search_knowledge_base finds passages with related wording.");
        wholeWords.Summary.ShouldBe("No matches for \"artic\"");
        wholeWords.Citations.ShouldBeNull();
        insideWords.Text.ShouldStartWith("Found \"artic\" (also inside words, ignoring case) in 1 passage from 1 source; showing passages 1-1:");
        nowhere.Text.ShouldBe(
            "No passages contain \"zebra\" (also inside words, ignoring case). Try another spelling or fewer words; "
            + "search_knowledge_base finds passages with related wording.");
    }

    [Fact]
    public async Task Grep_matches_case_when_asked_and_stays_inside_the_conversation_scope()
    {
        _host.Sources.Add("Pricing Guide", id: SourceA);
        _host.Sources.Add("FAQ", id: SourceB);
        await AddChunksAsync(SourceA, "The US office is closed.");
        await AddChunksAsync(SourceB, "Call us any time.", "US customers pay in dollars.");
        var tool = Tool<GrepSourcesTool>();

        var anyCase = await tool.InvokeAsync(Invocation(new() { ["text"] = "us" }), Ct);
        var exactCase = await tool.InvokeAsync(Invocation(new() { ["text"] = "US", ["match_case"] = true }), Ct);
        var scoped = await tool.InvokeAsync(
            Invocation(new() { ["text"] = "US", ["match_case"] = "true", ["source_ids"] = new[] { SourceA.ToString(), SourceB.ToString() } }, scope: [SourceB]),
            Ct);

        anyCase.Summary.ShouldBe("3 passages in 2 sources for \"us\"");
        exactCase.Summary.ShouldBe("2 passages in 2 sources for \"US\"");
        exactCase.Text.ShouldStartWith("Found \"US\" (whole words, matching case)");
        scoped.Summary.ShouldBe("1 passage in 1 source for \"US\"");
        scoped.Citations.ShouldNotBeNull().ShouldHaveSingleItem().SourceId.ShouldBe(SourceB);
    }

    [Fact]
    public async Task Grep_shows_the_text_around_the_match_of_a_long_passage()
    {
        _host.Sources.Add("Manual", id: SourceA);
        var filler = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", 120));
        await AddChunksAsync(SourceA, $"{filler} The torque limit is 42 Nm for joint 2. {filler}");

        var result = await Tool<GrepSourcesTool>().InvokeAsync(Invocation(new() { ["text"] = "42 Nm" }), Ct);

        var passage = result.Text.Split('\n')[3];
        passage.ShouldStartWith("…");
        passage.ShouldEndWith("…");
        passage.ShouldContain("The torque limit is 42 Nm for joint 2.");
        passage.Length.ShouldBeLessThanOrEqualTo(702);
        result.Citations.ShouldNotBeNull().ShouldHaveSingleItem().Snippet.ShouldContain("42 Nm");
    }

    [Fact]
    public async Task Grep_skips_passages_of_deleted_sources()
    {
        _host.Sources.Add("FAQ", id: SourceB);
        await AddChunksAsync(SourceA, "An orphaned mention of Acme.");
        await AddChunksAsync(SourceB, "Acme pays on time.");

        var result = await Tool<GrepSourcesTool>().InvokeAsync(Invocation(new() { ["text"] = "acme" }), Ct);

        result.Citations.ShouldNotBeNull().ShouldHaveSingleItem().SourceId.ShouldBe(SourceB);
        result.Text.ShouldNotContain("orphaned");
    }

    [Fact]
    public async Task Grep_validates_arguments()
    {
        var tool = Tool<GrepSourcesTool>();

        var missing = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation([]), Ct));
        var badFlags = await Should.ThrowAsync<ToolArgumentException>(() =>
            tool.InvokeAsync(Invocation(new() { ["text"] = "x", ["match_case"] = "maybe", ["whole_words"] = 3 }), Ct));
        var tooLong = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["text"] = new string('a', 201) }), Ct));
        var badOutput = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["text"] = "x", ["output"] = "chunks" }), Ct));
        var negative = await Should.ThrowAsync<ToolArgumentException>(() => tool.InvokeAsync(Invocation(new() { ["text"] = "x", ["offset"] = -1 }), Ct));

        missing.Message.ShouldBe("Invalid arguments for grep_sources: 'text' is required.");
        badFlags.Message.ShouldBe("Invalid arguments for grep_sources: 'match_case' must be true or false. 'whole_words' must be true or false.");
        tooLong.Message.ShouldBe("Invalid arguments for grep_sources: 'text' must be at most 200 characters.");
        badOutput.Message.ShouldBe("Invalid arguments for grep_sources: 'output' must be passages or sources.");
        negative.Message.ShouldBe("Invalid arguments for grep_sources: 'offset' must be zero or positive.");
    }

    [Fact]
    public void Tool_declarations_expose_names_descriptions_and_schemas()
    {
        var registry = _host.Services.GetRequiredService<ToolRegistry>();

        registry.Names.ShouldBe(
            [
                ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource, ToolNames.DelegateResearch,
                ToolNames.RecordFeedback, ToolNames.RequestHumanHandover,
            ],
            ignoreOrder: true);
        foreach (var name in registry.Names)
        {
            registry.TryGet(name, out var tool).ShouldBeTrue();
            tool.Declaration.Name.ShouldBe(name);
            tool.Declaration.Description.ShouldNotBeNullOrWhiteSpace();
            tool.Declaration.JsonSchema.GetProperty("type").GetString().ShouldBe("object");
            tool.Declaration.JsonSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        }

        registry.TryGet(ToolNames.DelegateResearch, out var research).ShouldBeTrue();
        research.Declaration.JsonSchema.GetProperty("required")[0].GetString().ShouldBe("task");
        research.Timeout.ShouldBe(TimeSpan.FromSeconds(120 * 7));
        registry.TryGet(ToolNames.FindSources, out var find).ShouldBeTrue();
        var findProperties = find.Declaration.JsonSchema.GetProperty("properties");
        findProperties.GetProperty("status").GetProperty("enum").GetArrayLength().ShouldBe(5);
        findProperties.GetProperty("type").GetProperty("enum").GetArrayLength().ShouldBe(4);
        findProperties.GetProperty("limit").GetProperty("maximum").GetInt32().ShouldBe(FindSourcesTool.MaxLimit);
        registry.TryGet(ToolNames.GrepSources, out var grep).ShouldBeTrue();
        grep.Declaration.JsonSchema.GetProperty("required")[0].GetString().ShouldBe("text");
        var grepProperties = grep.Declaration.JsonSchema.GetProperty("properties");
        grepProperties.GetProperty("text").GetProperty("maxLength").GetInt32().ShouldBe(200);
        grepProperties.GetProperty("output").GetProperty("enum").GetArrayLength().ShouldBe(2);
        grepProperties.GetProperty("limit").GetProperty("maximum").GetInt32().ShouldBe(GrepSourcesTool.MaxSourceLimit);
    }

    private IAgentTool Tool<T>() where T : IAgentTool =>
        _host.Services.GetServices<IAgentTool>().OfType<T>().Single();

    private Task AddChunksAsync(Guid sourceId, params string[] contents) =>
        _host.VectorStore.InsertAsync(contents.Select((content, index) => Chunk(sourceId, index, [1, 0, 0, 0], content: content)).ToArray(), Ct);

    private ToolInvocation Invocation(Dictionary<string, object?> arguments, IReadOnlyList<Guid>? scope = null, CitationRegistry? citations = null)
    {
        var channel = Channel.CreateUnbounded<ChatStreamEvent>();
        var turn = new AgentTurnContext(Guid.NewGuid(), Guid.NewGuid(), scope, citations ?? new CitationRegistry(), channel.Writer);
        return new ToolInvocation("call_1", new ToolArguments(arguments), turn, "orchestrator", _host.Services.GetRequiredService<AgentRunner>(), _scope.ServiceProvider);
    }
}
