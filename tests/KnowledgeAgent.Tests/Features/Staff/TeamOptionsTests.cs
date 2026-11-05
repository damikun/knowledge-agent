using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Tests.Features.Staff;

public sealed class TeamOptionsTests
{
    [Theory]
    [InlineData("http://localhost:8180/realms/knowledge-agent", "http://localhost:8180/admin/knowledge-agent/console/")]
    [InlineData("https://login.example.com/auth/realms/acme/", "https://login.example.com/auth/admin/acme/console/")]
    [InlineData("https://login.example.com/oidc", null)]
    [InlineData("not a url", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void The_console_link_is_derived_from_the_realm_url(string? authority, string? expected) =>
        TeamOptions.ResolveConsoleUrl(new TeamOptions(), new AuthOptions { Mode = AuthMode.Oidc, Authority = authority }).ShouldBe(expected);

    [Fact]
    public void A_configured_link_wins_but_only_over_http_and_never_without_sign_in()
    {
        var team = new TeamOptions { KeycloakConsoleUrl = "https://kc.example.com/admin/acme/console/" };
        TeamOptions.ResolveConsoleUrl(team, new AuthOptions { Mode = AuthMode.Oidc, Authority = "http://localhost:8180/realms/x" })
            .ShouldBe("https://kc.example.com/admin/acme/console/");
        TeamOptions.ResolveConsoleUrl(new TeamOptions { KeycloakConsoleUrl = "ftp://kc.example.com/" }, new AuthOptions { Mode = AuthMode.Oidc }).ShouldBeNull();
        TeamOptions.ResolveConsoleUrl(team, new AuthOptions { Mode = AuthMode.None }).ShouldBeNull();
    }
}
