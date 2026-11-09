using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class GrepSourcesTool(IOptions<AgentOptions> options) : IAgentTool
{
    public const int DefaultPassageLimit = 10;
    public const int MaxPassageLimit = 25;
    public const int DefaultSourceLimit = 20;
    public const int MaxSourceLimit = 50;
    public const string PassagesOutput = "passages";
    public const string SourcesOutput = "sources";

    private const int HeaderBudget = 400;
    private const int HitHeaderBudget = 160;
    private const int MinPassageChars = 200;
    private const int MaxPassageChars = 700;

    private readonly int _maxChars = options.Value.MaxToolResultChars;

    public string Name => ToolNames.GrepSources;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.GrepSources,
        "Finds every passage that contains an exact word, name, number, code or phrase. Unlike search_knowledge_base, "
        + "which returns the few passages closest in meaning, it lists all matches page by page: use it for names, "
        + "numbers and quotes, to check whether something is mentioned at all, and with output=sources to list or count "
        + "every source that mentions it. Passages carry citation tags such as [S1]. Case is ignored and only whole "
        + "words match unless you change that. Neighbouring passages overlap slightly, so a mention near a boundary can "
        + "appear in two passages.",
        ToolSchemas.GrepSources(DefaultPassageLimit, MaxPassageLimit, DefaultSourceLimit, MaxSourceLimit, TextMatching.MaxTextLength));

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var text = TextMatching.Clean(arguments.GetString("text", required: true));
        var matchCase = arguments.GetBoolean("match_case") ?? false;
        var wholeWords = arguments.GetBoolean("whole_words") ?? true;
        var output = arguments.GetString("output") ?? PassagesOutput;
        var requestedSourceIds = arguments.GetGuidList("source_ids");
        var limit = arguments.GetInt32("limit");
        var offset = arguments.GetInt32("offset") ?? 0;
        arguments.ThrowIfInvalid(Name);

        if (text.Length == 0)
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'text' must not be empty.");
        }

        if (text.Length > TextMatching.MaxTextLength)
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'text' must be at most {TextMatching.MaxTextLength} characters.");
        }

        var listSources = string.Equals(output, SourcesOutput, StringComparison.OrdinalIgnoreCase);
        if (!listSources && !string.Equals(output, PassagesOutput, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'output' must be {PassagesOutput} or {SourcesOutput}.");
        }

        if (offset < 0)
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'offset' must be zero or positive.");
        }

        var sourceIds = SearchKnowledgeBaseTool.ResolveSourceFilter(invocation.Turn.ScopeSourceIds, requestedSourceIds);
        if (sourceIds is { Count: 0 })
        {
            return new ToolResult(SearchKnowledgeBaseTool.NoSourcesMessage, SearchKnowledgeBaseTool.NoSourcesMessage);
        }

        var search = invocation.Services.GetRequiredService<IExactSearch>();
        var match = new MatchDescription(TextUtil.CollapseWhitespace(text), matchCase, wholeWords);

        if (listSources)
        {
            var sources = await search.FindSourcesAsync(
                new ExactSearchRequest(text, matchCase, wholeWords, sourceIds, offset, Math.Clamp(limit ?? DefaultSourceLimit, 1, MaxSourceLimit)),
                cancellationToken);
            return new ToolResult(FormatSources(match, sources), SummarizeSources(match, sources)) { Untrusted = true };
        }

        var page = await search.FindPassagesAsync(
            new ExactSearchRequest(text, matchCase, wholeWords, sourceIds, offset, Math.Clamp(limit ?? DefaultPassageLimit, 1, MaxPassageLimit)),
            cancellationToken);
        if (page.Items.Count == 0)
        {
            return new ToolResult(FormatNoPassages(match, page), Summarize(match, page.TotalPassages, page.TotalSources));
        }

        // Citations keep the snippet, so the portal shows the passage around the match rather than its start.
        var pattern = new Regex(TextMatching.Pattern(text, matchCase, wholeWords), RegexOptions.CultureInvariant);
        var budget = Math.Clamp((_maxChars - HeaderBudget) / page.Items.Count - HitHeaderBudget, MinPassageChars, MaxPassageChars);
        var hits = page.Items.Select(hit => hit with { Content = Snippet(hit.Content, pattern, budget) }).ToList();
        var citations = invocation.Turn.Citations.Register(hits);
        return new ToolResult(
            FormatPassages(match, page, hits, citations),
            Summarize(match, page.TotalPassages, page.TotalSources),
            Citations: citations.DistinctBy(citation => citation.Ref).ToList()) { Untrusted = true };
    }

    public static string FormatPassages(
        MatchDescription match,
        ExactSearchPage<KnowledgeSearchHit> page,
        IReadOnlyList<KnowledgeSearchHit> hits,
        IReadOnlyList<CitationDto> citations)
    {
        var builder = new StringBuilder(Found(match, page.TotalPassages, page.TotalSources))
            .Append(string.Create(CultureInfo.InvariantCulture, $"; showing passages {page.Offset + 1}-{page.Offset + hits.Count}:"));
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            builder.Append("\n\n[").Append(citations[i].Ref).Append("] ").Append(TextUtil.CollapseWhitespace(hit.SourceTitle));
            if (hit.Page is int pageNumber)
            {
                builder.Append(" (page ").Append(pageNumber.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            builder.Append(" | source_id=").Append(hit.SourceId.ToString("D")).Append('\n').Append(hit.Content);
        }

        if (page.NextOffset is { } next)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"\n\nMore passages: call {ToolNames.GrepSources} again with offset={next}."));
        }

        return builder.ToString();
    }

    public static string FormatSources(MatchDescription match, ExactSearchPage<ExactSearchSource> page)
    {
        if (page.TotalPassages == 0)
        {
            return NoMatches(match);
        }

        var builder = new StringBuilder(Found(match, page.TotalPassages, page.TotalSources));
        if (page.Items.Count == 0)
        {
            return builder.Append(PastTheEnd(page.Offset)).ToString();
        }

        builder.Append(string.Create(CultureInfo.InvariantCulture, $"; showing sources {page.Offset + 1}-{page.Offset + page.Items.Count}, most passages first:"));
        foreach (var source in page.Items)
        {
            builder.Append("\n- ").Append(TextUtil.CollapseWhitespace(source.Title))
                .Append(" (id: ").Append(source.SourceId.ToString("D"))
                .Append(", passages: ").Append(source.Passages.ToString(CultureInfo.InvariantCulture));
            if (source.Url is { } url)
            {
                builder.Append(", url: ").Append(url);
            }

            builder.Append(')');
        }

        if (page.NextOffset is { } next)
        {
            builder.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"\nMore sources: call {ToolNames.GrepSources} again with output={SourcesOutput} and offset={next}."));
        }

        return builder.ToString();
    }

    // The match sits in the first quarter, so a snippet shortened from the start still shows it.
    internal static string Snippet(string content, Regex pattern, int maxChars)
    {
        var text = TextUtil.CollapseWhitespace(content);
        if (text.Length <= maxChars)
        {
            return text;
        }

        var match = pattern.Match(text);
        var matchStart = match.Success ? match.Index : 0;
        var matchEnd = match.Success ? match.Index + match.Length : 0;
        var start = Math.Clamp(matchStart - maxChars / 4, 0, text.Length - maxChars);
        var end = start + maxChars;

        if (start > 0)
        {
            var space = text.IndexOf(' ', start);
            if (space >= 0 && space < matchStart)
            {
                start = space + 1;
            }
            else if (char.IsLowSurrogate(text[start]))
            {
                start++;
            }
        }

        if (end < text.Length)
        {
            var space = text.LastIndexOf(' ', end);
            if (space > matchEnd)
            {
                end = space;
            }
            else if (char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
        }

        return (start > 0 ? TextUtil.Ellipsis.ToString() : string.Empty)
            + text[start..end].Trim()
            + (end < text.Length ? TextUtil.Ellipsis.ToString() : string.Empty);
    }

    private static string FormatNoPassages(MatchDescription match, ExactSearchPage<KnowledgeSearchHit> page) =>
        page.TotalPassages == 0
            ? NoMatches(match)
            : Found(match, page.TotalPassages, page.TotalSources) + PastTheEnd(page.Offset);

    private static string NoMatches(MatchDescription match) =>
        $"No passages contain \"{match.Text}\" ({match.Mode}). Try another spelling or fewer words"
        + (match.WholeWords ? ", or set whole_words to false" : string.Empty)
        + $"; {ToolNames.SearchKnowledgeBase} finds passages with related wording.";

    private static string Found(MatchDescription match, long passages, long sources) =>
        $"Found \"{match.Text}\" ({match.Mode}) in {Count(passages, "passage")} from {Count(sources, "source")}";

    private static string PastTheEnd(int offset) =>
        string.Create(CultureInfo.InvariantCulture, $". Offset {offset} is past the last result; use a smaller offset.");

    private static string Summarize(MatchDescription match, long passages, long sources) =>
        passages == 0
            ? $"No matches for \"{match.Text}\""
            : $"{Count(passages, "passage")} in {Count(sources, "source")} for \"{match.Text}\"";

    private static string SummarizeSources(MatchDescription match, ExactSearchPage<ExactSearchSource> page) =>
        page.TotalSources == 0 ? $"No matches for \"{match.Text}\""
        : page.Items.Count == page.TotalSources ? $"{Count(page.TotalSources, "source")} for \"{match.Text}\""
        : string.Create(CultureInfo.InvariantCulture, $"{page.Items.Count} of {Count(page.TotalSources, "source")} for \"{match.Text}\"");

    private static string Count(long count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}");

    internal sealed record MatchDescription(string Text, bool MatchCase, bool WholeWords)
    {
        public string Mode => $"{(WholeWords ? "whole words" : "also inside words")}, {(MatchCase ? "matching case" : "ignoring case")}";
    }
}
