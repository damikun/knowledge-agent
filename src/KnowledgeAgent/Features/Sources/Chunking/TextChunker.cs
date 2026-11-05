using System.Globalization;

namespace KnowledgeAgent.Features.Sources.Chunking;

internal sealed record TextChunk(int Index, string Content, int? Page, int TokenEstimate);

internal sealed record ChunkingSettings(int ChunkSizeTokens, int ChunkOverlapTokens, bool TrackPages = false);

internal sealed class TextChunker
{
    public const int CharsPerToken = 4;
    public const int MinChunkSizeTokens = 8;

    private enum SplitLevel
    {
        Paragraph,
        Line,
        Sentence,
        Word,
        Character,
    }

    public static int EstimateTokens(int characters) => characters <= 0 ? 0 : (characters + CharsPerToken - 1) / CharsPerToken;

    public static int EstimateTokens(string text) => EstimateTokens(text.Length);

    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        var sizeTokens = Math.Max(MinChunkSizeTokens, settings.ChunkSizeTokens);
        var overlapTokens = Math.Clamp(settings.ChunkOverlapTokens, 0, sizeTokens / 2);
        var maxChars = sizeTokens * CharsPerToken;
        var overlapChars = overlapTokens * CharsPerToken;

        // Pieces leave room for a full overlap, so any piece can follow the overlap of the previous chunk.
        var maxPiece = maxChars - overlapChars;

