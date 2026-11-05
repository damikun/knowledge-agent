using System.Text;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Sources.Chunking;
using KnowledgeAgent.Tests.Features.Sources.Support;

namespace KnowledgeAgent.Tests.Features.Sources.Chunking;

public sealed class TextChunkerTests
{
    private readonly TextChunker _chunker = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t ")]
    public void Empty_or_whitespace_text_produces_no_chunks(string text)
    {
        _chunker.Chunk(text, new ChunkingSettings(100, 10)).ShouldBeEmpty();
        _chunker.Chunk(text, new ChunkingSettings(100, 10, TrackPages: true)).ShouldBeEmpty();
    }

    [Fact]
    public void Pages_without_text_produce_no_chunks()
    {
        _chunker.Chunk("\f \f\n", new ChunkingSettings(100, 10, TrackPages: true)).ShouldBeEmpty();
    }

    [Fact]
    public void Tiny_text_is_a_single_trimmed_chunk()
    {
        var chunks = _chunker.Chunk("  Hello world.  ", new ChunkingSettings(100, 10));

        var chunk = chunks.ShouldHaveSingleItem();
        chunk.Content.ShouldBe("Hello world.");
        chunk.Index.ShouldBe(0);
        chunk.Page.ShouldBeNull();
        chunk.TokenEstimate.ShouldBe(3);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(50, 0)]
    [InlineData(50, 10)]
    [InlineData(100, 25)]
    [InlineData(500, 60)]
    public void Chunks_respect_the_size_limit_and_are_never_empty(int size, int overlap)
    {
        var chunks = _chunker.Chunk(TestDocuments.Prose(40), new ChunkingSettings(size, overlap));

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.Content.Length > 0 && c.Content.Length <= size * TextChunker.CharsPerToken);
        chunks.ShouldAllBe(c => c.TokenEstimate <= size && c.TokenEstimate == (c.Content.Length + 3) / 4);
        chunks.ShouldAllBe(c => c.Content == c.Content.Trim());
        chunks.Select(c => c.Index).ShouldBe(Enumerable.Range(0, chunks.Count));
    }

    [Fact]
    public void Without_overlap_every_word_is_kept_exactly_once_in_order()
    {
        var text = TestDocuments.Prose(30);

        var chunks = _chunker.Chunk(text, new ChunkingSettings(64, 0));

        Collapse(string.Join(" ", chunks.Select(c => c.Content))).ShouldBe(Collapse(text));
    }

    [Fact]
    public void Consecutive_chunks_overlap_by_at_most_the_configured_amount()
    {
        const int overlapTokens = 20;
        var chunks = _chunker.Chunk(TestDocuments.Prose(30), new ChunkingSettings(100, overlapTokens));

        chunks.Count.ShouldBeGreaterThan(3);
        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1].Content;
            var overlap = SharedOverlap(previous, chunks[i].Content);
            overlap.Length.ShouldBeGreaterThan(0, $"chunk {i} should start with the tail of chunk {i - 1}");
            overlap.Length.ShouldBeLessThanOrEqualTo(overlapTokens * TextChunker.CharsPerToken);

            var start = previous.Length - overlap.Length;
            char.IsWhiteSpace(previous[start - 1]).ShouldBeTrue();
        }
    }

    [Fact]
    public void Overlap_prefers_sentence_starts()
    {
        var chunks = _chunker.Chunk(TestDocuments.Prose(20), new ChunkingSettings(100, 25));

        chunks.Skip(1).ShouldAllBe(c => c.Content.StartsWith("Paragraph "));
    }

    [Fact]
    public void Paragraph_boundaries_are_preferred_over_sentence_boundaries()
    {
        var first = string.Join(' ', Enumerable.Repeat("The first paragraph talks about apples.", 7));
        var second = string.Join(' ', Enumerable.Repeat("The second paragraph talks about pears.", 7));

        var chunks = _chunker.Chunk(first + "\n\n" + second, new ChunkingSettings(100, 0));

        chunks.Select(c => c.Content).ShouldBe([first, second]);
    }

    [Fact]
    public void Oversized_paragraphs_are_split_at_sentence_ends()
    {
        var chunks = _chunker.Chunk(TestDocuments.Prose(1, sentencesPerParagraph: 40), new ChunkingSettings(60, 0));

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.Content.EndsWith('.') && c.Content.StartsWith("Paragraph "));
    }

    [Fact]
    public void Sentences_longer_than_a_chunk_are_split_between_words()
    {
        var sentence = string.Join(' ', Enumerable.Range(0, 300).Select(i => $"word{i}"));

        var chunks = _chunker.Chunk(sentence, new ChunkingSettings(20, 0));

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.Content.Length <= 80);
        chunks.SelectMany(c => c.Content.Split(' ')).ShouldBe(sentence.Split(' '));
    }

    [Fact]
    public void Page_numbers_follow_form_feed_sections()
    {
        var chunks = _chunker.Chunk("Page one text.\fPage two text.\f\fPage four text.", new ChunkingSettings(100, 10, TrackPages: true));

        chunks.Select(c => (c.Page, c.Content)).ShouldBe(
            [(1, "Page one text."), (2, "Page two text."), (4, "Page four text.")]);
        chunks.Select(c => c.Index).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Chunks_never_span_pages()
    {
        var text = TestDocuments.Prose(10, topic: "alpha") + "\f" + TestDocuments.Prose(10, topic: "beta");

        var chunks = _chunker.Chunk(text, new ChunkingSettings(80, 15, TrackPages: true));

        chunks.Where(c => c.Page == 1).ShouldAllBe(c => c.Content.Contains("alpha") && !c.Content.Contains("beta"));
        chunks.Where(c => c.Page == 2).ShouldAllBe(c => c.Content.Contains("beta") && !c.Content.Contains("alpha"));
        chunks.Select(c => c.Page!.Value).ShouldBeInOrder();
        chunks.First().Page.ShouldBe(1);
        chunks.Last().Page.ShouldBe(2);
        chunks.ShouldAllBe(c => !c.Content.Contains('\f'));
    }

    [Fact]
    public void Pages_are_not_tracked_unless_requested()
    {
        _chunker.Chunk("one\ftwo", new ChunkingSettings(100, 0)).ShouldAllBe(c => c.Page == null);
    }

    [Fact]
    public void Unicode_text_is_never_split_inside_surrogate_pairs()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var family = char.ConvertFromUtf32(0x1F468) + (char)0x200D + char.ConvertFromUtf32(0x1F469) + (char)0x200D + char.ConvertFromUtf32(0x1F467);
        var cjk = string.Concat(Enumerable.Repeat("知识库可以回答问题。", 120));
        var text = string.Concat(Enumerable.Repeat(emoji, 400)) + "\n\n" + string.Concat(Enumerable.Repeat(family, 100)) + "\n\n" + cjk;

        foreach (var overlap in new[] { 0, 5 })
        {
            var chunks = _chunker.Chunk(text, new ChunkingSettings(20, overlap));

            chunks.Count.ShouldBeGreaterThan(10);
            chunks.ShouldAllBe(c => c.Content.Length <= 80 && HasNoLoneSurrogates(c.Content));
            if (overlap == 0)
            {
                string.Concat(chunks.Select(c => c.Content)).ShouldBe(Regex.Replace(text, @"\s", string.Empty));
            }
        }
    }

    [Fact]
    public void Cjk_sentences_are_split_at_ideographic_full_stops()
    {
        var sentence = "知识库可以回答问题。";
        var chunks = _chunker.Chunk(string.Concat(Enumerable.Repeat(sentence, 50)), new ChunkingSettings(10, 0));

        chunks.ShouldAllBe(c => c.Content.EndsWith('。') && c.Content.Length <= 40);
    }

    [Fact]
    public void Huge_input_without_whitespace_is_split_at_character_boundaries()
    {
        var text = new string('x', 1_000_000);

        var chunks = _chunker.Chunk(text, new ChunkingSettings(500, 60));

        chunks.Count.ShouldBeGreaterThanOrEqualTo(500);
        chunks.ShouldAllBe(c => c.Content.Length <= 2000);
        chunks.Sum(c => c.Content.Length).ShouldBeGreaterThanOrEqualTo(text.Length);
    }

    [Fact]
    public void Huge_prose_input_is_chunked_within_bounds()
    {
        var text = TestDocuments.Prose(2000);

        var chunks = _chunker.Chunk(text, new ChunkingSettings(500, 60));

        chunks.Count.ShouldBeGreaterThan(100);
        chunks.ShouldAllBe(c => c.TokenEstimate <= 500 && c.Content.Length > 0);
    }

    [Fact]
    public void Chunking_is_deterministic()
    {
        var text = TestDocuments.Prose(25) + "\f" + TestDocuments.Prose(5);
        var settings = new ChunkingSettings(70, 12, TrackPages: true);

        var first = _chunker.Chunk(text, settings);
        var second = new TextChunker().Chunk(text, settings);

        second.ShouldBe(first);
    }

    [Fact]
    public void Overlap_is_clamped_to_half_the_chunk_size()
    {
        var chunks = _chunker.Chunk(TestDocuments.Prose(10), new ChunkingSettings(40, 1000));

        chunks.Count.ShouldBeGreaterThan(1);
        chunks.ShouldAllBe(c => c.Content.Length <= 160);
        for (var i = 1; i < chunks.Count; i++)
        {
            SharedOverlap(chunks[i - 1].Content, chunks[i].Content).Length.ShouldBeLessThanOrEqualTo(80);
        }
    }

    [Fact]
    public void Chunk_size_is_raised_to_the_minimum()
    {
        var chunks = _chunker.Chunk(TestDocuments.Prose(2), new ChunkingSettings(1, 0));

        chunks.ShouldAllBe(c => c.Content.Length <= TextChunker.MinChunkSizeTokens * TextChunker.CharsPerToken);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(2000, 500)]
    [InlineData(2001, 501)]
    public void Token_estimate_is_characters_divided_by_four_rounded_up(int characters, int tokens)
    {
        TextChunker.EstimateTokens(characters).ShouldBe(tokens);
        TextChunker.EstimateTokens(new string('a', characters)).ShouldBe(tokens);
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string SharedOverlap(string previous, string next)
    {
        for (var length = Math.Min(previous.Length, next.Length); length > 0; length--)
        {
            if (previous.EndsWith(next[..length], StringComparison.Ordinal))
            {
                return next[..length];
            }
        }

        return string.Empty;
    }

    private static bool HasNoLoneSurrogates(string text)
    {
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
