using System.Diagnostics;
using System.Diagnostics.Metrics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Sources.Ingestion;

internal static class IngestionTelemetry
{
    public static readonly ActivitySource ActivitySource = new(KnowledgeAgentTelemetry.Ingestion);

    private static readonly Meter Meter = new(KnowledgeAgentTelemetry.Ingestion);

    public static readonly Counter<long> SourcesProcessed = Meter.CreateCounter<long>(
        "knowledge.ingestion.sources", unit: "{source}", description: "Ingestion attempts by outcome (ready, retry, failed).");

    public static readonly Counter<long> ChunksStored = Meter.CreateCounter<long>(
        "knowledge.ingestion.chunks", unit: "{chunk}", description: "Chunks embedded and stored.");

    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "knowledge.ingestion.duration", unit: "s", description: "Duration of one ingestion attempt.");

    public static class Tags
    {
        public const string SourceId = "knowledge.source.id";
        public const string SourceType = "knowledge.source.type";
        public const string Attempt = "knowledge.ingestion.attempt";
        public const string Outcome = "knowledge.ingestion.outcome";
        public const string ContentKind = "knowledge.content.kind";
        public const string ChunkCount = "knowledge.chunk.count";
        public const string PageCount = "knowledge.page.count";
        public const string EmbeddingModel = "knowledge.embedding.model";
    }
}
