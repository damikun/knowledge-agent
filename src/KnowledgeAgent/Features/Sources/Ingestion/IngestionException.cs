namespace KnowledgeAgent.Features.Sources.Ingestion;

internal sealed class IngestionException(string message, bool isPermanent = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsPermanent { get; } = isPermanent;

    // The HTTP status when the server answered with an error.
    public int? StatusCode { get; init; }
}
