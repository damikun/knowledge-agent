using KnowledgeAgent.Infrastructure.Web;

namespace KnowledgeAgent.Features.Sources.Ingestion;

internal static class ErrorSanitizer
{
    public const int MaxLength = 300;

    public static string ToUserMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception switch
        {
            IngestionException => exception.Message,
            TimeoutException => "The operation timed out.",
            OperationCanceledException => "The operation was cancelled or timed out.",
            _ => exception.Message,
        };

        var sanitized = Sanitize(message);
        return sanitized.Length > 0 ? sanitized : $"Ingestion failed ({exception.GetType().Name}).";
    }

    public static string Sanitize(string? message) => TextUtilities.Truncate(ErrorMessages.Redact(message), MaxLength, "…");
}
