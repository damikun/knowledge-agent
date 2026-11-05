using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace KnowledgeAgent.Features.Agents.Access;

internal static class AgentAccessEndpoints
{
    public static RouteGroupBuilder MapAgentAccessEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/agents/{id:guid}").WithTags("Agents");

        group.MapGet("/access", async (Guid id, IAgentApiKeyService keys, CancellationToken cancellationToken) =>
                await keys.GetAccessAsync(id, cancellationToken) is { } access ? Results.Ok(access) : NotFound(id))
            .WithName("GetAgentAccess")
            .WithSummary("Whether the agent answers as an MCP server and over the HTTP API, and where.");
        group.MapPut("/access", async (Guid id, SaveAgentAccessRequest request, IAgentApiKeyService keys, CancellationToken cancellationToken) =>
                await keys.SaveAccessAsync(id, request, cancellationToken) is { } access ? Results.Ok(access) : NotFound(id))
            .WithName("SaveAgentAccess")
            .WithSummary("Turns the agent's MCP server and HTTP API on or off; a value left out stays.");
        group.MapGet("/api-keys", async (Guid id, IAgentApiKeyService keys, CancellationToken cancellationToken) =>
                await keys.ListAsync(id, cancellationToken) is { } list ? Results.Ok(list) : NotFound(id))
            .WithName("ListAgentApiKeys")
            .WithSummary("The agent's API keys, without the keys themselves: active first, then revoked.");
        group.MapPost("/api-keys", async (Guid id, CreateAgentApiKeyRequest request, ClaimsPrincipal user, IAgentApiKeyService keys, CancellationToken cancellationToken) =>
                await keys.CreateAsync(id, request, StaffClaims.DisplayName(user), cancellationToken) is { } created
                    ? Results.Created($"/api/agents/{id}/api-keys/{created.Key.Id}", created)
                    : NotFound(id))
            .WithName("CreateAgentApiKey")
            .WithSummary("Creates a key for this agent. The response is the only time the key is shown; only its hash is stored.");
        group.MapDelete("/api-keys/{keyId:guid}", async (Guid id, Guid keyId, IAgentApiKeyService keys, CancellationToken cancellationToken) =>
                await keys.RevokeAsync(id, keyId, cancellationToken) ? Results.NoContent() : ApiResults.NotFound($"API key '{keyId}' was not found."))
            .WithName("RevokeAgentApiKey")
            .WithSummary("Revokes a key: it stops working at once and stays listed as revoked.");

        return group;
    }

    // Outside /api's staff sign-in: the agent's API key authorizes.
    public static void MapAgentPublicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/public/agents/{agentKey}")
            .WithTags("Agent API")
            .RequireAuthorization(AgentKeyAuth.ApiPolicy);
        api.MapPost("/ask", AskAsync)
            .WithName("AskAgent")
            .WithSummary("Asks the agent a question with one of its API keys. Answers with citations; send conversationId to ask a follow-up.");
        api.MapPost("/search", SearchAsync)
            .WithName("SearchAgentKnowledge")
            .WithSummary("Searches what the agent may search and returns passages with their source titles.");

        endpoints.MapMcp(AgentApiKeyService.McpPath("{" + AgentKeyAuth.AgentKeyRoute + "}"))
            .RequireAuthorization(AgentKeyAuth.McpPolicy)
            .WithTags("Agent MCP server");
    }

    private static async Task<IResult> AskAsync(
        AgentAskRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        IAgentRepository agents,
        AgentKeyLookup keys,
        AgentAskService ask,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        var (agent, key, rejection) = await ResolveAsync(user, http, agents, keys, limiter, cancellationToken);
        if (rejection is not null)
        {
            return rejection;
        }

        try
        {
            return Results.Ok(await ask.AskAsync(agent!, key!, ConversationChannel.Api, request, cancellationToken));
        }
        catch (AgentAskException ex)
        {
            return Problem(ex);
        }
    }

    private static async Task<IResult> SearchAsync(
        AgentSearchRequest request,
        ClaimsPrincipal user,
        HttpContext http,
        IAgentRepository agents,
        AgentKeyLookup keys,
        AgentAskService ask,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        var (agent, _, rejection) = await ResolveAsync(user, http, agents, keys, limiter, cancellationToken);
        if (rejection is not null)
        {
            return rejection;
        }

        try
        {
            return Results.Ok(await ask.SearchAsync(agent!, request, cancellationToken));
        }
        catch (AgentAskException ex)
        {
            return Problem(ex);
        }
    }

    private static async Task<(AgentConfig? Agent, AgentApiKey? Key, IResult? Rejection)> ResolveAsync(
        ClaimsPrincipal user,
        HttpContext http,
        IAgentRepository agents,
        AgentKeyLookup keys,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        IResult? rejection = null;
        var keyId = AgentKeyAuth.KeyId(user);
        if (!limiter.TryAcquire("agent-key:" + keyId.ToString("N"), AgentKeyAuth.RequestsPerMinute(user), out var retryAfter))
        {
            http.Response.Headers[HeaderNames.RetryAfter] = WidgetRateLimiter.RetryAfterSeconds(retryAfter);
            rejection = TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: "This key sent too many requests. Wait a moment and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = PublicWidgetEndpoints.RateLimitedCode });
        }

        var agent = rejection is null ? await agents.GetAsync(AgentKeyAuth.AgentId(user), cancellationToken) : null;
        var key = agent is not null ? await keys.GetAsync(keyId, cancellationToken) : null;
        if (rejection is null && (agent is null || key is null))
        {
            rejection = TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "API key required", detail: "The API key is not valid or was revoked.");
        }

        return rejection is not null ? (null, null, rejection) : (agent, key, null);
    }

    private static IResult Problem(AgentAskException ex) => TypedResults.Problem(
        statusCode: ex.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            ChatErrorCodes.ConversationBusy or "conversation_closed" => StatusCodes.Status409Conflict,
            "invalid_question" or "invalid_query" or "invalid_conversation" => StatusCodes.Status400BadRequest,
            ChatErrorCodes.ModelNotConfigured or ChatErrorCodes.ProviderError => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        },
        title: "The agent could not answer",
        detail: ex.Message,
        extensions: new Dictionary<string, object?> { ["code"] = ex.Code });

    private static IResult NotFound(Guid id) => ApiResults.NotFound($"Agent '{id}' was not found.");
}
