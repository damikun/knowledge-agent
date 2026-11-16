namespace KnowledgeAgent.Features.Sources.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public int MaxParallelism { get; set; } = 2;
    public int ChunkSizeTokens { get; set; } = 500;
    public int ChunkOverlapTokens { get; set; } = 60;
    public int EmbeddingBatchSize { get; set; } = 64;
    public int MaxAttempts { get; set; } = 3;
    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;
    public long MaxDownloadBytes { get; set; } = 20 * 1024 * 1024;
    public int FetchTimeoutSeconds { get; set; } = 30;

    public bool AllowPrivateNetworks { get; set; }

    public string UserAgent { get; set; } = "KnowledgeAgent/1.0 (+https://github.com/knowledge-agent)";

    public int MaxCrawlPages { get; set; } = 500;

    public double CrawlRequestsPerSecond { get; set; } = 1;

    public ContextualizeMode Contextualize { get; set; } = ContextualizeMode.Auto;

    public int MaxModelContextChunks { get; set; } = 300;
    public int ModelContextParallelism { get; set; } = 4;
}

public enum ContextualizeMode
{
    Auto,

    Headings,

    Model,

    Off,
}
