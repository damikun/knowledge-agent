using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Agents;

internal static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/agents").WithTags("Agents");

        group.MapGet("/", ListAsync)
            .WithName("ListAgents")
            .WithSummary("Every agent, the built-in one first.");
        group.MapGet("/options", (IAgentService agents) => TypedResults.Ok(agents.GetOptions()))
            .WithName("GetAgentOptions")
            .WithSummary("The tools and chat profiles an agent can use.");
        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAgent");
        group.MapPost("/", CreateAsync)
            .WithName("CreateAgent")
            .WithSummary("Creates an agent with a persona, a knowledge scope (folders), tools and a handover policy: when it offers a person in widgets that offer one.");
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAgent")
            .WithSummary("Replaces an agent. Without a handover policy it keeps its own.");
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAgent")
            .WithSummary("Deletes an agent. The built-in agent cannot be deleted. An agent others hand off to answers 409 with them, unless removeFromTeams=true removes it from their specialists first.");

        return group;
    }

    private static async Task<Ok<IReadOnlyList<AgentDto>>> ListAsync(IAgentService agents, CancellationToken cancellationToken) =>
        TypedResults.Ok(await agents.ListAsync(cancellationToken));

    private static async Task<Results<Ok<AgentDto>, ProblemHttpResult>> GetAsync(Guid id, IAgentService agents, CancellationToken cancellationToken) =>
        await agents.GetAsync(id, cancellationToken) is { } agent ? TypedResults.Ok(agent) : NotFound(id);

    private static async Task<Created<AgentDto>> CreateAsync(SaveAgentRequest request, IAgentService agents, CancellationToken cancellationToken)
    {
        var agent = await agents.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/agents/{agent.Id}", agent);
    }

    private static async Task<Results<Ok<AgentDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SaveAgentRequest request,
        IAgentService agents,
        CancellationToken cancellationToken) =>
        await agents.UpdateAsync(id, request, cancellationToken) is { } agent ? TypedResults.Ok(agent) : NotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        IAgentService agents,
        CancellationToken cancellationToken,
        bool removeFromTeams = false)
    {
        try
        {
            return await agents.DeleteAsync(id, removeFromTeams, cancellationToken) ? TypedResults.NoContent() : NotFound(id);
        }
        catch (AgentIsSpecialistException ex)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Other agents hand off to this agent",
                detail: $"{ex.Message} Delete it with removeFromTeams=true to remove it from their specialists as well.",
                extensions: new Dictionary<string, object?> { ["code"] = SpecialistInUseCode, ["agents"] = ex.Agents });
        }
    }

    public const string SpecialistInUseCode = "agent_is_specialist";

    private static ProblemHttpResult NotFound(Guid id) => ApiResults.NotFound($"Agent '{id}' was not found.");
}
