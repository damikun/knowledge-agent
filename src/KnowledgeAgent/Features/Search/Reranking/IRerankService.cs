namespace KnowledgeAgent.Features.Search.Reranking;

public sealed record RerankResult(string Model, IReadOnlyList<double> Scores);

public interface IRerankService
{
    Task<string?> DescribeAsync(CancellationToken cancellationToken = default);

    Task<RerankResult?> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default);
}
