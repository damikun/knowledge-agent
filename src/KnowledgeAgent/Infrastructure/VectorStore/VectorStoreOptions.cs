namespace KnowledgeAgent.Infrastructure.VectorStore;

public sealed class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    public string Provider { get; set; } = "ClickHouse";
    public string ConnectionStringName { get; set; } = "clickhouse";
    public string Database { get; set; } = "knowledge";
    public string Table { get; set; } = "chunks";
    public bool EnsureSchemaOnStartup { get; set; } = true;
}
