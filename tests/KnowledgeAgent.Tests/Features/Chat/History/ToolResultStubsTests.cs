using System.Globalization;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Tools;

namespace KnowledgeAgent.Tests.Features.Chat.History;

public sealed class ToolResultStubsTests
{
    private static readonly Guid HandbookId = Guid.Parse("0f8c2a8e-1b7c-4f8e-9d3a-2c1b0a9e8d7f");
    private static readonly Guid PolicyId = Guid.Parse("7a1e4c2b-3d5f-4a6b-8c9d-0e1f2a3b4c5d");

    [Fact]
    public void Search_results_become_a_header_with_each_reference_title_page_and_source_id()
    {
        var text = SearchResult(("S1", "Employee handbook", 3, HandbookId), ("S2", "HR policy 2026", null, PolicyId));

        var stub = Stubs().For(ToolNames.SearchKnowledgeBase, text);

        stub.ShouldBe(
            $"(Earlier result, shortened from {N(text.Length)} characters. Passages are not repeated; use read_source with a source_id, or search again with source_ids, if you need the text.)\n"
            + "Found 2 passage(s) for \"vacation days\".\n"
            + $"[S1] Employee handbook (page 3) | source_id={HandbookId}\n"
            + $"[S2] HR policy 2026 | source_id={PolicyId}");
    }

    [Fact]
    public void Grep_passages_are_stubbed_like_search_results()
    {
        var text = "Found \"refund\" (whole words, ignoring case) in 2 passages from 1 source; showing passages 1-2:"
            + $"\n\n[S1] Employee handbook (page 3) | source_id={HandbookId}\n" + new string('a', 800)
            + $"\n\n[S3] Employee handbook (page 4) | source_id={HandbookId}\n" + new string('b', 800);

        var stub = Stubs().For(ToolNames.GrepSources, text);

        stub.ShouldStartWith("(Earlier result, shortened from 1,");
        stub.Split('\n')[1].ShouldStartWith("Found \"refund\"");
        stub.ShouldContain($"[S1] Employee handbook (page 3) | source_id={HandbookId}");
        stub.ShouldContain($"[S3] Employee handbook (page 4) | source_id={HandbookId}", Case.Sensitive, "taken from the passage header when the turn has no citation for it");
        stub.ShouldNotContain("aaaa");
    }

    [Fact]
    public void A_reference_missing_from_the_turn_citations_keeps_its_original_line_without_the_score()
    {
        var text = "Found 1 passage(s) for \"x\".\n\n[S9] Old notes (page 2) | score 0.81 | source_id=" + PolicyId + "\n" + new string('p', 900);

        var stub = Stubs().For(ToolNames.SearchKnowledgeBase, text);

        stub.Split('\n')[^1].ShouldBe($"[S9] Old notes (page 2) | source_id={PolicyId}");
    }

    [Fact]
    public void Read_source_keeps_the_two_header_lines()
    {
        var text = $"Source: Employee handbook (id: {HandbookId}, type: pdf)\nCharacters 0-11600 of 40000. Next offset: 11600.\n\n" + new string('x', 11_600);

        var stub = Stubs().For(ToolNames.ReadSource, text);

        stub.ShouldBe(
            $"(Earlier result, shortened from {N(text.Length)} characters. The text is not repeated; call read_source again with this source_id and offset if you need it.)\n"
            + $"Source: Employee handbook (id: {HandbookId}, type: pdf)\n"
            + "Characters 0-11600 of 40000. Next offset: 11600.");
    }

    [Fact]
    public void Researcher_reports_keep_an_excerpt_and_their_references()
    {
        var report = "Employees get 25 days [S1]; the 2026 policy adds one more [S2]. " + new string('r', 3_000);

        var stub = Stubs(new HistoryOptions { ReportExcerptChars = 100 }).For(ToolNames.DelegateResearch, report);

        var lines = stub.Split('\n');
        lines[0].ShouldBe($"(Earlier result, shortened from {N(report.Length)} characters.)");
        lines[1].Length.ShouldBe(101);
        lines[1].ShouldStartWith("Employees get 25 days [S1]");
        lines[1].ShouldEndWith("…");
        lines[2..].ShouldBe(["References:", $"[S1] Employee handbook (page 3) | source_id={HandbookId}", $"[S2] HR policy 2026 | source_id={PolicyId}"]);
    }

    [Fact]
    public void Mcp_results_name_the_tool_turn_and_time_and_keep_an_excerpt()
    {
        var text = "{\"order\":\"42\",\"status\":\"shipped\"," + new string(' ', 3_000) + "}";

        var stub = Stubs(new HistoryOptions { McpExcerptChars = 40 }).For("mcp_shop__get_order", text);

        var lines = stub.Split('\n');
        lines[0].ShouldBe(
            $"(Earlier result of mcp_shop__get_order from turn 3 at 2026-11-09 14:02 UTC, shortened from {N(text.Length)} characters. This was the state at that time; call the tool again for the current state.)");
        lines[1].ShouldBe(text[..40] + "…");
    }

