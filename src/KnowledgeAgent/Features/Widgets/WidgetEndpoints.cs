using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Widgets;

internal static class WidgetEndpoints
{
    public static RouteGroupBuilder MapWidgetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/widgets").WithTags("Widgets");

        group.MapGet("/", ListAsync)
            .WithName("ListWidgets");
        group.MapGet("/options", (IWidgetService widgets) => TypedResults.Ok(widgets.GetOptions()))
            .WithName("GetWidgetOptions")
            .WithSummary("The choices of every widget setting, and the defaults of a new widget.");
        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetWidget");
        group.MapPost("/", CreateAsync)
            .WithName("CreateWidget")
            .WithSummary("Creates a widget for one agent; the response holds its public key for the embed snippet.");
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateWidget");
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteWidget")
            .WithSummary("Deletes a widget with its images and visitors. Its conversations stay until retention removes them.");
        group.MapPost("/{id:guid}/identity-secret", CreateIdentitySecretAsync)
            .WithName("CreateWidgetIdentitySecret")
            .WithSummary("Turns identity verification on, or rotates its secret. The secret is in this response only; the old one stops working at once.");
        group.MapDelete("/{id:guid}/identity-secret", RemoveIdentitySecretAsync)
            .WithName("RemoveWidgetIdentitySecret")
            .WithSummary("Turns identity verification off; identify calls are then ignored and anyone may chat.");
        group.MapPost("/{id:guid}/preview-session", StartPreviewSessionAsync)
            .WithName("StartWidgetPreviewSession")
            .WithSummary("A visitor session for trying the real widget in the portal, also while it is turned off. The token lasts an hour and only "
                + "works with an admin's session; its conversations are tests that count nowhere, and asking for a person asks nobody.");
        group.MapPost("/{id:guid}/assets", UploadAssetAsync)
            .WithName("UploadWidgetAsset")
            .WithSummary("Uploads a PNG, JPEG or WebP image of up to 512 KB for the avatar or launcher icon.")
            .DisableAntiforgery();

        return group;
    }

    private static async Task<Ok<IReadOnlyList<WidgetDto>>> ListAsync(IWidgetService widgets, CancellationToken cancellationToken) =>
        TypedResults.Ok(await widgets.ListAsync(cancellationToken));

    private static async Task<Results<Ok<WidgetDto>, ProblemHttpResult>> GetAsync(Guid id, IWidgetService widgets, CancellationToken cancellationToken) =>
        await widgets.GetAsync(id, cancellationToken) is { } widget ? TypedResults.Ok(widget) : NotFound(id);

    private static async Task<Created<WidgetDto>> CreateAsync(SaveWidgetRequest request, IWidgetService widgets, CancellationToken cancellationToken)
    {
        var widget = await widgets.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/widgets/{widget.Id}", widget);
    }

    private static async Task<Results<Ok<WidgetDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SaveWidgetRequest request,
        IWidgetService widgets,
        CancellationToken cancellationToken) =>
        await widgets.UpdateAsync(id, request, cancellationToken) is { } widget ? TypedResults.Ok(widget) : NotFound(id);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, IWidgetService widgets, CancellationToken cancellationToken) =>
        await widgets.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : NotFound(id);

    private static async Task<Results<Ok<WidgetIdentitySecretDto>, ProblemHttpResult>> CreateIdentitySecretAsync(
        Guid id,
        HttpResponse response,
        IWidgetService widgets,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        return await widgets.CreateIdentitySecretAsync(id, cancellationToken) is { } secret ? TypedResults.Ok(secret) : NotFound(id);
    }

    private static async Task<Results<Ok<WidgetDto>, ProblemHttpResult>> RemoveIdentitySecretAsync(Guid id, IWidgetService widgets, CancellationToken cancellationToken) =>
        await widgets.RemoveIdentitySecretAsync(id, cancellationToken) is { } widget ? TypedResults.Ok(widget) : NotFound(id);

    private static async Task<Results<Ok<WidgetPreviewSessionDto>, ProblemHttpResult>> StartPreviewSessionAsync(
        Guid id,
        HttpResponse response,
        IWidgetRepository widgets,
        VisitorService visitors,
        LinkHosts links,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        if (await widgets.GetAsync(id, cancellationToken) is not { } widget)
        {
            return NotFound(id);
        }

        var session = await visitors.StartPreviewSessionAsync(widget, cancellationToken);
        return TypedResults.Ok(session with { Config = session.Config with { LinkHosts = await links.ForWidgetAsync(widget, cancellationToken) } });
    }

    private static async Task<Results<Created<WidgetAssetDto>, ProblemHttpResult, ValidationProblem>> UploadAssetAsync(
        Guid id,
        IFormFile? file,
        IWidgetService widgets,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ApiResults.Validation("file", "Choose an image to upload.");
        }

        if (file.Length > WidgetAsset.MaxBytes)
        {
            return ApiResults.Validation("file", $"The image must be at most {WidgetAsset.MaxBytes / 1024} KB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var asset = await widgets.AddAssetAsync(id, buffer.ToArray(), cancellationToken);
        return asset is null ? NotFound(id) : TypedResults.Created(asset.Url, asset);
    }

    private static ProblemHttpResult NotFound(Guid id) => ApiResults.NotFound($"Widget '{id}' was not found.");
}
