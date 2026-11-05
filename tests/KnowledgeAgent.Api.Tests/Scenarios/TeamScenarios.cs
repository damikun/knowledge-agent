using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Runs as the local admin of Auth:Mode=None.
public abstract class TeamScenarios(AppFixture fixture)
{
    private const string Me = "local-admin";

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task The_team_lists_who_signed_in_with_when_they_were_added_and_no_console_without_sign_in()
    {
        await using var app = await fixture.CreateAppAsync();
        (await app.Client.GetJsonAsync<CurrentUserDto>("/api/auth/me")).Id.ShouldBe(Me);

        var team = await app.Client.GetJsonAsync<TeamDto>("/api/team");

        team.RoleChanges.ShouldBeFalse();
        team.KeycloakConsoleUrl.ShouldBeNull("Auth:Mode=None has no Keycloak");
        var me = team.Members.ShouldHaveSingleItem();
        me.ShouldSatisfyAllConditions(
            member => member.Id.ShouldBe(Me),
            member => member.Name.ShouldBe("Local admin"),
            member => member.Role.ShouldBe(AuthRoles.Admin),
            member => member.FirstSeenAt.ShouldNotBe(default, "added at the first sign-in"),
            member => member.LastSeenAt.ShouldBeGreaterThanOrEqualTo(member.FirstSeenAt));
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task With_sign_in_admins_get_the_realm_console_link()
    {
        await using var app = await fixture.CreateAppAsync(options =>
        {
            options.ConfigureServices = TestUsers.Install;
            options.Settings["Auth:Mode"] = "Oidc";
            options.Settings["Auth:Authority"] = "http://127.0.0.1:9/realms/knowledge-agent";
            options.Settings["Auth:ClientSecret"] = "test-client-secret";
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/team").As(AuthRoles.Admin);
        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
        (await response.ReadJsonAsync<TeamDto>()).KeycloakConsoleUrl.ShouldBe("http://127.0.0.1:9/admin/knowledge-agent/console/");
    }
}

public sealed class InMemoryTeamScenarios(InMemoryAppFixture fixture) : TeamScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersTeamScenarios(ContainersAppFixture fixture) : TeamScenarios(fixture), IClassFixture<ContainersAppFixture>;