    [Theory]
    [InlineData(ToolNames.FindSources)]
    [InlineData("some_future_tool")]
    [InlineData(null)]
    public void Other_results_keep_a_short_excerpt(string? toolName)
    {
        var text = "Found 40 sources; showing 1-40:\n" + string.Join('\n', Enumerable.Range(0, 40).Select(i => $"- Doc {i} (id: {Guid.Empty}, type: pdf)"));

        var stub = Stubs(new HistoryOptions { OtherExcerptChars = 120 }).For(toolName, text);

        stub.ShouldStartWith($"(Earlier result, shortened from {N(text.Length)} characters.)\nFound 40 sources; showing 1-40:\n- Doc 0 (id: ");
        stub.Length.ShouldBeLessThan(260);
    }

    [Fact]
    public void Short_results_errors_and_hand_offs_stay_whole()
    {
        var stubs = Stubs();
        var shortError = "Error: Source 1 was not found.";
        var longTransfer = "Handed over: " + new string('t', 900);

        stubs.For(ToolNames.ReadSource, shortError).ShouldBeSameAs(shortError);
        stubs.For(ToolNames.SearchKnowledgeBase, "No passages found for \"x\".").ShouldBe("No passages found for \"x\".");
        stubs.For(ToolNames.TransferToAgent, longTransfer).ShouldBeSameAs(longTransfer);
        stubs.For(ToolNames.RequestHumanHandover, longTransfer).ShouldBeSameAs(longTransfer);
    }

    [Fact]
    public void Long_errors_keep_an_excerpt()
    {
        var error = "Error: Shop refused the call: " + new string('e', 2_000);

        var stub = Stubs(new HistoryOptions { OtherExcerptChars = 50 }).For("mcp_shop__get_order", error);

        stub.ShouldBe($"(Earlier result, shortened from {N(error.Length)} characters.)\n" + error[..50] + "…");
    }

    [Fact]
    public void Wrapped_content_stays_inside_a_block_with_the_same_key()
    {
        var inner = SearchResult(("S1", "Employee handbook", 3, HandbookId));
        var wrapped = UntrustedText.Wrap(inner, "a1b2c3d4e5f6");

        var stub = Stubs().For(ToolNames.SearchKnowledgeBase, wrapped);

        ToolResultStubs.TryUnwrap(stub[(stub.IndexOf('\n') + 1)..], out var body, out var key).ShouldBeTrue();
        key.ShouldBe("a1b2c3d4e5f6");
        stub.ShouldStartWith($"(Earlier result, shortened from {N(inner.Length)} characters.");
        body.ShouldBe($"Found 1 passage(s) for \"vacation days\".\n[S1] Employee handbook (page 3) | source_id={HandbookId}");
        UntrustedText.IsWrapped(stub).ShouldBeFalse("the header is ours and sits before the block");
    }

    [Fact]
    public void Excerpts_never_split_a_surrogate_pair()
    {
        var text = new string('a', 49) + "😀" + new string('b', 2_000);

        var stub = Stubs(new HistoryOptions { OtherExcerptChars = 50 }).For("some_tool", text);

        stub.Split('\n')[1].ShouldBe(new string('a', 49) + "…");
    }

    [Fact]
    public void The_same_turn_always_gives_the_same_stub()
    {
        var text = SearchResult(("S1", "Employee handbook", 3, HandbookId));

        Stubs().For(ToolNames.SearchKnowledgeBase, text).ShouldBe(Stubs().For(ToolNames.SearchKnowledgeBase, text));
    }

    internal static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    internal static ToolResultStubs Stubs(HistoryOptions? options = null) => new(Turn(), options ?? new HistoryOptions());

    internal static ConversationTurn Turn() => new()
    {
        Sequence = 3,
        CreatedAt = new DateTimeOffset(2026, 11, 9, 14, 2, 31, TimeSpan.Zero),
        CitationsJson = ChatJson.SerializeDtos<CitationDto>(
        [
            new CitationDto("S1", HandbookId, Guid.NewGuid(), "Employee  handbook", null, 3, "snippet", 0.9),
            new CitationDto("S2", PolicyId, Guid.NewGuid(), "HR policy 2026", "https://example.com", null, "snippet", 0.8),
        ]),
    };

    internal static string SearchResult(params (string Ref, string Title, int? Page, Guid SourceId)[] hits) =>
        $"Found {hits.Length} passage(s) for \"vacation days\"."
        + string.Concat(hits.Select(hit =>
            $"\n\n[{hit.Ref}] {hit.Title}{(hit.Page is { } page ? $" (page {page})" : string.Empty)} | score 0.90 | source_id={hit.SourceId}\n" + new string('p', 3_900)));
}
