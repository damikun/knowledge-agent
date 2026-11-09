using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Help;

// Knowledge search limited to the Internal folder, which no customer agent ever searches. Help agent only.
internal sealed class SearchInternalTool : IAgentTool
{
    public const string ToolName = "search_internal";
    public const string NothingToSearch = "Nothing to search: the Internal folder holds no sources. Answer from the product help.";
    private const int PassageChars = 800;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "What to look up in the team's Internal sources, such as \"refund policy for resellers\"."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 8,
              "description": "Number of passages to return (default 5)."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public string Name => ToolName;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolName,
        "Searches the team's Internal sources: staff-only documents they filed in the Internal folder on the Sources page, "
        + "such as their own procedures and policies. Use it for questions about how this team works rather than how the "
        + "product works. Returns passages tagged [S1], [S2] ...; cite the tag of every passage you use.",
        Schema);

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var query = invocation.Arguments.GetString("query", required: true);
        var limit = Math.Clamp(invocation.Arguments.GetInt32("limit") ?? HelpIndex.DefaultLimit, 1, HelpIndex.MaxLimit);
        invocation.Arguments.ThrowIfInvalid(Name);

        var services = invocation.Services;
        var tree = new SourceFolderTree(await services.GetRequiredService<ISourceFolderRepository>().ListAsync(cancellationToken));
        var internalFolders = tree.InternalFolderIds();
        var sourceIds = internalFolders.Count == 0
            ? []
            : await services.GetRequiredService<ISourceRepository>().ListIdsInFoldersAsync(internalFolders, cancellationToken);
        if (sourceIds.Count == 0)
        {
            return new ToolResult(NothingToSearch, "No Internal sources");
        }

        IReadOnlyList<KnowledgeSearchHit> hits;
        try
        {
            hits = (await services.GetRequiredService<IKnowledgeSearch>().SearchAsync(new KnowledgeSearchRequest(query!, limit, sourceIds), cancellationToken)).Hits;
        }
        catch (ModelNotConfiguredException)
        {
            return new ToolResult(
                "Nothing to search: no embedding model is connected, so the Internal sources cannot be searched. Answer from the product help.",
                "Internal sources need an embedding model");
        }

        if (hits.Count == 0)
        {
            return new ToolResult($"No Internal passages found for \"{query}\".", $"No Internal passages for \"{query}\"");
        }

        var citations = invocation.Turn.Citations.Register(hits
            .Select(hit => hit with { SourceTitle = $"Internal: {hit.SourceTitle}", SourceUrl = $"/sources?source={hit.SourceId:D}" })
            .ToList());
        var builder = new StringBuilder($"Found {hits.Count} passage(s) in the Internal sources for \"{query}\".");
        for (var i = 0; i < hits.Count; i++)
        {
            builder.Append("\n\n[").Append(citations[i].Ref).Append("] Internal source: ").Append(TextUtil.CollapseWhitespace(hits[i].SourceTitle))
                .Append('\n').Append(TextUtil.Shorten(hits[i].Content.Trim(), PassageChars));
        }

        var summary = hits.Count == 1 ? $"1 Internal passage for \"{query}\"" : $"{hits.Count} Internal passages for \"{query}\"";
        return new ToolResult(builder.ToString(), summary, Citations: citations.DistinctBy(citation => citation.Ref).ToList()) { Untrusted = true };
    }
}
