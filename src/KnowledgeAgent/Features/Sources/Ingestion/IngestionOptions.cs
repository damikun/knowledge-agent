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

    // Off by default: fetching private or loopback addresses exposes internal services to SSRF.
    public bool AllowPrivateNetworks { get; set; }

    // robots.txt groups are matched by the product token, "KnowledgeAgent".
    public string UserAgent { get; set; } = "KnowledgeAgent/1.0 (+https://github.com/knowledge-agent)";

    // The most pages a website may ask for. Each page is one step of the crawl job, and Inngest allows about a thousand
    // steps per run.
    public int MaxCrawlPages { get; set; } = 500;

    // Per host, across all crawls. A robots.txt Crawl-delay can only slow it down.
    public double CrawlRequestsPerSecond { get; set; } = 1;

    // Contextual retrieval: what is stored with each chunk to place it in its document.
    public ContextualizeMode Contextualize { get; set; } = ContextualizeMode.Auto;

    // Chunks of one source that get a model-written context; the rest get the title and section path.
    public int MaxModelContextChunks { get; set; } = 300;
    public int ModelContextParallelism { get; set; } = 4;
}

public enum ContextualizeMode
{
    // Title and section path always; a model-written context too when the contextualizer profile names a model.
    Auto,

    // Title and section path only.
    Headings,

    // Always ask the contextualizer profile's model.
    Model,

    // Nothing: chunks are embedded and searched as they are.
    Off,
}
