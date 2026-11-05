using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.Web;

internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    // Non-standard status for requests the client aborted, following the nginx convention.
    internal const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, httpContext.Request.Method, httpContext.Request.Path);
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = ClientClosedRequest;
            }

            return true;
        }

        var problem = CreateProblem(exception);
        Log(httpContext, exception, problem.Status ?? StatusCodes.Status500InternalServerError);

        if (httpContext.Response.HasStarted)
        {
            // Too late for a problem response once streaming has started, so the server aborts the request.
            return false;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }))
        {
            return true;
        }

        // The writers follow the Accept header; a chat stream accepts only text/event-stream and still gets the problem.
        await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        return true;
    }

    internal static ProblemDetails CreateProblem(Exception exception) => exception switch
    {
        ValidationFailedException validation => new HttpValidationProblemDetails(
            validation.Errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Detail = validation.Message,
        },
        ModelNotConfiguredException => new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Model not configured",
            Detail = exception.Message,
        },
        PromptStoreUnavailableException => new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Prompt store unavailable",
            Detail = exception.Message,
        },
        ConversationBusyException => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The previous answer is still being written",
            Detail = exception.Message,
            Extensions = { ["code"] = ChatErrorCodes.ConversationBusy },
        },
        ConversationClosedException => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The conversation is closed",
            Detail = "This conversation was closed. Start a new one.",
            Extensions = { ["code"] = ChatErrorCodes.ConversationClosed },
        },
        PersonJoinedException => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Someone already joined",
            Detail = "Someone from the team has joined this chat, so it stays with them.",
            Extensions = { ["code"] = ChatErrorCodes.PersonJoined },
        },
        ConversationNotFoundException => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not Found",
            Detail = exception.Message,
        },
        PromptNotFoundException => new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Prompt not found",
            Detail = exception.Message,
        },
        PromptRenderException => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Template could not be rendered",
            Detail = exception.Message,
        },
        BadHttpRequestException badRequest => new ProblemDetails
        {
            Status = badRequest.StatusCode,
            Title = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request body too large" : "Bad request",
            Detail = badRequest.Message,
        },
        // No detail, so internal error messages never reach the caller.
        _ => new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
        },
    };

    private void Log(HttpContext httpContext, Exception exception, int status)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;
        if (exception is ModelNotConfiguredException or PromptStoreUnavailableException)
        {
            LogDependencyUnavailable(logger, method, path, exception.Message);
        }
        else if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, method, path);
        }
        else
        {
            LogClientError(logger, method, path, status, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} answered 503: {Reason}")]
    private static partial void LogDependencyUnavailable(ILogger logger, string method, PathString path, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Method} {Path} rejected with {Status}: {Reason}")]
    private static partial void LogClientError(ILogger logger, string method, PathString path, int status, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Method} {Path} was aborted by the client")]
    private static partial void LogRequestAborted(ILogger logger, string method, PathString path);
}
