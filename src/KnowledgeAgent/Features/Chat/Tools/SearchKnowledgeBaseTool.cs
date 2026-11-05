using System.Globalization;
using System.Text;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class SearchKnowledgeBaseTool(IOptions<AgentOptions> options) : IAgentTool
{
    private const int HitHeaderBudget = 160;
    private const int MinPassageChars = 300;

    private readonly AgentOptions _options = options.Value;

    public string Name => ToolNames.SearchKnowledgeBase;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.SearchKnowledgeBase,
        "Semantic search over the user's knowledge base (web pages, PDFs and notes they added). Returns the most "
        + "relevant passages, each tagged with a citation reference such as [S1]; support every fact you take from "
        + "a passage with its tag. Search again with rephrased or more specific queries when results are weak.",
        ToolSchemas.SearchKnowledgeBase(DefaultTopK(options.Value), MaxTopK(options.Value)));

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var query = arguments.GetString("query", required: true);
        var topK = arguments.GetInt32("top_k");
        var requestedSourceIds = arguments.GetGuidList("source_ids");
        arguments.ThrowIfInvalid(Name);

        var count = Math.Clamp(topK ?? DefaultTopK(_options), 1, MaxTopK(_options));
        var sourceIds = ResolveSourceFilter(invocation.Turn.ScopeSourceIds, requestedSourceIds);
        if (sourceIds is { Count: 0 })
        {
            return new ToolResult(NoSourcesMessage, NoSourcesMessage);
        }

        var search = invocation.Services.GetRequiredService<IKnowledgeSearch>();
        var hits = await search.SearchAsync(new KnowledgeSearchRequest(query!, count, sourceIds), cancellationToken);

        if (hits.Count == 0)
        {
            return new ToolResult(FormatNoResults(query!), $"No passages for \"{query}\"");
        }

        var citations = invocation.Turn.Citations.Register(hits);
        var text = Format(query!, hits, citations, _options.MaxToolResultChars);
        var summary = hits.Count == 1 ? $"1 passage for \"{query}\"" : $"{hits.Count} passages for \"{query}\"";
        return new ToolResult(text, summary, Citations: citations.DistinctBy(citation => citation.Ref).ToList());
    }

    // Test chat clients parse the output of FormatNoResults and Format, so both formats are a contract.
    public static string FormatNoResults(string query) => $"No passages found for \"{query}\".";

    public static string Format(string query, IReadOnlyList<KnowledgeSearchHit> hits, IReadOnlyList<CitationDto> citations, int maxChars)
    {
        var header = string.Create(CultureInfo.InvariantCulture, $"Found {hits.Count} passage(s) for \"{query}\".");
        var passageBudget = Math.Max(MinPassageChars, (maxChars - header.Length) / Math.Max(1, hits.Count) - HitHeaderBudget);

        var builder = new StringBuilder(header);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            builder.Append("\n\n[").Append(citations[i].Ref).Append("] ").Append(TextUtil.CollapseWhitespace(hit.SourceTitle));
            if (hit.Page is int page)
            {
                builder.Append(" (page ").Append(page.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            builder.Append(" | score ").Append(hit.Score.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" | source_id=").Append(hit.SourceId.ToString("D"))
                .Append('\n')
                .Append(TextUtil.Shorten(hit.Content.Trim(), passageBudget));
        }

        return builder.ToString();
    }

    public const string NoSourcesMessage = "No sources are available to this agent yet.";

    // Null searches every source. The requested sources can narrow the scope but never widen it.
    public static IReadOnlyCollection<Guid>? ResolveSourceFilter(IReadOnlyList<Guid>? scope, IReadOnlyList<Guid> requested)
    {
        if (scope is null)
        {
            return requested.Count == 0 ? null : requested;
        }

        if (requested.Count == 0)
        {
            return scope;
        }

        var narrowed = requested.Where(scope.Contains).ToList();
        return narrowed.Count > 0 ? narrowed : scope;
    }

    private static int MaxTopK(AgentOptions options) => Math.Max(1, options.MaxTopK);

    private static int DefaultTopK(AgentOptions options) => Math.Clamp(options.DefaultTopK, 1, MaxTopK(options));
}
