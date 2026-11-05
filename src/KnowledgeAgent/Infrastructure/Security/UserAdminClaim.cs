using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;

namespace KnowledgeAgent.Infrastructure.Security;

// Whether the user may manage users in Keycloak's admin console. Runs at sign-in and at every session check.
internal sealed class UserAdminClaim() : ClaimAction(Name, ClaimValueTypes.Boolean)
{
    public const string Name = "ka_user_admin";

    // The claim name of the mapper in docker/keycloak/knowledge-agent-realm.json.
    public const string KeycloakClaim = "realm_management_roles";

    // view-users opens the console's user list; realm-admin includes both.
    private static readonly HashSet<string> UserAdminRoles = new(StringComparer.Ordinal) { "manage-users", "view-users", "realm-admin" };

    public static bool IsUserAdmin(ClaimsPrincipal user) =>
        user.HasClaim(claim => claim.Type == Name && claim.Value == "true");

    public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
    {
        var roles = identity.FindAll(KeycloakClaim).ToList();
        var userAdmin = roles.Any(claim => UserAdminRoles.Contains(claim.Value));
        foreach (var claim in roles)
        {
            identity.TryRemoveClaim(claim);
        }

        foreach (var stale in identity.FindAll(Name).ToList())
        {
            identity.TryRemoveClaim(stale);
        }

        if (userAdmin)
        {
            identity.AddClaim(new Claim(Name, "true", ValueType, issuer));
        }
    }
}
