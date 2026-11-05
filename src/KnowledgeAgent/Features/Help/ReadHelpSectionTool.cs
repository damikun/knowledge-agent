using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Help;

internal sealed class ReadHelpSectionTool(HelpIndex index) : IAgentTool
{
    public const string ToolName = "read_help_section";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "The section's id from the help contents, such as \"guide/connect-a-model#add-an-api-key\"."
            }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public string Name => ToolName;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolName,
        "Reads one section of the product's own help in full, as Markdown with its links to portal pages. Take the id from "
        + "the help contents or from search_help. Returns the section tagged [S1], [S2] ...; cite the tag when you use it.",
        Schema);

    public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var id = invocation.Arguments.GetString("id", required: true);
        invocation.Arguments.ThrowIfInvalid(Name);

        var section = index.Find(id);
        if (section is null)
        {
            return Task.FromResult(new ToolResult(
                $"No help section has the id \"{id}\". Use an id exactly as the help contents list it, or find one with {SearchHelpTool.ToolName}.",
                $"No help section \"{id}\"",
                IsError: true));
        }

        var citation = invocation.Turn.Citations.Register([SearchHelpTool.ToSearchHit(new HelpHit(section, 1))])[0];
        var builder = new StringBuilder();
        builder.Append('[').Append(citation.Ref).Append("] ").Append(section.Path).Append(" (id: ").Append(section.Id);
        if (section.Link is { } link)
        {
            builder.Append(", portal page: ").Append(link);
        }

        builder.Append(")\n\n").Append(section.Text.Trim());
        return Task.FromResult(new ToolResult(builder.ToString(), $"Read \"{section.Path}\"", Citations: [citation]));
    }
}
