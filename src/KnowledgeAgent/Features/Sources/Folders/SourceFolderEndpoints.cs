using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Sources.Folders;

internal static class SourceFolderEndpoints
{
    public static RouteGroupBuilder MapSourceFolderEndpoints(this RouteGroupBuilder sources)
    {
        var group = sources.MapGroup("/folders");

        group.MapGet("/", ListAsync)
            .WithName("ListSourceFolders")
            .WithSummary("Every folder with the number of sources directly in it, plus the unfiled and total source counts.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSourceFolder")
            .WithSummary("Get one folder.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSourceFolder")
            .WithSummary("Create a folder at the top level or, with parentId, inside another folder.")
            .WithDescription(
                $"name: at most {SourceFolder.MaxNameLength} characters and unique among its siblings, ignoring case. "
                + $"Folders nest at most {SourceFolder.MaxDepth} levels deep.")
            .ProducesValidationProblem();

        group.MapPatch("/{id:guid}", RenameAsync)
            .WithName("RenameSourceFolder")
            .WithSummary("Rename a folder. The Internal folder cannot be renamed.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/move", MoveAsync)
            .WithName("MoveSourceFolder")
            .WithSummary("Move a folder with its subfolders and sources into another folder, or to the top level when parentId is null. The Internal folder cannot be moved.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSourceFolder")
            .WithSummary("Delete a folder and its subfolders. Their sources stay in the library, unfiled. The Internal folder cannot be deleted.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        sources.MapPost("/move", MoveSourcesAsync)
            .WithName("MoveSources")
            .WithSummary("Move sources into a folder, or out of every folder when folderId is null.")
            .WithDescription(
                $"sourceIds: 1-{SourceFolderService.MaxSourcesPerMove} source ids; unknown ids are skipped and not counted. "
                + "Or matching instead of sourceIds: the query, type, status, folder and site filters of GET /api/sources, "
                + "which moves every source they match in one step.")
            .ProducesValidationProblem();

        return group;
    }

    private static async Task<Ok<SourceFolderListDto>> ListAsync(ISourceFolderService folders, CancellationToken cancellationToken)
    {
        var listing = await folders.ListAsync(cancellationToken);
        var counts = listing.Counts;
        return TypedResults.Ok(new SourceFolderListDto(
            listing.Folders.Select(folder => ToDto(folder, counts)).ToList(),
            counts.Unfiled,
            counts.Total));
    }

    private static async Task<Results<Ok<SourceFolderDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        ISourceFolderService folders,
        CancellationToken cancellationToken)
    {
        var folder = await folders.GetAsync(id, cancellationToken);
        return folder is null ? FolderNotFound(id) : TypedResults.Ok(await ToDtoAsync(folder, folders, cancellationToken));
    }

    private static async Task<Created<SourceFolderDto>> CreateAsync(
        CreateSourceFolderRequest request,
        ISourceFolderService folders,
        CancellationToken cancellationToken)
    {
        var folder = await folders.CreateAsync(request.Name, request.ParentId, cancellationToken);
        return TypedResults.Created($"/api/sources/folders/{folder.Id}", ToDto(folder, sourceCount: 0));
    }

    private static async Task<Results<Ok<SourceFolderDto>, ProblemHttpResult>> RenameAsync(
        Guid id,
        RenameSourceFolderRequest request,
        ISourceFolderService folders,
        CancellationToken cancellationToken)
    {
        var folder = await folders.RenameAsync(id, request.Name, cancellationToken);
        return folder is null ? FolderNotFound(id) : TypedResults.Ok(await ToDtoAsync(folder, folders, cancellationToken));
    }

    private static async Task<Results<Ok<SourceFolderDto>, ProblemHttpResult>> MoveAsync(
        Guid id,
        MoveSourceFolderRequest request,
        ISourceFolderService folders,
        CancellationToken cancellationToken)
    {
        var folder = await folders.MoveAsync(id, request.ParentId, cancellationToken);
        return folder is null ? FolderNotFound(id) : TypedResults.Ok(await ToDtoAsync(folder, folders, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ISourceFolderService folders,
        CancellationToken cancellationToken) =>
        await folders.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : FolderNotFound(id);

    private static async Task<Results<Ok<MoveSourcesResponse>, ValidationProblem>> MoveSourcesAsync(
        MoveSourcesRequest request,
        ISourceFolderService folders,
        CancellationToken cancellationToken)
    {
        if (request.Matching is not { } matching)
        {
            var moved = await folders.MoveSourcesAsync(request.SourceIds ?? [], request.FolderId, cancellationToken);
            return TypedResults.Ok(new MoveSourcesResponse(moved));
        }

        if (request.SourceIds is { Count: > 0 })
        {
            return ApiResults.Validation("matching", "Send either sourceIds or matching, not both.");
        }

        var errors = new ValidationErrors();
        var query = SourceEndpoints.ParseFilters(matching.Query, matching.Type, [matching.Status], matching.Folder, matching.Site, errors, "matching.")
            with { Parent = ParentFilter.TopLevel };
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var matched = await folders.MoveMatchingSourcesAsync(query, request.FolderId, cancellationToken);
        return TypedResults.Ok(new MoveSourcesResponse(matched));
    }

    private static async Task<SourceFolderDto> ToDtoAsync(SourceFolder folder, ISourceFolderService folders, CancellationToken cancellationToken) =>
        ToDto(folder, await folders.CountSourcesAsync(folder.Id, cancellationToken));

    private static SourceFolderDto ToDto(SourceFolder folder, SourceFolderCounts counts) =>
        ToDto(folder, counts.ByFolder.GetValueOrDefault(folder.Id));

    private static SourceFolderDto ToDto(SourceFolder folder, int sourceCount) =>
        new(folder.Id, folder.Name, folder.ParentId, sourceCount, folder.CreatedAt, folder.UpdatedAt) { IsInternal = folder.IsInternal };

    private static ProblemHttpResult FolderNotFound(Guid id) => ApiResults.NotFound($"Folder '{id}' was not found.");
}
