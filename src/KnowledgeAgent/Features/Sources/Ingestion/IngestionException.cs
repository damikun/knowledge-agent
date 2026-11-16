namespace KnowledgeAgent.Features.Sources.Ingestion;

internal sealed class IngestionException(string message, bool isPermanent = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsPermanent { get; } = isPermanent;

    public int? StatusCode { get; init; }
}
