using System.Globalization;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class CitationRegistry
{
    public const string ReferencePrefix = "S";
    public const int SnippetLength = 300;

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, string> _referenceByChunk = [];
    private readonly List<CitationDto> _turnCitations = [];
    private readonly Dictionary<string, int> _turnIndexByReference = new(StringComparer.Ordinal);
    private int _highestNumber;

    public CitationRegistry(IEnumerable<CitationDto>? previousCitations = null)
    {
        foreach (var citation in previousCitations ?? [])
        {
            _referenceByChunk.TryAdd(citation.ChunkId, citation.Ref);
            _highestNumber = Math.Max(_highestNumber, ParseNumber(citation.Ref));
        }
    }

    public IReadOnlyList<CitationDto> TurnCitations
    {
        get
        {
            lock (_gate)
            {
                return _turnCitations.ToList();
            }
        }
    }

    public IReadOnlyList<CitationDto> Register(IReadOnlyList<KnowledgeSearchHit> hits)
    {
        lock (_gate)
        {
            var citations = new List<CitationDto>(hits.Count);
            foreach (var hit in hits)
            {
                if (!_referenceByChunk.TryGetValue(hit.ChunkId, out var reference))
                {
                    reference = ReferencePrefix + (++_highestNumber).ToString(CultureInfo.InvariantCulture);
                    _referenceByChunk[hit.ChunkId] = reference;
                }

                var citation = new CitationDto(
                    reference,
                    hit.SourceId,
                    hit.ChunkId,
                    hit.SourceTitle,
                    hit.SourceUrl,
                    hit.Page,
                    TextUtil.Shorten(TextUtil.CollapseWhitespace(hit.Content), SnippetLength),
                    hit.Score);

                if (_turnIndexByReference.TryGetValue(reference, out var index))
                {
                    _turnCitations[index] = citation;
                }
                else
                {
                    _turnIndexByReference[reference] = _turnCitations.Count;
                    _turnCitations.Add(citation);
                }

                citations.Add(citation);
            }

            return citations;
        }
    }

    public static int ParseNumber(string? reference) =>
        reference is { Length: > 1 }
        && reference.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
}
