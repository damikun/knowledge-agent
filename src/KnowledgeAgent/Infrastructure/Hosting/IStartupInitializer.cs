namespace KnowledgeAgent.Infrastructure.Hosting;

public interface IStartupInitializer
{
    int Order { get; }
    string Name { get; }

    /// <summary>When false, a failure is logged and startup continues.</summary>
    bool IsCritical => true;

    Task InitializeAsync(CancellationToken cancellationToken);
}

public static class StartupOrder
{
    public const int Persistence = 0;
    public const int VectorStore = 10;
    public const int Prompts = 20;
    public const int Agents = 25;
    public const int Labels = 27;
    public const int Ingestion = 30;
}