        var chunks = new List<TextChunk>();
        var pieces = new List<Range>();
        var ranges = new List<Range>();
        var sectionStart = 0;
        var page = 1;
        while (true)
        {
            var separator = settings.TrackPages ? text.IndexOf(Extraction.TextNormalizer.PageSeparator, sectionStart) : -1;
            var sectionEnd = separator < 0 ? text.Length : separator;

            pieces.Clear();
            ranges.Clear();
            Split(text, sectionStart, sectionEnd, SplitLevel.Paragraph, maxPiece, pieces);
            Pack(text, pieces, maxChars, overlapChars, ranges);
            foreach (var range in ranges)
            {
                var content = text[range];
                chunks.Add(new TextChunk(chunks.Count, content, settings.TrackPages ? page : null, EstimateTokens(content.Length)));
            }

            if (separator < 0)
            {
                return chunks;
            }

            sectionStart = separator + 1;
            page++;
        }
    }

    private static void Split(string text, int start, int end, SplitLevel level, int maxPiece, List<Range> pieces)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (start >= end)
        {
            return;
        }

        if (end - start <= maxPiece)
        {
            pieces.Add(new Range(start, end));
            return;
        }

        if (level == SplitLevel.Character)
        {
            SplitAtTextElements(text, start, end, maxPiece, pieces);
            return;
        }

        var cuts = FindCuts(text, start, end, level);
        var pieceStart = start;
        foreach (var cut in cuts)
        {
            Split(text, pieceStart, cut, level + 1, maxPiece, pieces);
            pieceStart = cut;
        }

        Split(text, pieceStart, end, level + 1, maxPiece, pieces);
    }

    private static List<int> FindCuts(string text, int start, int end, SplitLevel level)
    {
        var cuts = new List<int>();
        for (var i = start + 1; i < end; i++)
        {
            var c = text[i];
            switch (level)
            {
                case SplitLevel.Paragraph when c == '\n':
                    var next = i + 1;
                    while (next < end && text[next] is ' ' or '\t')
                    {
                        next++;
                    }

                    if (next < end && text[next] == '\n')
                    {
                        cuts.Add(i);
                        while (next < end && char.IsWhiteSpace(text[next]))
                        {
                            next++;
                        }

                        i = next - 1;
                    }

                    break;
                case SplitLevel.Line when c == '\n':
                    cuts.Add(i);
                    break;
                case SplitLevel.Sentence:
                    if (char.IsWhiteSpace(c) && !char.IsWhiteSpace(text[i - 1]) && EndsSentence(text, start, i - 1)
                        && !NextNonWhiteSpaceIsLower(text, i, end))
                    {
                        cuts.Add(i);
                    }
                    else if (!char.IsWhiteSpace(c) && IsCjkTerminator(text[i - 1]))
                    {
                        cuts.Add(i);
                    }

                    break;
                case SplitLevel.Word when char.IsWhiteSpace(c) && !char.IsWhiteSpace(text[i - 1]):
                    cuts.Add(i);
                    break;
            }
        }

        return cuts;
    }

    private static void SplitAtTextElements(string text, int start, int end, int maxPiece, List<Range> pieces)
    {
        var position = start;
        while (position < end)
        {
            var limit = Math.Min(end, position + maxPiece);
            var cut = limit < end ? TextElementBoundaryAtOrBefore(text, position, limit) : end;
            pieces.Add(new Range(position, cut));
            position = cut;
        }
    }

    private static void Pack(string text, List<Range> pieces, int maxChars, int overlapChars, List<Range> chunks)
    {
        if (pieces.Count == 0)
        {
            return;
        }

        var chunkStart = pieces[0].Start.Value;
        var chunkEnd = pieces[0].End.Value;
        for (var i = 1; i < pieces.Count; i++)
        {
            var pieceStart = pieces[i].Start.Value;
            var pieceEnd = pieces[i].End.Value;
            if (pieceEnd - chunkStart <= maxChars)
            {
                chunkEnd = pieceEnd;
                continue;
            }

            chunks.Add(new Range(chunkStart, chunkEnd));

            // The window start keeps the new chunk [overlapStart, pieceEnd) within maxChars.
            var overlapStart = overlapChars > 0
                ? FindOverlapStart(text, chunkStart, chunkEnd, Math.Max(chunkEnd - overlapChars, pieceEnd - maxChars), overlapChars)
                : -1;
            chunkStart = overlapStart >= 0 ? overlapStart : pieceStart;
            chunkEnd = pieceEnd;
        }

        chunks.Add(new Range(chunkStart, chunkEnd));
    }

    private static int FindOverlapStart(string text, int chunkStart, int chunkEnd, int windowStart, int overlapChars)
    {
        windowStart = Math.Max(windowStart, chunkStart + 1);
        if (windowStart >= chunkEnd)
        {
            return -1;
        }

        var firstWordStart = -1;
        for (var i = windowStart; i < chunkEnd; i++)
        {
            if (!IsWordStart(text, i))
            {
                continue;
            }

            if (firstWordStart < 0)
            {
                firstWordStart = i;
            }

            if (IsSentenceStart(text, chunkStart, i))
            {
                return chunkEnd - i >= overlapChars / 2 ? i : firstWordStart;
            }
        }

        if (firstWordStart >= 0)
        {
            return firstWordStart;
        }

        var boundary = TextElementBoundaryAtOrAfter(text, chunkStart, windowStart);
        return boundary < chunkEnd && !char.IsWhiteSpace(text[boundary]) ? boundary : -1;
    }

    private static bool IsWordStart(string text, int i) =>
        !char.IsWhiteSpace(text[i]) && (char.IsWhiteSpace(text[i - 1]) || IsCjkTerminator(text[i - 1]));

    private static bool IsSentenceStart(string text, int chunkStart, int i)
    {
        var j = i - 1;
        var sawWhiteSpace = false;
        while (j >= chunkStart && char.IsWhiteSpace(text[j]))
        {
            if (text[j] == '\n')
            {
                return true;
            }

            sawWhiteSpace = true;
            j--;
        }

        if (j < chunkStart)
        {
            return false;
        }

        return IsCjkTerminator(text[j]) || (sawWhiteSpace && EndsSentence(text, chunkStart, j));
    }

    private static bool EndsSentence(string text, int lowerBound, int index)
    {
        while (index >= lowerBound && IsClosingPunctuation(text[index]))
        {
            index--;
        }

        if (index < lowerBound)
        {
            return false;
        }

        var c = text[index];
        return c is '.' or '!' or '?' or '…' || IsCjkTerminator(c);
    }

    private static bool NextNonWhiteSpaceIsLower(string text, int index, int end)
    {
        while (index < end && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index < end && char.IsLower(text[index]);
    }

    private static bool IsCjkTerminator(char c) => c is '。' or '！' or '？' or '｡';

    private static bool IsClosingPunctuation(char c) => c is '"' or '\'' or '”' or '’' or '»' or ')' or ']' or '}' or '」' or '』' or '）';

    private static int TextElementBoundaryAtOrBefore(string text, int start, int limit)
    {
        var position = start;
        while (position < limit)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(position));
            if (length <= 0 || position + length > limit)
            {
                break;
            }

            position += length;
        }

        if (position > start)
        {
            return position;
        }

        // A grapheme cluster longer than the limit is cut inside, keeping surrogate pairs intact.
        return char.IsLowSurrogate(text[limit]) && limit - 1 > start ? limit - 1 : limit;
    }

    private static int TextElementBoundaryAtOrAfter(string text, int start, int from)
    {
        var position = start;
        while (position < from && position < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(position));
            position += Math.Max(1, length);
        }

        return Math.Min(position, text.Length);
    }
}
