using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Labels;

internal static class LabelEndpoints
{
    public const int DefaultRelabelDays = 30;

    public static IEndpointRouteBuilder MapLabelEndpoints(this IEndpointRouteBuilder routes)
    {
        // Staff read labels and put them on conversations; admins define them.
        var labels = routes.MapGroup("/labels").WithTags("Labels").RequireAdminForChanges();

        labels.MapGet("/", (ILabelService service, CancellationToken cancellationToken) => service.ListAsync(cancellationToken))
            .WithName("ListLabels")
            .WithSummary("Every label by name, with the number of conversations that show it.");

        labels.MapPost("/", CreateAsync)
            .WithName("CreateLabel")
            .WithSummary("Creates a label. Colors: gray, blue, green, amber, red, purple, teal, pink. The description tells the auto-labeling what the label means.")
            .ProducesValidationProblem();

        labels.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateLabel")
            .WithSummary("Changes a label; fields left out stay as they are.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        labels.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteLabel")
            .WithSummary("Deletes a label and takes it off every conversation.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        labels.MapPost("/relabel", RelabelAsync)
            .WithName("RelabelConversations")
            .WithSummary($"Queues the auto-labeling again for the conversations updated in the last days (30 by default, at most 90), at most {LabelService.MaxRelabel}, most recent first.")
            .ProducesValidationProblem();

        routes.MapPut("/conversations/{id:guid}/labels", SetConversationLabelsAsync)
            .WithTags("Conversations")
            .WithName("SetConversationLabels")
            .WithSummary("Sets every label a conversation shows. New ones are manual, auto ones kept stay auto, and an auto label left out is never applied to it again.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<Created<LabelDto>> CreateAsync(CreateLabelRequest request, ILabelService service, CancellationToken cancellationToken)
    {
        var label = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/labels/{label.Id}", label);
    }

    private static async Task<Results<Ok<LabelDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateLabelRequest request,
        ILabelService service,
        CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken) is { } label ? TypedResults.Ok(label) : LabelNotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ILabelService service, CancellationToken cancellationToken) =>
        await service.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : LabelNotFound(id);

    private static async Task<Accepted<RelabelResponse>> RelabelAsync(int? days, ILabelService service, CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, new RelabelResponse(await service.RelabelAsync(days ?? DefaultRelabelDays, cancellationToken)));

    private static async Task<Results<Ok<IReadOnlyList<ConversationLabelDto>>, ProblemHttpResult>> SetConversationLabelsAsync(
        Guid id,
        SetConversationLabelsRequest request,
        ILabelService service,
        CancellationToken cancellationToken) =>
        await service.SetConversationLabelsAsync(id, request.LabelIds, cancellationToken) is { } labels
            ? TypedResults.Ok(labels)
            : ApiResults.NotFound($"Conversation '{id}' was not found.");

    private static ProblemHttpResult LabelNotFound(Guid id) => ApiResults.NotFound($"Label '{id}' was not found.");
}
