using System.Security.Claims;
using System.Text.Json;
using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Tests.Infrastructure.Security;

public sealed class UserAdminClaimTests
{
    [Theory]
    [InlineData("manage-users")]
    [InlineData("view-users")]
    [InlineData("realm-admin")]
    public void A_user_admin_role_of_realm_management_becomes_the_claim(string role)
    {
        var user = Run(new Claim(UserAdminClaim.KeycloakClaim, "view-realm"), new Claim(UserAdminClaim.KeycloakClaim, role));

        UserAdminClaim.IsUserAdmin(user).ShouldBeTrue();
        user.FindAll(UserAdminClaim.KeycloakClaim).ShouldBeEmpty("only the answer is kept in the session");
        user.FindAll(UserAdminClaim.Name).ShouldHaveSingleItem().Value.ShouldBe("true");
    }

    [Fact]
    public void Other_roles_or_none_give_no_claim_and_a_role_taken_away_removes_it()
    {
        UserAdminClaim.IsUserAdmin(Run(new Claim(UserAdminClaim.KeycloakClaim, "view-realm"), new Claim("roles", "admin"))).ShouldBeFalse();
        UserAdminClaim.IsUserAdmin(Run(new Claim("roles", "admin"))).ShouldBeFalse("an admin of the app is not a Keycloak user admin");

        // A session check starts from the new ID token; a claim left over from before must not survive.
        UserAdminClaim.IsUserAdmin(Run(new Claim(UserAdminClaim.Name, "true"))).ShouldBeFalse();
    }

    private static ClaimsPrincipal Run(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "oidc", "name", "roles");
        using var empty = JsonDocument.Parse("{}");
        new UserAdminClaim().Run(empty.RootElement, identity, "oidc");
        return new ClaimsPrincipal(identity);
    }
}
