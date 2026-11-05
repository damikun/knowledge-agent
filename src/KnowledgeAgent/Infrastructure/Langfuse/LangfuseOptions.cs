namespace KnowledgeAgent.Infrastructure.Langfuse;

public sealed class LangfuseOptions
{
    public const string SectionName = "Langfuse";

    public string? BaseUrl { get; set; }
    public string? PublicUrl { get; set; }
    public string? PublicKey { get; set; }
    public string? SecretKey { get; set; }
    public string? ProjectId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
