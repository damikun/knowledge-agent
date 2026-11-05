using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Search;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Help;

// Given only to the help agent and never registered, so no customer agent can use it.
internal sealed class SearchHelpTool(HelpIndex index, string? page) : IAgentTool
{
    public const string ToolName = "search_help";
    private const int PassageChars = 800;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "What to look up in the product help, in a few words, such as \"add an API key\" or \"widget snippet\"."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 8,
              "description": "Number of help sections to return (default 5)."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public string Name => ToolName;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolName,
        "Searches the product's own help by keywords: how to set up and use this support platform (models, sources, agents, "
        + "widgets, the Inbox, contacts, labels, evaluations, alerts, tools from MCP servers, notifications). Use it when the "
        + "help contents do not show where the answer is. Returns the start of each matching section tagged [S1], [S2] ... "
        + "with its id; read a section whole with read_help_section, and cite the tag of every section you use.",
        Schema);

    public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var query = invocation.Arguments.GetString("query", required: true);
        var limit = invocation.Arguments.GetInt32("limit");
        invocation.Arguments.ThrowIfInvalid(Name);

        var hits = index.Search(query!, limit ?? HelpIndex.DefaultLimit, page);
        if (hits.Count == 0)
        {
            return Task.FromResult(new ToolResult($"No help sections found for \"{query}\". Try other words.", $"No help for \"{query}\""));
        }

        var citations = invocation.Turn.Citations.Register(hits.Select(ToSearchHit).ToList());
        var builder = new StringBuilder($"Found {hits.Count} help section(s) for \"{query}\".");
        for (var i = 0; i < hits.Count; i++)
        {
            var section = hits[i].Section;
            builder.Append("\n\n[").Append(citations[i].Ref).Append("] ").Append(section.Path).Append(" (id: ").Append(section.Id);
            if (section.Link is { } link)
            {
                builder.Append(", portal page: ").Append(link);
            }

            builder.Append(')');

            builder.Append('\n').Append(TextUtil.Shorten(section.Text.Trim(), PassageChars));
        }

        var summary = hits.Count == 1 ? $"1 help section for \"{query}\"" : $"{hits.Count} help sections for \"{query}\"";
        return Task.FromResult(new ToolResult(builder.ToString(), summary, Citations: citations.DistinctBy(citation => citation.Ref).ToList()));
    }

    public static KnowledgeSearchHit ToSearchHit(HelpHit hit) => new(
        hit.Section.Key,
        hit.Section.Key,
        0,
        hit.Section.Path,
        hit.Section.Link,
        null,
        HelpMarkdown.Plain(hit.Section.Text),
        hit.Score);
}
