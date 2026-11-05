using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Agents;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed record SpecialistChoice(AgentConfig Agent, string WhenToUse);

internal sealed class TransferToAgentTool : IAgentTool
{
    private readonly Dictionary<string, SpecialistChoice> _specialists;

    public TransferToAgentTool(IReadOnlyList<SpecialistChoice> specialists)
    {
        _specialists = specialists.ToDictionary(choice => choice.Agent.Key, StringComparer.OrdinalIgnoreCase);
        var description = new StringBuilder(
            "Hands this conversation to a specialist colleague, who answers the person's current message right away, "
            + "with the conversation so far, and the messages after it. Use it when the person's need matches one of "
            + "these specialists; do not answer that need yourself. Specialists:");
        foreach (var choice in specialists)
        {
            description.Append("\n- ").Append(choice.Agent.Key).Append(" (").Append(choice.Agent.Name).Append("): ").Append(choice.WhenToUse);
        }

        Declaration = AIFunctionFactory.CreateDeclaration(ToolNames.TransferToAgent, description.ToString(), Schema(specialists.Select(choice => choice.Agent.Key)));
    }

    public string Name => ToolNames.TransferToAgent;

    public AIFunctionDeclaration Declaration { get; }

    public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var key = invocation.Arguments.GetString("agent", required: true)?.Trim();
        var reason = invocation.Arguments.GetString("reason", required: true)?.Trim() ?? string.Empty;
        invocation.Arguments.ThrowIfInvalid(Name);
        if (key is null || !_specialists.TryGetValue(key, out var choice))
        {
            return Task.FromResult(ToolResult.Failure($"'{key}' is not one of your specialists. Choose one of: {string.Join(", ", _specialists.Keys)}."));
        }

        var target = choice.Agent;
        if (!invocation.Turn.RequestTransfer(new AgentTransfer(target, reason, invocation.CallId, Back: false)))
        {
            return Task.FromResult(ToolResult.Failure("The conversation was already handed to another agent in this reply."));
        }

        return Task.FromResult(new ToolResult(
            $"Handed to {target.Name}. {target.Name} answers the person now, in this same reply. Do not write anything more.",
            $"Handed to {target.Name}")
        {
            EndsRun = true,
        });
    }

    private static JsonElement Schema(IEnumerable<string> keys)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["agent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
                    ["description"] = "The key of the specialist to hand the conversation to.",
                },
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why this specialist, in a few words, such as 'question about an invoice'. The team sees it.",
                },
            },
            ["required"] = new JsonArray("agent", "reason"),
            ["additionalProperties"] = false,
        };
        return JsonSerializer.SerializeToElement(schema);
    }
}

internal sealed class TransferBackTool(AgentConfig frontAgent) : IAgentTool
{
    public string Name => ToolNames.TransferBack;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.TransferBack,
        $"Hands this conversation back to {frontAgent.Name}, who answers the person's current message right away. Use it "
        + "when the person moves on to something outside what you are meant to handle; do not answer that yourself.",
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Why you hand it back, in a few words, such as 'asks about shipping now'. The team sees it.",
                },
            },
            ["required"] = new JsonArray("reason"),
            ["additionalProperties"] = false,
        }));

    public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var reason = invocation.Arguments.GetString("reason", required: true)?.Trim() ?? string.Empty;
        invocation.Arguments.ThrowIfInvalid(Name);
        if (!invocation.Turn.RequestTransfer(new AgentTransfer(frontAgent, reason, invocation.CallId, Back: true)))
        {
            return Task.FromResult(ToolResult.Failure("The conversation was already handed to another agent in this reply."));
        }

        return Task.FromResult(new ToolResult(
            $"Handed back to {frontAgent.Name}, who answers the person now, in this same reply. Do not write anything more.",
            $"Handed back to {frontAgent.Name}")
        {
            EndsRun = true,
        });
    }
}
