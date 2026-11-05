using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Widgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeAgent.Features.Agents.Access;

// Stateless Streamable HTTP, so any instance can answer.
internal static class AgentMcpServer
{
    public const string AskTool = "ask";
    public const string SearchTool = "search_knowledge";

    private static readonly JsonElement AskSchema = Parse("""
        {
          "type": "object",
          "properties": {
            "question": { "type": "string", "description": "The question, in the words of the person who asks." },
            "conversation_id": { "type": "string", "description": "The conversation_id of an earlier answer, to ask a follow-up question in the same conversation." }
          },
          "required": ["question"]
        }
        """);

    private static readonly JsonElement SearchSchema = Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "What to look for." },
            "top_k": { "type": "integer", "minimum": 1, "maximum": 20, "description": "How many passages to return; 6 when left out." }
          },
          "required": ["query"]
        }
        """);

    public static IServiceCollection AddAgentMcpServer(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "knowledge-agent", Version = "1.0" };
                options.ServerInstructions = "Ask this support agent questions with the ask tool; it answers from its knowledge base with citations. "
                    + "Use search_knowledge to read the passages themselves.";
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler((request, cancellationToken) => ValueTask.FromResult(ListTools(request.User)))
            .WithCallToolHandler(CallToolAsync);
        return services;
    }

    private static ListToolsResult ListTools(ClaimsPrincipal? user) => new()
    {
        Tools = user?.Identity?.IsAuthenticated == true
            ?
            [
                new Tool
                {
                    Name = AskTool,
                    Title = "Ask the agent",
                    Description = "Asks the support agent a question. It answers from its knowledge base, with numbered citations of the sources it used, and returns a conversation_id for follow-up questions.",
                    InputSchema = AskSchema,
                    Annotations = new ToolAnnotations { Title = "Ask the agent", ReadOnlyHint = true, OpenWorldHint = false },
                },
                new Tool
                {
                    Name = SearchTool,
                    Title = "Search the knowledge base",
                    Description = "Searches the agent's knowledge base and returns the closest passages with the titles of their sources.",
                    InputSchema = SearchSchema,
                    Annotations = new ToolAnnotations { Title = "Search the knowledge base", ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
                },
            ]
            : [],
    };

    private static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var user = request.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new McpProtocolException("An API key is required.", McpErrorCode.InvalidRequest);
        }

        var name = request.Params?.Name;
        if (name is not (AskTool or SearchTool))
        {
            throw new McpProtocolException($"Unknown tool '{name}'.", McpErrorCode.InvalidParams);
        }

        await using var scope = request.Services!.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var services = scope.ServiceProvider;
        var limiter = services.GetRequiredService<WidgetRateLimiter>();
        var keyId = AgentKeyAuth.KeyId(user);
        if (!limiter.TryAcquire("agent-key:" + keyId.ToString("N"), AgentKeyAuth.RequestsPerMinute(user), out var retryAfter))
        {
            return Error(string.Create(CultureInfo.InvariantCulture, $"Too many requests with this key. Try again in {Math.Ceiling(retryAfter.TotalSeconds):0} seconds."));
        }

        var agents = services.GetRequiredService<IAgentRepository>();
        var agent = await agents.GetAsync(AgentKeyAuth.AgentId(user), cancellationToken);
        var key = await services.GetRequiredService<AgentKeyLookup>().GetAsync(keyId, cancellationToken);
        if (agent is null || key is null)
        {
            return Error("This agent is no longer available.");
        }

        var arguments = request.Params!.Arguments ?? new Dictionary<string, JsonElement>();
        var ask = services.GetRequiredService<AgentAskService>();
        try
        {
            if (name == AskTool)
            {
                var conversationId = Text(arguments, "conversation_id") is { } text
                    ? Guid.TryParse(text, out var parsed) ? parsed : throw new AgentAskException("invalid_conversation", "conversation_id is not a conversation id.")
                    : (Guid?)null;
                var answer = await ask.AskAsync(agent, key, ConversationChannel.Mcp, new AgentAskRequest(Text(arguments, "question") ?? string.Empty, conversationId), cancellationToken);
                return Ok(FormatAnswer(answer));
            }

            var topK = arguments.TryGetValue("top_k", out var top) && top.ValueKind == JsonValueKind.Number && top.TryGetInt32(out var number) ? number : (int?)null;
            var result = await ask.SearchAsync(agent, new AgentSearchRequest(Text(arguments, "query") ?? string.Empty, topK), cancellationToken);
            return Ok(FormatPassages(result));
        }
        catch (AgentAskException ex)
        {
            return Error(ex.Message);
        }
    }

    internal static string FormatAnswer(AgentAnswerDto answer)
    {
        var builder = new StringBuilder(answer.Answer.Trim());
        if (answer.Citations.Count > 0)
        {
            builder.Append("\n\nSources:");
            foreach (var citation in answer.Citations)
            {
                builder.Append("\n[").Append(citation.Ref).Append("] ").Append(citation.SourceTitle);
                if (citation.SourceUrl is { } url)
                {
                    builder.Append(" (").Append(url).Append(')');
                }
            }
        }

        builder.Append("\n\nconversation_id: ").Append(answer.ConversationId.ToString("D"));
        return builder.ToString();
    }

    internal static string FormatPassages(AgentSearchResultDto result)
    {
        if (result.Passages.Count == 0)
        {
            return "No passages found.";
        }

        var builder = new StringBuilder();
        for (var i = 0; i < result.Passages.Count; i++)
        {
            var passage = result.Passages[i];
            if (i > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append('[').Append(i + 1).Append("] ").Append(passage.SourceTitle);
            if (passage.Page is { } page)
            {
                builder.Append(" (page ").Append(page.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            if (passage.SourceUrl is { } url)
            {
                builder.Append(' ').Append(url);
            }

            builder.Append('\n').Append(passage.Content.Trim());
        }

        return builder.ToString();
    }

    private static string? Text(IDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static CallToolResult Ok(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) => new() { Content = [new TextContentBlock { Text = text }], IsError = true };

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

// Read again so a revoked key stops working at once.
internal sealed class AgentKeyLookup(Infrastructure.Persistence.KnowledgeDbContext db, Infrastructure.Persistence.DbContextGate gate)
{
    public async Task<AgentApiKey?> GetAsync(Guid keyId, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var key = await db.AgentApiKeys.AsNoTracking().FirstOrDefaultAsync(item => item.Id == keyId, cancellationToken);
        return key is { RevokedAt: null } ? key : null;
    }
}
