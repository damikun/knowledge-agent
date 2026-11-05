using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Features.Staff;

public sealed class TeamOptions
{
    public const string SectionName = "Team";

    // The Keycloak admin console of the realm. Empty: derived from Auth:Authority (…/realms/{realm} becomes
    // …/admin/{realm}/console/), and none without sign-in.
    public string? KeycloakConsoleUrl { get; set; }

    internal static string? ResolveConsoleUrl(TeamOptions team, AuthOptions auth)
    {
        if (auth.Mode != AuthMode.Oidc)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(team.KeycloakConsoleUrl))
        {
            return Uri.TryCreate(team.KeycloakConsoleUrl.Trim(), UriKind.Absolute, out var configured) && IsHttp(configured)
                ? configured.ToString()
                : null;
        }

        if (string.IsNullOrWhiteSpace(auth.Authority)
            || !Uri.TryCreate(auth.Authority.Trim().TrimEnd('/'), UriKind.Absolute, out var authority)
            || !IsHttp(authority))
        {
            return null;
        }

        var path = authority.AbsolutePath;
        var marker = path.LastIndexOf("/realms/", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var realm = path[(marker + "/realms/".Length)..];
        if (realm.Length == 0 || realm.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var builder = new UriBuilder(authority) { Path = $"{path[..marker]}/admin/{realm}/console/", Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }

    private static bool IsHttp(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
