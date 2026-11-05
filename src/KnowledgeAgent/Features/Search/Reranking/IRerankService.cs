namespace KnowledgeAgent.Features.Search.Reranking;

public sealed record RerankResult(string Model, IReadOnlyList<double> Scores);

public interface IRerankService
{
    Task<string?> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Scores each document from 0 to 1 in input order, or returns null when no reranker is configured.</summary>
    Task<RerankResult?> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default);
}
