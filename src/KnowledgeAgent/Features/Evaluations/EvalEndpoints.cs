using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Evaluations;

internal static class EvalEndpoints
{
    public const int DefaultRunListLimit = 20;

    public static RouteGroupBuilder MapEvalEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/evals").WithTags("Evaluations");

        group.MapGet("/datasets", ListDatasetsAsync)
            .WithName("ListEvalDatasets")
            .WithSummary("Built-in golden sets, the team's own datasets and Langfuse datasets, plus whether Langfuse and the LLM judge are available.");

        group.MapPost("/datasets", CreateDatasetAsync)
            .WithName("CreateEvalDataset")
            .WithSummary("Create an editable dataset, empty or as a copy of another one (copyFrom, copyFromSource: builtin, langfuse or custom).")
            .ProducesValidationProblem();

        group.MapPatch("/datasets/{id:guid}", UpdateDatasetAsync)
            .WithName("UpdateEvalDataset")
            .WithSummary("Rename an editable dataset or change its description.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/datasets/{id:guid}", DeleteDatasetAsync)
            .WithName("DeleteEvalDataset")
            .WithSummary("Delete an editable dataset and its questions; its runs stay.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/datasets/{id:guid}/sync", SyncCustomDatasetAsync)
            .WithName("SyncCustomEvalDataset")
            .WithSummary("Create or update the Langfuse dataset of an editable dataset.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/datasets/{id:guid}/items", ListItemsAsync)
            .WithName("ListEvalDatasetItems")
            .WithSummary("The questions of an editable dataset, oldest first.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/datasets/{id:guid}/items", AddItemAsync)
            .WithName("AddEvalDatasetItem")
            .WithSummary("Add a question, typed in or taken from a conversation (conversationId, messageId), with the expected answer and facts.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/datasets/{id:guid}/items/batch", ImportItemsAsync)
            .WithName("ImportEvalDatasetItems")
            .WithSummary("Add up to 1000 questions at once (an upload), all or nothing; questions the dataset already has are skipped. Row errors come back as items[i].field.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/datasets/{id:guid}/items/delete", DeleteItemsAsync)
            .WithName("DeleteEvalDatasetItems")
            .WithSummary("Remove several questions from an editable dataset at once.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/datasets/{id:guid}/items/{itemId:guid}", UpdateItemAsync)
            .WithName("UpdateEvalDatasetItem")
            .WithSummary("Change a question of an editable dataset.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/datasets/{id:guid}/items/{itemId:guid}", DeleteItemAsync)
            .WithName("DeleteEvalDatasetItem")
            .WithSummary("Remove a question from an editable dataset.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/datasets/{name}/sync", SyncDatasetAsync)
            .WithName("SyncEvalDataset")
            .WithSummary("Create or update the Langfuse dataset of a builtin golden set.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs", ListRunsAsync)
            .WithName("ListEvalRuns")
            .WithSummary("Recent eval runs, newest first (limit 1-200, default 20; optional dataset filter).");

        group.MapPost("/runs", StartRunAsync)
            .WithName("StartEvalRun")
            .WithSummary("Queue an eval run; it executes in the background (poll GET /api/evals/runs/{id}).");

