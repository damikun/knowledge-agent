using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources.Ingestion;

// Rejects only nonsensical values. Values that are merely out of range are clamped where they are used.
internal sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    // Leaves room under Inngest's limit of 1,000 steps per run for the discovery and finishing steps.
    public const int MaxCrawlPagesLimit = 990;

    public const double MaxCrawlRequestsPerSecond = 2;

    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        var failures = new List<string>();
        Require(options.MaxParallelism >= 1, "Ingestion:MaxParallelism must be at least 1.");
        Require(options.ChunkSizeTokens >= 1, "Ingestion:ChunkSizeTokens must be at least 1.");
        Require(options.ChunkOverlapTokens >= 0, "Ingestion:ChunkOverlapTokens must not be negative.");
        Require(options.EmbeddingBatchSize >= 1, "Ingestion:EmbeddingBatchSize must be at least 1.");
        Require(options.MaxAttempts >= 1, "Ingestion:MaxAttempts must be at least 1.");
        Require(options.MaxUploadBytes >= 1, "Ingestion:MaxUploadBytes must be positive.");
        Require(options.MaxDownloadBytes >= 1, "Ingestion:MaxDownloadBytes must be positive.");
        Require(options.FetchTimeoutSeconds >= 1, "Ingestion:FetchTimeoutSeconds must be at least 1.");
        Require(!string.IsNullOrWhiteSpace(options.UserAgent), "Ingestion:UserAgent is required.");
        Require(options.MaxCrawlPages is >= 1 and <= MaxCrawlPagesLimit, $"Ingestion:MaxCrawlPages must be between 1 and {MaxCrawlPagesLimit}.");
        Require(options.CrawlRequestsPerSecond is > 0 and <= MaxCrawlRequestsPerSecond, $"Ingestion:CrawlRequestsPerSecond must be above 0 and at most {MaxCrawlRequestsPerSecond}.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Require(bool condition, string failure)
        {
            if (!condition)
            {
                failures.Add(failure);
            }
        }
    }
}
