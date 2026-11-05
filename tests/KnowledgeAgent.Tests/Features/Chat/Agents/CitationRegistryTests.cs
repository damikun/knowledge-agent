using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Tests.Features.Chat.Agents;

public sealed class CitationRegistryTests
{
    [Fact]
    public void Numbers_new_chunks_after_the_highest_previous_reference_and_reuses_known_chunks()
    {
        var known = Guid.NewGuid();
        var previous = new[]
        {
            Citation("S1", Guid.NewGuid()),
            Citation("S4", known),
            Citation("bogus", Guid.NewGuid()),
        };
        var registry = new CitationRegistry(previous);

        var first = registry.Register([Hit(Guid.NewGuid()), Hit(known), Hit(Guid.NewGuid())]);
        first.Select(c => c.Ref).ShouldBe(["S5", "S4", "S6"]);

        var repeatedChunk = first[0].ChunkId;
        var second = registry.Register([Hit(repeatedChunk)]);
        second.Single().Ref.ShouldBe("S5");

        registry.TurnCitations.Select(c => c.Ref).ShouldBe(["S5", "S4", "S6"]);
    }

    [Fact]
    public void Concurrent_registrations_get_unique_contiguous_references()
    {
        var registry = new CitationRegistry();
        var batches = Enumerable.Range(0, 20).Select(_ => Enumerable.Range(0, 5).Select(_ => Hit(Guid.NewGuid())).ToList()).ToList();

        var results = batches.AsParallel().Select(batch => registry.Register(batch)).ToList();

        var numbers = results.SelectMany(batch => batch.Select(c => CitationRegistry.ParseNumber(c.Ref))).ToList();
        numbers.Order().ShouldBe(Enumerable.Range(1, 100));
        foreach (var batch in results)
        {
            var batchNumbers = batch.Select(c => CitationRegistry.ParseNumber(c.Ref)).ToList();
            batchNumbers.ShouldBe(Enumerable.Range(batchNumbers[0], 5), "one search gets contiguous numbers");
        }
    }

    [Fact]
    public void Builds_snippets_from_collapsed_content()
    {
        var registry = new CitationRegistry();
        var citation = registry.Register([Hit(Guid.NewGuid(), "Line one\n\n   line two " + new string('x', 400))]).Single();

        citation.Snippet.Length.ShouldBe(CitationRegistry.SnippetLength);
        citation.Snippet.ShouldStartWith("Line one line two xxx");
        citation.Snippet.ShouldEndWith("…");
    }

    [Theory]
    [InlineData("S1", 1)]
    [InlineData("s12", 12)]
    [InlineData("S", 0)]
    [InlineData("X3", 0)]
    [InlineData("S-1", 0)]
    [InlineData(null, 0)]
    public void Parses_reference_numbers(string? reference, int expected) => CitationRegistry.ParseNumber(reference).ShouldBe(expected);

    private static KnowledgeSearchHit Hit(Guid chunkId, string content = "content") =>
        new(chunkId, Guid.NewGuid(), 0, "Title", null, null, content, 0.5);

    private static CitationDto Citation(string reference, Guid chunkId) =>
        new(reference, Guid.NewGuid(), chunkId, "Title", null, null, "snippet", 0.5);
}
