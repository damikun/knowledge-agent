using System.Text;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Features.Help;

internal sealed record HelpHit(HelpSection Section, double Score);

internal sealed class HelpIndex
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 8;

    // Repository docs are for whoever runs the installation, so they rank below the guide.
    private const double DocsWeight = 0.6;

    // Sections about the page the person is on come first among equals.
    private const double PageBoost = 1.25;

    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, HelpSection> _byId;

    public HelpIndex(HelpSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Version = snapshot.Version;
        Sections = snapshot.Sections;
        _entries = snapshot.Sections.Select(section => new Entry(section)).ToList();
        _byId = new Dictionary<string, HelpSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in snapshot.Sections)
        {
            _byId.TryAdd(section.Id, section);
        }

        Contents = BuildContents(snapshot.Sections);
    }

    public string Version { get; }

    public IReadOnlyList<HelpSection> Sections { get; }

    public string Contents { get; }

    public HelpSection? Find(string? id)
    {
        var key = id?.Trim().Trim('`', '"', '\'').Trim();
        return string.IsNullOrEmpty(key) ? null : _byId.GetValueOrDefault(key);
    }

    public IReadOnlyList<HelpHit> Search(string query, int limit, string? page)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var terms = QueryTermAnalyzer.Terms(query);
        if (terms.Count == 0)
        {
            return [];
        }

        var patterns = terms.Select(term => new Regex(KeywordScoring.Pattern(term), RegexOptions.CultureInvariant)).ToArray();
        var frequencies = new int[_entries.Count][];
        var documentFrequencies = new long[patterns.Length];
        long totalLength = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            totalLength += _entries[i].Length;
            frequencies[i] = new int[patterns.Length];
            for (var term = 0; term < patterns.Length; term++)
            {
                var count = patterns[term].Count(_entries[i].SearchText);
                frequencies[i][term] = count;
                if (count > 0)
                {
                    documentFrequencies[term]++;
                }
            }
        }

        var averageLength = _entries.Count == 0 ? 0 : (double)totalLength / _entries.Count;
        var idf = documentFrequencies.Select(frequency => KeywordScoring.Idf(_entries.Count, frequency)).ToArray();
        var pageSegment = HelpPages.Segment(page);
        var hits = new List<HelpHit>();
        for (var i = 0; i < _entries.Count; i++)
        {
            var score = 0d;
            for (var term = 0; term < patterns.Length; term++)
            {
                score += KeywordScoring.TermScore(idf[term], frequencies[i][term], _entries[i].Length, averageLength);
            }

            if (score <= 0)
            {
                continue;
            }

            var section = _entries[i].Section;
            score *= section.Origin == HelpOrigin.Docs ? DocsWeight : 1;
            if (pageSegment is not null && HelpPages.Segment(section.Link) == pageSegment)
            {
                score *= PageBoost;
            }

            hits.Add(new HelpHit(section, score));
        }

        return hits
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Section.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private static string BuildContents(IReadOnlyList<HelpSection> sections)
    {
        var builder = new StringBuilder();
        foreach (var origin in new[] { HelpOrigin.Guide, HelpOrigin.Docs })
        {
            var documents = sections.Where(section => section.Origin == origin).GroupBy(section => section.Document).ToList();
            if (documents.Count == 0)
            {
                continue;
            }

            builder.Append(builder.Length == 0 ? string.Empty : "\n")
                .Append(origin == HelpOrigin.Guide ? "## Guide (how to use the portal)\n" : "## Repository docs (for whoever runs the installation)\n");
            foreach (var document in documents)
            {
                builder.Append('\n').Append("### ").Append(document.First().DocumentTitle).Append('\n');
                foreach (var section in document)
                {
                    builder.Append("- `").Append(section.Id).Append("` ").Append(section.Title);
                    if (section.Summary.Length > 0)
                    {
                        builder.Append(": ").Append(section.Summary);
                    }

                    builder.Append('\n');
                }
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed class Entry
    {
        public Entry(HelpSection section)
        {
            Section = section;

            // The path twice, so a word in a heading weighs more than one in the text.
            SearchText = $"{section.Path}\n{section.Path}\n{HelpMarkdown.Plain(section.Text)}";
            Length = KeywordScoring.Length(SearchText);
        }

        public HelpSection Section { get; }
        public string SearchText { get; }
        public int Length { get; }
    }
}
