namespace KnowledgeAgent.Infrastructure.Security;

public enum AuthMode
{
    Oidc,

    None,
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Oidc;

    public string? Authority { get; set; }

    public string? MetadataAddress { get; set; }

    public string ClientId { get; set; } = "knowledge-agent-portal";

    public string? ClientSecret { get; set; }

    public string? PublicOrigin { get; set; }

    public bool RequireHttpsMetadata { get; set; }

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan SessionCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}
