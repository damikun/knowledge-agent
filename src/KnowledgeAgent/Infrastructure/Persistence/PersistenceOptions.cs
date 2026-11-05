namespace KnowledgeAgent.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "Postgres";
    public string ConnectionStringName { get; set; } = "knowledgedb";
    public bool MigrateOnStartup { get; set; } = true;
    public string InMemoryDatabaseName { get; set; } = "knowledge-agent";
}
