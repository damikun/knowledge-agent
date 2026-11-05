namespace KnowledgeAgent.Infrastructure.Security;

public enum AuthMode
{
    Oidc,

    // Every request is a local admin. Only for development and tests, where there is no identity provider.
    None,
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Oidc;

    // The issuer as the browser sees it, e.g. http://localhost:8180/realms/knowledge-agent.
    public string? Authority { get; set; }

    // For when the API reaches the provider at another address than the browser, as in Docker Compose.
    public string? MetadataAddress { get; set; }

    public string ClientId { get; set; } = "knowledge-agent-portal";

    public string? ClientSecret { get; set; }

    // The browser's origin for the portal, so the callbacks work behind the Vite proxy and nginx. Empty means the request's origin.
    public string? PublicOrigin { get; set; }

    public bool RequireHttpsMetadata { get; set; }

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    // How often a session is re-checked with the provider, so disabled accounts and role changes apply quickly.
    public TimeSpan SessionCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}
