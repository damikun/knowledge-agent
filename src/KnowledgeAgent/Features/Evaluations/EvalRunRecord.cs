namespace KnowledgeAgent.Features.Evaluations;

public sealed class EvalRunRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public string DatasetSource { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Judge { get; set; }
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public int FailedItems { get; set; }
    public string? ChatModel { get; set; }
    public string? EmbeddingModel { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string? ConfigJson { get; set; }
    public string? MetricsJson { get; set; }
    public string? ByTagJson { get; set; }
    public string? GateJson { get; set; }
    public string? ItemsJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? Error { get; set; }
    public string? LangfuseExperimentId { get; set; }
    public string? LangfuseUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
