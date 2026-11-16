using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;

namespace KnowledgeAgent.Infrastructure.Security;

internal sealed class UserAdminClaim() : ClaimAction(Name, ClaimValueTypes.Boolean)
{
    public const string Name = "ka_user_admin";

    public const string KeycloakClaim = "realm_management_roles";

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
