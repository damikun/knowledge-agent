using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Connections.Mcp;

internal static class McpServerEndpoints
{
    public static RouteGroupBuilder MapMcpServerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/connections/mcp").WithTags("Connections");

        group.MapGet("/", async (IMcpServerService service, CancellationToken cancellationToken) => TypedResults.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListMcpServers")
            .WithSummary("The MCP servers agents can use tools from, with their cached tools and the agents using them. Secrets are never returned.");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetMcpServer");
        group.MapPost("/", CreateAsync)
            .WithName("CreateMcpServer")
            .WithSummary("Connects an MCP server over Streamable HTTP, checks it and caches its tools. A failed check still saves it.");
        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateMcpServer")
            .WithSummary("Changes an MCP server. Without a secret the saved one stays; an empty secret removes it.");
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteMcpServer")
            .WithSummary("Deletes an MCP server; agents lose its tools.");
        group.MapPost("/{id:guid}/test", TestAsync)
            .WithName("TestMcpServer")
            .WithSummary("Connects, lists the tools, refreshes the cached tools and reports.");
        group.MapPost("/test", async (SaveMcpServerRequest request, IMcpServerService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.TestDraftAsync(request, cancellationToken)))
            .WithName("TestMcpServerDraft")
            .WithSummary("Tests settings before they are saved.");

        return group;
    }

    private static async Task<Results<Ok<McpServerDto>, ProblemHttpResult>> GetAsync(Guid id, IMcpServerService service, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken) is { } server ? TypedResults.Ok(server) : NotFound(id);

    private static async Task<Created<McpServerDto>> CreateAsync(SaveMcpServerRequest request, IMcpServerService service, CancellationToken cancellationToken)
    {
        var server = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/connections/mcp/{server.Id}", server);
    }

    private static async Task<Results<Ok<McpServerDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SaveMcpServerRequest request,
        IMcpServerService service,
        CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken) is { } server ? TypedResults.Ok(server) : NotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, IMcpServerService service, CancellationToken cancellationToken) =>
        await service.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : NotFound(id);

    private static async Task<Results<Ok<McpTestResultDto>, ProblemHttpResult>> TestAsync(Guid id, IMcpServerService service, CancellationToken cancellationToken) =>
        await service.TestAsync(id, cancellationToken) is { } result ? TypedResults.Ok(result) : NotFound(id);

    private static ProblemHttpResult NotFound(Guid id) => ApiResults.NotFound($"MCP server '{id}' was not found.");
}