        group.MapGet("/runs/{id:guid}", GetRunAsync)
            .WithName("GetEvalRun")
            .WithSummary("One eval run with its configuration, per-tag metrics and item results.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/runs/{id:guid}/cancel", CancelRunAsync)
            .WithName("CancelEvalRun")
            .WithSummary("Cancel a queued or running eval run (finished runs are left as they are).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/runs/{id:guid}", DeleteRunAsync)
            .WithName("DeleteEvalRun")
            .WithSummary("Delete a finished eval run.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<Ok<EvalDatasetListDto>> ListDatasetsAsync(IEvalService evals, CancellationToken cancellationToken) =>
        TypedResults.Ok(await evals.ListDatasetsAsync(cancellationToken));

    private static async Task<Created<EvalDatasetDto>> CreateDatasetAsync(
        CreateEvalDatasetRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken)
    {
        var dataset = await datasets.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/evals/datasets/{dataset.Id}", dataset);
    }

    private static async Task<Results<Ok<EvalDatasetDto>, ProblemHttpResult>> UpdateDatasetAsync(
        Guid id,
        UpdateEvalDatasetRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.UpdateAsync(id, request, cancellationToken) is { } dataset ? TypedResults.Ok(dataset) : DatasetNotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDatasetAsync(Guid id, ICustomDatasetService datasets, CancellationToken cancellationToken) =>
        await datasets.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : DatasetNotFound(id);

    private static async Task<Results<Ok<EvalDatasetSyncResultDto>, ProblemHttpResult>> SyncCustomDatasetAsync(
        Guid id,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.SyncAsync(id, cancellationToken) is { } result ? TypedResults.Ok(result) : DatasetNotFound(id);

    private static async Task<Results<Ok<IReadOnlyList<EvalDatasetItemDto>>, ProblemHttpResult>> ListItemsAsync(
        Guid id,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.ListItemsAsync(id, cancellationToken) is { } items ? TypedResults.Ok(items) : DatasetNotFound(id);

    private static async Task<Results<Created<EvalDatasetItemDto>, ProblemHttpResult>> AddItemAsync(
        Guid id,
        SaveEvalItemRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.AddItemAsync(id, request, cancellationToken) is { } item
            ? TypedResults.Created($"/api/evals/datasets/{id}/items/{item.Id}", item)
            : DatasetNotFound(id);

    private static async Task<Results<Ok<EvalDatasetItemDto>, ProblemHttpResult>> UpdateItemAsync(
        Guid id,
        Guid itemId,
        SaveEvalItemRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.UpdateItemAsync(id, itemId, request, cancellationToken) is { } item
            ? TypedResults.Ok(item)
            : ApiResults.NotFound($"Question '{itemId}' of dataset '{id}' was not found.");

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteItemAsync(
        Guid id,
        Guid itemId,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.DeleteItemAsync(id, itemId, cancellationToken)
            ? TypedResults.NoContent()
            : ApiResults.NotFound($"Question '{itemId}' of dataset '{id}' was not found.");

    private static async Task<Results<Ok<ImportEvalItemsResultDto>, ProblemHttpResult>> ImportItemsAsync(
        Guid id,
        ImportEvalItemsRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.ImportItemsAsync(id, request, cancellationToken) is { } result ? TypedResults.Ok(result) : DatasetNotFound(id);

    private static async Task<Results<Ok<DeleteEvalItemsResultDto>, ProblemHttpResult>> DeleteItemsAsync(
        Guid id,
        DeleteEvalItemsRequest request,
        ICustomDatasetService datasets,
        CancellationToken cancellationToken) =>
        await datasets.DeleteItemsAsync(id, request, cancellationToken) is { } deleted
            ? TypedResults.Ok(new DeleteEvalItemsResultDto(deleted))
            : DatasetNotFound(id);

    private static ProblemHttpResult DatasetNotFound(Guid id) => ApiResults.NotFound($"Dataset '{id}' was not found.");

    private static async Task<Results<Ok<EvalDatasetSyncResultDto>, ProblemHttpResult>> SyncDatasetAsync(
        string name,
        IEvalService evals,
        CancellationToken cancellationToken) =>
        await evals.SyncDatasetAsync(name, cancellationToken) is { } result
            ? TypedResults.Ok(result)
            : ApiResults.NotFound($"There is no builtin dataset '{name}'.");

    private static async Task<Results<Ok<IReadOnlyList<EvalRunSummaryDto>>, ValidationProblem>> ListRunsAsync(
        int? limit,
        string? dataset,
        IEvalService evals,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > EvalService.MaxRunListLimit)
        {
            return ApiResults.Validation("limit", $"limit must be between 1 and {EvalService.MaxRunListLimit}.");
        }

        return TypedResults.Ok(await evals.ListRunsAsync(limit ?? DefaultRunListLimit, dataset, cancellationToken));
    }

    private static async Task<Accepted<EvalRunSummaryDto>> StartRunAsync(
        StartEvalRunRequest request,
        IEvalService evals,
        CancellationToken cancellationToken)
    {
        var run = await evals.StartRunAsync(request, cancellationToken);
        return TypedResults.Accepted($"/api/evals/runs/{run.Id}", run);
    }

    private static async Task<Results<Ok<EvalRunDto>, ProblemHttpResult>> GetRunAsync(Guid id, IEvalService evals, CancellationToken cancellationToken) =>
        await evals.GetRunAsync(id, cancellationToken) is { } run ? TypedResults.Ok(run) : RunNotFound(id);

    private static async Task<Results<Ok<EvalRunSummaryDto>, ProblemHttpResult>> CancelRunAsync(Guid id, IEvalService evals, CancellationToken cancellationToken) =>
        await evals.CancelRunAsync(id, cancellationToken) is { } run ? TypedResults.Ok(run) : RunNotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteRunAsync(Guid id, IEvalService evals, CancellationToken cancellationToken) =>
        await evals.DeleteRunAsync(id, cancellationToken) switch
        {
            EvalRunDeleteResult.Deleted => TypedResults.NoContent(),
            EvalRunDeleteResult.Active => TypedResults.Problem(
                detail: "The run is queued or running. Cancel it first.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict"),
            _ => RunNotFound(id),
        };

    private static ProblemHttpResult RunNotFound(Guid id) => ApiResults.NotFound($"Eval run '{id}' was not found.");
}
