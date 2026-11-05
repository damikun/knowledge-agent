using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.SystemInfo;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// The other scenarios run as the local admin of Auth:Mode=None; these act as other users.
public sealed class AuthScenarios(InMemoryAppFixture fixture) : IClassFixture<InMemoryAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Without_a_session_the_api_answers_401_but_health_and_sign_in_stay_open()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);

        (await SendAsync(app, HttpMethod.Get, "/api/sources")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/api/system/health")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/health")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Sign-in is off in the tests, so login goes straight back to a local path and never to another site.
        using var client = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var login = await client.GetAsync("/api/auth/login?returnUrl=//evil.example/x", Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_can_chat_and_read_but_not_change_configuration()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);

        (await SendAsync(app, HttpMethod.Get, "/api/sources", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/models/profiles", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/conversations", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/conversations/search?q=refund", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/conversations/counts", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/conversations/{Guid.NewGuid()}/history", roles: AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may read a conversation's history; this one does not exist");
        (await SendAsync(app, HttpMethod.Get, "/api/labels", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Put, $"/api/conversations/{Guid.NewGuid()}/labels", new { labelIds = Array.Empty<Guid>() }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may label a conversation; this one does not exist");
        (await SendAsync(app, HttpMethod.Patch, $"/api/conversations/{Guid.NewGuid()}", new { pinned = true }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may rename and pin; this one does not exist");
        (await SendAsync(app, HttpMethod.Get, "/api/staff", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Post, $"/api/conversations/{Guid.NewGuid()}/assign", new { userId = (string?)null }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may assign; this one does not exist");
        (await SendAsync(app, HttpMethod.Post, "/api/labels", new { name = "Billing" }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Patch, $"/api/labels/{Guid.NewGuid()}", new { name = "Billing" }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Delete, $"/api/labels/{Guid.NewGuid()}", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, "/api/labels/relabel?days=30", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(app, HttpMethod.Post, "/api/sources/text", new AddTextSourceRequest("Notes", "Some text."), AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Put, "/api/models/profiles/utility", new { provider = "fake" }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/prompts", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/evals/datasets", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/jobs/overview", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/overview", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/system/health", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/analytics", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, "/api/evals/datasets", new { name = "Mine" }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, $"/api/evals/datasets/{Guid.NewGuid()}/items", new { question = "Why?" }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, $"/api/evals/datasets/{Guid.NewGuid()}/items/batch", new { items = new[] { new { question = "Why?" } } }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, $"/api/evals/datasets/{Guid.NewGuid()}/items/delete", new { ids = new[] { Guid.NewGuid() } }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A profile or prompt variables change what the agent is told, so a support user may not pick them.
        using var chat = await SendAsync(app, HttpMethod.Post, "/api/chat", new ChatRequestDto(null, "Hello", null, "utility", null), AuthRoles.Support);
        (await chat.ReadProblemAsync(HttpStatusCode.Forbidden)).Title.ShouldBe("Admin role required");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Only_admins_manage_mcp_connections_and_agent_api_keys()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        var agentId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var server = new { name = "Shop", url = "https://mcp.example.com/mcp" };

        (await SendAsync(app, HttpMethod.Get, "/api/connections/mcp")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, "/api/connections/mcp", null),
            (HttpMethod.Post, "/api/connections/mcp", server),
            (HttpMethod.Post, "/api/connections/mcp/test", server),
            (HttpMethod.Patch, $"/api/connections/mcp/{serverId}", server),
            (HttpMethod.Delete, $"/api/connections/mcp/{serverId}", null),
            (HttpMethod.Post, $"/api/connections/mcp/{serverId}/test", null),
            (HttpMethod.Get, $"/api/agents/{agentId}/api-keys", null),
            (HttpMethod.Post, $"/api/agents/{agentId}/api-keys", new { name = "Cursor" }),
            (HttpMethod.Delete, $"/api/agents/{agentId}/api-keys/{Guid.NewGuid()}", null),
            (HttpMethod.Get, $"/api/agents/{agentId}/access", null),
            (HttpMethod.Put, $"/api/agents/{agentId}/access", new { mcpServerEnabled = true }),
        })
        {
            (await SendAsync(app, method, url, body, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {url}");
        }

        (await SendAsync(app, HttpMethod.Get, "/api/connections/mcp", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/agents/{agentId}/api-keys", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(app, HttpMethod.Post, "/mcp/agents/internal", new { }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Post, "/api/public/agents/internal/ask", new { question = "Hi" }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Only_admins_see_the_team_and_edit_publish_or_roll_back_prompts()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        var name = $"e2e-auth-edit-{Guid.NewGuid():N}";
        app.Langfuse.SetTextPrompt(name, "Hello {{name}}.");
        var requests = new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, "/api/team", null),
            (HttpMethod.Get, $"/api/prompts/{name}/versions", null),
            (HttpMethod.Post, $"/api/prompts/{name}/versions", new { text = "Hi {{name}}." }),
            (HttpMethod.Post, $"/api/prompts/{name}/publish", new { version = 1 }),
        };

        foreach (var (method, url, body) in requests)
        {
            (await SendAsync(app, method, url, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {url}");
            (await SendAsync(app, method, url, body, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {url}");
        }

        app.Langfuse.Versions(name).ShouldBe([1], "nothing was saved for support");
        (await SendAsync(app, HttpMethod.Get, "/api/team", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/prompts/{name}/versions", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Post, $"/api/prompts/{name}/versions", new { text = "Hi {{name}}." }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(app, HttpMethod.Post, $"/api/prompts/{name}/publish", new { version = 2 }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        app.Langfuse.LabelsOf(name, 2).ShouldContain("production");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_reads_its_alerts_but_only_admins_snooze_them_or_change_where_they_go()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);

        (await SendAsync(app, HttpMethod.Get, "/api/alerts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var supportList = await SendAsync(app, HttpMethod.Get, "/api/alerts", roles: AuthRoles.Support);
        (await supportList.ReadJsonAsync<AlertListDto>()).Alerts.ShouldAllBe(alert => AlertKinds.Support.Contains(alert.Kind));
        (await SendAsync(app, HttpMethod.Post, "/api/alerts/visitors_waiting/snooze", new SnoozeAlertRequest(4), AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, "/api/alerts/visitors_waiting/dismiss", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Get, "/api/alerts/settings", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Put, "/api/alerts/settings", new SaveAlertSettingsRequest(true, false, null, null), AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(app, HttpMethod.Get, "/api/alerts", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Post, "/api/alerts/sources_failed/snooze", new SnoozeAlertRequest(4), AuthRoles.Admin))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "no source failed, so there is nothing to snooze");
        using var saved = await SendAsync(app, HttpMethod.Put, "/api/alerts/settings", new SaveAlertSettingsRequest(false, true, [AlertKinds.VisitorsWaiting], 10), AuthRoles.Admin);
        (await saved.ReadJsonAsync<AlertSettingsDto>()).Kinds.ShouldBe([AlertKinds.VisitorsWaiting]);
        using var read = await SendAsync(app, HttpMethod.Get, "/api/alerts/settings", roles: AuthRoles.Admin);
        (await read.ReadJsonAsync<AlertSettingsDto>()).WaitingMinutes.ShouldBe(10);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_finds_and_edits_people_but_only_admins_export_or_erase_them()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        var missing = Guid.NewGuid();

        (await SendAsync(app, HttpMethod.Get, "/api/people")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/api/people?q=ada", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/people/{missing}", roles: AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may read a person; this one does not exist");
        (await SendAsync(app, HttpMethod.Patch, $"/api/people/{missing}", new { notes = "VIP" }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "support may edit a person; this one does not exist");
        (await SendAsync(app, HttpMethod.Get, $"/api/conversations/search?contactId={missing}&createdBy=me", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/people/{missing}/export", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Delete, $"/api/people/{missing}", new { confirm = true }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Post, $"/api/widgets/{missing}/identity-secret", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(app, HttpMethod.Get, $"/api/people/{missing}/export", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Delete, $"/api/people/{missing}", new { confirm = true }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Post, $"/api/widgets/{missing}/identity-secret", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Only_admins_preview_a_widget_and_a_preview_token_works_only_with_an_admins_session()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        app.ChatModels.Reset();
        using var agentResponse = await SendAsync(app, HttpMethod.Post, "/api/agents", new SaveAgentRequest("Shop assistant", ApiClient.UniqueWord("shop-")), AuthRoles.Admin);
        var agent = await agentResponse.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        using var widgetResponse = await SendAsync(app, HttpMethod.Post, "/api/widgets", new SaveWidgetRequest("Demo shop", agent.Id), AuthRoles.Admin);
        var widget = await widgetResponse.ReadJsonAsync<WidgetDto>(HttpStatusCode.Created);

        (await SendAsync(app, HttpMethod.Post, $"/api/widgets/{widget.Id}/preview-session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Post, $"/api/widgets/{widget.Id}/preview-session", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var started = await SendAsync(app, HttpMethod.Post, $"/api/widgets/{widget.Id}/preview-session", roles: AuthRoles.Admin);
        var token = (await started.ReadJsonAsync<WidgetPreviewSessionDto>()).Session.Token;

        async Task<HttpStatusCode> SendAsVisitorAsync(params string[] roles)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/widgets/{widget.PublicKey}/contact")
            {
                Content = JsonContent.Create(new VisitorContactRequest("Ana"), options: TestJson.Options),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (roles.Length > 0)
            {
                request.As(roles);
            }

            using var response = await app.Client.SendAsync(request, Ct);
            return response.StatusCode;
        }

        (await SendAsVisitorAsync()).ShouldBe(HttpStatusCode.Unauthorized, "the public cannot use a preview token");
        (await SendAsVisitorAsync(AuthRoles.Support)).ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsVisitorAsync(AuthRoles.Admin)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_reads_contacts_and_segments_and_saves_segments_but_only_admins_manage_attributes_and_delete()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        var missing = Guid.NewGuid();

        (await SendAsync(app, HttpMethod.Get, "/api/segments")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Get, "/api/segments", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/people/attributes", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, "/api/people/count?f=returning:true", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Post, "/api/segments", new { name = "Returning", filter = new[] { new { attr = "returning", op = "true" } } }, AuthRoles.Support))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(app, HttpMethod.Patch, $"/api/segments/{missing}", new { name = "X" }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Delete, $"/api/segments/{missing}", roles: AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Patch, "/api/people/attributes/plan", new { label = "Plan" }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Delete, "/api/people/attributes/plan", new { confirm = true }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(app, HttpMethod.Patch, $"/api/people/{missing}/attributes", new { attributes = new { plan = "pro" } }, AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(app, HttpMethod.Delete, $"/api/segments/{missing}", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Patch, "/api/people/attributes/plan", new { label = "Plan" }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Delete, "/api/people/attributes/plan", new { confirm = true }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(app, HttpMethod.Patch, $"/api/people/{missing}/attributes", new { attributes = new { plan = "pro" } }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Admins_change_configuration_and_me_reports_the_roles()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);

        using var added = await SendAsync(app, HttpMethod.Post, "/api/sources/text", new AddTextSourceRequest("Notes", "Some text."), AuthRoles.Admin);
        added.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await SendAsync(app, HttpMethod.Post, "/api/labels", new { name = "Billing", color = "teal" }, AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(app, HttpMethod.Get, "/api/system/health", roles: AuthRoles.Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var me = await SendAsync(app, HttpMethod.Get, "/api/auth/me", roles: [AuthRoles.Support, AuthRoles.Admin]);
        var user = await me.ReadJsonAsync<CurrentUserDto>();
        user.ShouldBe(new CurrentUserDto("Test user", "test@example.com", [AuthRoles.Admin, AuthRoles.Support], "none"), new CurrentUserComparer());

        (await SendAsync(app, HttpMethod.Get, "/api/sources", roles: "viewer")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "a role the portal does not know");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Changes_with_the_session_cookie_need_the_request_header()
    {
        var app = await fixture.GetAppAsync();

        using var withoutHeader = new HttpRequestMessage(HttpMethod.Post, "/api/sources/text") { Content = JsonContent.Create(new AddTextSourceRequest("Notes", "Some text.")) };
        withoutHeader.Headers.Add("Cookie", $"{AuthModule.CookieName}=session");
        using var rejected = await app.Client.SendAsync(withoutHeader, Ct);
        (await rejected.ReadProblemAsync(HttpStatusCode.Forbidden)).Title.ShouldBe("Missing request header");

        using var withHeader = new HttpRequestMessage(HttpMethod.Post, "/api/sources/text") { Content = JsonContent.Create(new AddTextSourceRequest("Notes", "Some text.")) };
        withHeader.Headers.Add("Cookie", $"{AuthModule.CookieName}=session");
        withHeader.Headers.Add(AuthModule.RequestHeader, "1");
        using var accepted = await app.Client.SendAsync(withHeader, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Reading needs no header, and neither does a request without the cookie (Inngest, widgets, scripts).
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/sources");
        read.Headers.Add("Cookie", $"{AuthModule.CookieName}=session");
        (await app.Client.SendAsync(read, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_session_without_a_refresh_token_must_sign_in_again()
    {
        await using var app = await CreateOidcAppAsync();

        // A session without a refresh token: there is nothing to check it with.
        using var response = await SendWithSessionAsync(app, Session(app, refreshToken: null, due: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(AuthModule.CookieName + "=;", StringComparison.Ordinal), "the cookie is removed");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_session_stays_while_the_identity_provider_cannot_be_reached()
    {
        await using var app = await CreateOidcAppAsync();

        using var response = await SendWithSessionAsync(app, Session(app, refreshToken: "refresh", due: DateTimeOffset.UtcNow.AddMinutes(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, app.Logs.Dump());
        (await response.ReadJsonAsync<CurrentUserDto>()).Roles.ShouldBe([AuthRoles.Support]);
        app.Logs.Dump().ShouldContain("Could not check the session of user-1");
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(AuthModule.CookieName + "=", StringComparison.Ordinal)
            && !cookie.StartsWith(AuthModule.CookieName + "=;", StringComparison.Ordinal), "the session is renewed with the time of the next try");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Me_says_whether_the_user_may_manage_users_in_keycloak()
    {
        await using var app = await CreateOidcAppAsync();
        var due = DateTimeOffset.UtcNow.AddMinutes(5);

        using var userAdmin = await SendWithSessionAsync(app, Session(app, refreshToken: "refresh", due: due, new Claim(UserAdminClaim.Name, "true")));
        (await userAdmin.ReadJsonAsync<CurrentUserDto>()).CanManageUsers.ShouldBeTrue();

        using var appAdmin = await SendWithSessionAsync(app, Session(app, refreshToken: "refresh", due: due, new Claim("roles", AuthRoles.Admin)));
        var me = await appAdmin.ReadJsonAsync<CurrentUserDto>();
        me.Roles.ShouldContain(AuthRoles.Admin);
        me.CanManageUsers.ShouldBeFalse("an admin of the app is not a user admin in Keycloak");

        var local = await fixture.GetAppAsync();
        (await local.Client.GetJsonAsync<CurrentUserDto>("/api/auth/me")).CanManageUsers.ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task System_health_reports_sign_in_down_without_naming_the_authority()
    {
        await using var app = await CreateOidcAppAsync();

        var health = await app.Services.GetRequiredService<SystemHealthService>().GetAsync(Ct);

        var signIn = health.Components.Single(c => c.Key == SystemComponents.SignIn);
        signIn.Status.ShouldBe(SystemComponentStatus.Down);
        signIn.Variant.ShouldBe("oidc");
        signIn.Detail.ShouldBe("Not reachable; new sign-ins fail, open sessions keep working");
        health.Components.ShouldAllBe(c => !c.Detail.Contains("127.0.0.1") && !c.Detail.Contains("test-client-secret"));
    }

    // Nothing answers at the authority, so these sessions are made the way the API's sign-in would make them.
    private Task<KnowledgeAgentApp> CreateOidcAppAsync() =>
        fixture.CreateAppAsync(options =>
        {
            options.Settings["Auth:Mode"] = "Oidc";
            options.Settings["Auth:Authority"] = "http://127.0.0.1:9/realms/knowledge-agent";
            options.Settings["Auth:ClientSecret"] = "test-client-secret";
        });

    private static string Session(KnowledgeAgentApp app, string? refreshToken, DateTimeOffset? due, params Claim[] extra)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-1"), new Claim("name", "Signed-in user"), new Claim("roles", AuthRoles.Support), .. extra],
            authenticationType: "oidc",
            nameType: "name",
            roleType: "roles"));
        var now = DateTimeOffset.UtcNow;
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(8) };
        List<AuthenticationToken> tokens = [new() { Name = "id_token", Value = "id" }];
        if (refreshToken is not null)
        {
            tokens.Add(new() { Name = "refresh_token", Value = refreshToken });
        }

        properties.StoreTokens(tokens);
        if (due is { } dueAt)
        {
            properties.Items[SessionCheck.DueItem] = dueAt.ToString("O", CultureInfo.InvariantCulture);
        }

        var cookie = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSchemes.Cookie);
        return cookie.TicketDataFormat.Protect(new AuthenticationTicket(principal, properties, AuthSchemes.Cookie));
    }

    private static async Task<HttpResponseMessage> SendWithSessionAsync(KnowledgeAgentApp app, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"{AuthModule.CookieName}={session}");
        return await app.Client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> SendAsync(KnowledgeAgentApp app, HttpMethod method, string url, object? body = null, params string[] roles)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: TestJson.Options);
        }

        if (roles.Length > 0)
        {
            request.As(roles);
        }

        return await app.Client.SendAsync(request, Ct);
    }

    private sealed class CurrentUserComparer : IEqualityComparer<CurrentUserDto>
    {
        public bool Equals(CurrentUserDto? x, CurrentUserDto? y) =>
            x is not null && y is not null && x.Name == y.Name && x.Email == y.Email && x.Mode == y.Mode && x.Roles.SequenceEqual(y.Roles);

        public int GetHashCode(CurrentUserDto obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
