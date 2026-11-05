using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.TestKit;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.Keycloak;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Runs Keycloak with the stack's realm file; two HttpClients play the browser against the API and Keycloak.
public sealed partial class KeycloakSignInTests(KeycloakSignInTests.KeycloakFixture keycloak, InMemoryAppFixture fixture)
    : IClassFixture<KeycloakSignInTests.KeycloakFixture>, IClassFixture<InMemoryAppFixture>
{
    private const string ClientSecret = "test-client-secret-0123456789";
    private const string Password = "test-password";
    private const string KeycloakAdminUser = "admin";
    private const string KeycloakAdminPassword = "test-keycloak-admin";

    // Short, so the tests see sessions checked again.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private static string RealmFile => Path.Combine(AppContext.BaseDirectory, "keycloak", "knowledge-agent-realm.json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Admins_sign_in_through_keycloak_get_their_roles_and_sign_out()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });

        await SignInAsync(api, "admin@knowledge-agent.local", returnUrl: "/chat");

        var me = await api.GetJsonAsync<CurrentUserDto>("/api/auth/me");
        me.Name.ShouldBe("Admin Local");
        me.Email.ShouldBe("admin@knowledge-agent.local");
        me.Roles.ShouldBe([AuthRoles.Admin, AuthRoles.Support]);
        me.Mode.ShouldBe("oidc");
        (await api.GetAsync("/api/prompts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var logout = await api.GetAsync("/api/auth/logout", Ct);
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var endSession = logout.Headers.Location!;
        endSession.AbsoluteUri.ShouldStartWith(keycloak.Authority + "/protocol/openid-connect/logout");
        endSession.Query.ShouldContain("id_token_hint=");
        using var browser = keycloak.CreateBrowser();
        using var loggedOut = await browser.GetAsync(endSession, Ct);
        loggedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var back = loggedOut.Headers.Location!;
        back.AbsolutePath.ShouldBe(AuthModule.SignedOutCallbackPath);
        using var home = await api.GetAsync(back.PathAndQuery, Ct);
        home.Headers.Location!.OriginalString.ShouldBe("/");
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_users_get_only_the_support_role()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });

        await SignInAsync(api, "user@knowledge-agent.local", returnUrl: "/");

        (await api.GetJsonAsync<CurrentUserDto>("/api/auth/me")).Roles.ShouldBe([AuthRoles.Support]);
        (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync("/api/prompts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_wrong_password_never_creates_a_session()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var browser = keycloak.CreateBrowser();

        using var login = await api.GetAsync("/api/auth/login", Ct);
        var form = await GetLoginFormAsync(browser, login.Headers.Location!);
        using var rejected = await browser.PostAsync(form, Credentials("admin@knowledge-agent.local", "wrong"), Ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, "Keycloak shows the form again");
        (await rejected.Content.ReadAsStringAsync(Ct)).ShouldContain("Invalid username or password");
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_sign_in_page_may_stay_open_as_long_as_keycloak_allows()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var realm = JsonDocument.Parse(await File.ReadAllTextAsync(RealmFile, Ct));
        var loginTimeout = TimeSpan.FromSeconds(realm.RootElement.GetProperty("accessCodeLifespanLogin").GetInt32());

        var oidc = app.Factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthSchemes.Oidc);

        loginTimeout.ShouldBe(TimeSpan.FromHours(8));
        oidc.RemoteAuthenticationTimeout.ShouldBe(loginTimeout, "the correlation cookie lasts as long as the page");
        oidc.ProtocolValidator.NonceLifetime.ShouldBe(loginTimeout, "so does the nonce");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_sign_in_that_went_stale_here_starts_over_without_the_password()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var browser = keycloak.CreateBrowser();
        using var started = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        var callback = await SignInAtKeycloakAsync(started, browser, "admin@knowledge-agent.local", returnUrl: "/widgets");

        // This client lacks the sign-in's correlation cookie, like a browser whose sign-in page stayed open past it.
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var stale = await api.GetAsync(callback.PathAndQuery, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var again = stale.Headers.Location!;
        again.AbsoluteUri.ShouldStartWith(keycloak.Authority + "/protocol/openid-connect/auth");

        await CompleteSignInAsync(api, await ReturnFromKeycloakAsync(browser, again), "/widgets");
        (await api.GetJsonAsync<CurrentUserDto>("/api/auth/me")).Email.ShouldBe("admin@knowledge-agent.local");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_sign_in_starts_over_only_once()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var browser = keycloak.CreateBrowser();
        using var started = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        var callback = await SignInAtKeycloakAsync(started, browser, "admin@knowledge-agent.local", returnUrl: "/");
        using var retrying = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var stale = await retrying.GetAsync(callback.PathAndQuery, Ct);
        var second = await ReturnFromKeycloakAsync(browser, stale.Headers.Location!);

        // The second return goes stale as well, so the portal explains instead of starting over forever.
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var failed = await api.GetAsync(second.PathAndQuery, Ct);

        failed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        failed.Headers.Location!.OriginalString.ShouldBe("/?authError=" + Uri.EscapeDataString("Correlation failed."));
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_error_from_keycloak_goes_to_the_portal_without_starting_over()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var browser = keycloak.CreateBrowser();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        var callback = await SignInAtKeycloakAsync(api, browser, "admin@knowledge-agent.local", returnUrl: "/");
        var state = QueryHelpers.ParseQuery(callback.Query)["state"].ToString();

        using var denied = await api.GetAsync($"{AuthModule.CallbackPath}?state={Uri.EscapeDataString(state)}&error=access_denied", Ct);

        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = denied.Headers.Location!.OriginalString;
        location.ShouldStartWith("/?authError=");
        Uri.UnescapeDataString(location).ShouldContain("access_denied");
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Requests_between_session_checks_do_not_ask_keycloak()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var backchannel = new Backchannel();
        await using var app = await CreateAppAsync(backchannel: backchannel);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });

        await SignInAsync(api, "user@knowledge-agent.local", returnUrl: "/");
        for (var i = 0; i < 3; i++)
        {
            (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        backchannel.Refreshes.ShouldBe(0, "the default interval is minutes");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_disabled_account_is_signed_out_at_the_next_session_check()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync(CheckInterval);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Admin, AuthRoles.Support);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, user.Name, returnUrl: "/");
        (await api.GetAsync("/api/prompts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await admin.SetEnabledAsync(user, enabled: false);
        await Task.Delay(CheckInterval * 1.5, Ct);

        using var refused = await api.GetAsync("/api/prompts", Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, app.Logs.Dump());
        refused.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(AuthModule.CookieName + "=;", StringComparison.Ordinal), "the cookie is removed");
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        app.Logs.Dump().ShouldContain("invalid_grant");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Role_changes_reach_a_session_at_its_next_check()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync(CheckInterval);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Admin, AuthRoles.Support);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, user.Name, returnUrl: "/");
        (await api.GetJsonAsync<CurrentUserDto>("/api/auth/me")).Roles.ShouldBe([AuthRoles.Admin, AuthRoles.Support]);

        await admin.RemoveRoleAsync(user, AuthRoles.Admin);
        await Task.Delay(CheckInterval * 1.5, Ct);

        var me = await api.GetJsonAsync<CurrentUserDto>("/api/auth/me");
        me.Roles.ShouldBe([AuthRoles.Support], "the session goes on with the roles Keycloak has now");
        me.Email.ShouldBe(user.Name);
        (await api.GetAsync("/api/prompts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await admin.AddRoleAsync(user, AuthRoles.Admin);
        await Task.Delay(CheckInterval * 1.5, Ct);

        (await api.GetAsync("/api/prompts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "a role given back works without signing in again");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Only_user_admins_in_keycloak_may_open_its_console()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync(CheckInterval);
        using var admin = keycloak.CreateAdmin();
        var userAdmin = await admin.CreateUserAsync(AuthRoles.Admin, AuthRoles.Support);
        await admin.AddClientRoleAsync(userAdmin, "realm-management", "manage-users");
        var appAdmin = await admin.CreateUserAsync(AuthRoles.Admin, AuthRoles.Support);

        using var asUserAdmin = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(asUserAdmin, userAdmin.Name, returnUrl: "/");
        (await asUserAdmin.GetJsonAsync<CurrentUserDto>("/api/auth/me")).CanManageUsers.ShouldBeTrue("the portal client's mapper puts manage-users into the ID token");

        using var asAppAdmin = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(asAppAdmin, appAdmin.Name, returnUrl: "/");
        var me = await asAppAdmin.GetJsonAsync<CurrentUserDto>("/api/auth/me");
        me.Roles.ShouldBe([AuthRoles.Admin, AuthRoles.Support]);
        me.CanManageUsers.ShouldBeFalse("an admin of the app is not a user admin in Keycloak");

        await admin.AddClientRoleAsync(appAdmin, "realm-management", "manage-users");
        await Task.Delay(CheckInterval * 1.5, Ct);
        (await asAppAdmin.GetJsonAsync<CurrentUserDto>("/api/auth/me")).CanManageUsers.ShouldBeTrue(app.Logs.Dump());
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Signing_a_user_out_in_keycloak_ends_the_session_here()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync(CheckInterval);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Support);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, user.Name, returnUrl: "/");

        await admin.SignOutAsync(user);
        await Task.Delay(CheckInterval * 1.5, Ct);

        (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task While_keycloak_cannot_be_reached_sessions_stay_and_are_checked_again()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var backchannel = new Backchannel();
        await using var app = await CreateAppAsync(CheckInterval, backchannel);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Support);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, user.Name, returnUrl: "/");

        backchannel.Unreachable = true;
        await Task.Delay(CheckInterval * 1.5, Ct);
        (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "an outage signs nobody out");
        backchannel.Refreshes.ShouldBe(1);

        backchannel.Unreachable = false;
        await admin.SetEnabledAsync(user, enabled: false);
        await Task.Delay(CheckInterval * 1.5, Ct);
        (await api.GetAsync("/api/conversations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the next try reaches Keycloak");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Requests_that_arrive_together_share_one_session_check()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var backchannel = new Backchannel { RefreshDelay = TimeSpan.FromMilliseconds(500) };
        await using var app = await CreateAppAsync(CheckInterval, backchannel);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, "user@knowledge-agent.local", returnUrl: "/");
        await Task.Delay(CheckInterval * 1.5, Ct);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.GetAsync("/api/conversations", Ct)));
        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
            backchannel.Refreshes.ShouldBe(1, "the five requests share one check");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_live_view_ends_when_its_session_is_due_for_a_check()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync(CheckInterval);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Support);
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(api, user.Name, returnUrl: "/");
        Guid conversationId;
        using (var scope = app.Services.CreateScope())
        {
            var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            conversationId = (await conversations.CreateAsync(new Conversation { Title = "Followed" }, Ct)).Id;
        }

        // The view closes by itself, and the reconnect runs the check.
        using (var view = await FollowAsync(api, conversationId))
        {
            view.StatusCode.ShouldBe(HttpStatusCode.OK);
            await (await view.Content.ReadAsStreamAsync(Ct)).CopyToAsync(Stream.Null, Ct).WaitAsync(TimeSpan.FromSeconds(15), Ct);
        }

        await admin.SetEnabledAsync(user, enabled: false);
        await Task.Delay(CheckInterval * 1.5, Ct);

        using var refused = await FollowAsync(api, conversationId);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> FollowAsync(HttpClient api, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/events");
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        return await api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    private Task<KnowledgeAgentApp> CreateAppAsync(TimeSpan? checkInterval = null, Backchannel? backchannel = null) =>
        fixture.CreateAppAsync(options =>
        {
            options.Settings["Auth:Mode"] = "Oidc";
            options.Settings["Auth:Authority"] = keycloak.Authority;
            options.Settings["Auth:ClientSecret"] = ClientSecret;
            options.Settings["Auth:PublicOrigin"] = "http://localhost";
            if (checkInterval is { } interval)
            {
                options.Settings["Auth:SessionCheckInterval"] = interval.ToString("c", CultureInfo.InvariantCulture);
            }

            if (backchannel is not null)
            {
                options.ConfigureServices = services =>
                    services.Configure<OpenIdConnectOptions>(AuthSchemes.Oidc, oidc => oidc.BackchannelHttpHandler = backchannel);
            }
        });

    private async Task SignInAsync(HttpClient api, string user, string returnUrl)
    {
        using var browser = keycloak.CreateBrowser();
        var callback = await SignInAtKeycloakAsync(api, browser, user, returnUrl);
        await CompleteSignInAsync(api, callback, returnUrl);
    }

    private async Task<Uri> SignInAtKeycloakAsync(HttpClient api, HttpClient browser, string user, string returnUrl)
    {
        using var login = await api.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync(Ct));
        var authorize = login.Headers.Location!;
        authorize.AbsoluteUri.ShouldStartWith(keycloak.Authority + "/protocol/openid-connect/auth");

        // Keycloak uses pushed authorization requests, so the browser only carries a request_uri.
        authorize.Query.ShouldContain("request_uri=");

        var form = await GetLoginFormAsync(browser, authorize);
        using var signedIn = await browser.PostAsync(form, Credentials(user, Password), Ct);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect, await signedIn.Content.ReadAsStringAsync(Ct));
        var callback = signedIn.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).ShouldBe("http://localhost" + AuthModule.CallbackPath);
        return callback;
    }

    private static async Task CompleteSignInAsync(HttpClient api, Uri callback, string returnUrl)
    {
        using var completed = await api.GetAsync(callback.PathAndQuery, Ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.Redirect, await completed.Content.ReadAsStringAsync(Ct));
        completed.Headers.Location!.OriginalString.ShouldBe(returnUrl);
        completed.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(AuthModule.CookieName, StringComparison.Ordinal) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Uri> ReturnFromKeycloakAsync(HttpClient browser, Uri authorize)
    {
        var location = authorize;
        for (var hops = 0; hops < 5 && location.Authority == authorize.Authority; hops++)
        {
            using var page = await browser.GetAsync(location, Ct);
            page.StatusCode.ShouldBe(HttpStatusCode.Redirect, "Keycloak asks for no password again");
            location = new Uri(location, page.Headers.Location!);
        }

        location.GetLeftPart(UriPartial.Path).ShouldBe("http://localhost" + AuthModule.CallbackPath);
        return location;
    }

    private static async Task<Uri> GetLoginFormAsync(HttpClient browser, Uri authorize)
    {
        // Keycloak first redirects to its own login step, as it does for a browser.
        var page = await browser.GetAsync(authorize, Ct);
        for (var hops = 0; page.StatusCode == HttpStatusCode.Redirect && hops < 5; hops++)
        {
            var next = new Uri(authorize, page.Headers.Location!);
            next.Authority.ShouldBe(authorize.Authority, "the login stays at Keycloak");
            page.Dispose();
            page = await browser.GetAsync(next, Ct);
        }

        using var _ = page;
        page.StatusCode.ShouldBe(HttpStatusCode.OK, page.Headers.Location?.ToString());
        var html = await page.Content.ReadAsStringAsync(Ct);
        var form = LoginForm().Match(html);
        form.Success.ShouldBeTrue("the Keycloak login form");
        var action = FormAction().Match(form.Value);
        action.Success.ShouldBeTrue("the login form's action");
        return new Uri(WebUtility.HtmlDecode(action.Groups[1].Value));
    }

    private static FormUrlEncodedContent Credentials(string user, string password) =>
        new(new Dictionary<string, string> { ["username"] = user, ["password"] = password, ["credentialId"] = string.Empty });

    [GeneratedRegex(@"<form\b[^>]*\bid=""kc-form-login""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LoginForm();

    [GeneratedRegex(@"\baction=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FormAction();

    private sealed class Backchannel() : DelegatingHandler(new HttpClientHandler())
    {
        private int _refreshes;
        private volatile bool _unreachable;

        public int Refreshes => Volatile.Read(ref _refreshes);

        public bool Unreachable
        {
            get => _unreachable;
            set => _unreachable = value;
        }

        // Holds refreshes back, so requests sent together find the check still running.
        public TimeSpan RefreshDelay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null && (await request.Content.ReadAsStringAsync(cancellationToken)).Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _refreshes);
                if (_unreachable)
                {
                    throw new HttpRequestException("Connection refused");
                }

                await Task.Delay(RefreshDelay, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    public sealed record KeycloakUser(string Id, string Name);

    public sealed class KeycloakAdmin(Uri baseAddress) : IDisposable
    {
        private readonly HttpClient _http = new() { BaseAddress = baseAddress };

        public async Task<KeycloakUser> CreateUserAsync(params string[] roles)
        {
            var name = $"user-{Guid.NewGuid():N}@knowledge-agent.local";
            using var created = await SendAsync(HttpMethod.Post, "users", new
            {
                username = name,
                email = name,
                emailVerified = true,
                firstName = "Test",
                lastName = "User",
                enabled = true,
                credentials = new[] { new { type = "password", value = Password, temporary = false } },
            });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
            var user = new KeycloakUser(created.Headers.Location!.Segments[^1], name);
            foreach (var role in roles)
            {
                await AddRoleAsync(user, role);
            }

            return user;
        }

        public async Task SetEnabledAsync(KeycloakUser user, bool enabled)
        {
            // The whole representation goes back, so nothing else of the user changes.
            using var read = await SendAsync(HttpMethod.Get, $"users/{user.Id}");
            var representation = JsonNode.Parse(await read.Content.ReadAsStringAsync(Ct))!.AsObject();
            representation["enabled"] = enabled;
            using var updated = await SendAsync(HttpMethod.Put, $"users/{user.Id}", representation);
            updated.StatusCode.ShouldBe(HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync(Ct));
        }

        public Task AddRoleAsync(KeycloakUser user, string role) => ChangeRoleAsync(HttpMethod.Post, user, role);

        public Task RemoveRoleAsync(KeycloakUser user, string role) => ChangeRoleAsync(HttpMethod.Delete, user, role);

        public async Task AddClientRoleAsync(KeycloakUser user, string clientId, string role)
        {
            using var clients = await SendAsync(HttpMethod.Get, $"clients?clientId={Uri.EscapeDataString(clientId)}");
            var id = JsonNode.Parse(await clients.Content.ReadAsStringAsync(Ct))!.AsArray().Single()!["id"]!.GetValue<string>();
            using var read = await SendAsync(HttpMethod.Get, $"clients/{id}/roles/{Uri.EscapeDataString(role)}");
            read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(Ct));
            var representation = JsonNode.Parse(await read.Content.ReadAsStringAsync(Ct));
            using var response = await SendAsync(HttpMethod.Post, $"users/{user.Id}/role-mappings/clients/{id}", new JsonArray(representation));
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        }

        public async Task SignOutAsync(KeycloakUser user)
        {
            using var response = await SendAsync(HttpMethod.Post, $"users/{user.Id}/logout");
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        }

        public void Dispose() => _http.Dispose();

        private async Task ChangeRoleAsync(HttpMethod method, KeycloakUser user, string role)
        {
            using var read = await SendAsync(HttpMethod.Get, $"roles/{role}");
            var representation = JsonNode.Parse(await read.Content.ReadAsStringAsync(Ct));
            using var response = await SendAsync(method, $"users/{user.Id}/role-mappings/realm", new JsonArray(representation));
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, "admin/realms/knowledge-agent/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync());
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await _http.SendAsync(request, Ct);
        }

        // Admin tokens of the master realm last a minute, so each call gets a fresh one.
        private async Task<string> GetTokenAsync()
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = KeycloakAdminUser,
                ["password"] = KeycloakAdminPassword,
            });
            using var response = await _http.PostAsync("realms/master/protocol/openid-connect/token", form, Ct);
            var json = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, json);
            return JsonNode.Parse(json)!["access_token"]!.GetValue<string>();
        }
    }

    private sealed class BrowserCookies : DelegatingHandler
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await base.SendAsync(request, cancellationToken);
            foreach (var header in response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            {
                var pair = header.Split(';', 2)[0].Split('=', 2);
                var expired = header.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase) || pair.Length < 2 || pair[1].Length == 0;
                if (expired)
                {
                    _cookies.Remove(pair[0]);
                }
                else
                {
                    _cookies[pair[0]] = pair[1];
                }
            }

            return response;
        }
    }

    public sealed class KeycloakFixture : IAsyncLifetime
    {
        private KeycloakContainer? _container;

        public string Authority { get; private set; } = string.Empty;

        private Uri BaseAddress { get; set; } = new("http://localhost/");

        // Keycloak keeps its login session in cookies marked Secure even on http. Browsers send those to localhost;
        // CookieContainer does not, so the test browser keeps its own jar.
        public HttpClient CreateBrowser() =>
            new(new BrowserCookies { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false } });

        public KeycloakAdmin CreateAdmin() => new(BaseAddress);

        public async ValueTask InitializeAsync()
        {
            if (!DockerAvailability.IsAvailable)
            {
                return;
            }

            _container = new KeycloakBuilder(ContainerImages.Keycloak)
                .WithUsername(KeycloakAdminUser)
                .WithPassword(KeycloakAdminPassword)
                // Keycloak only imports a file named after its realm.
                .WithRealm(RealmFile)
                .WithEnvironment("KA_PORTAL_CLIENT_SECRET", ClientSecret)
                .WithEnvironment("KA_PORTAL_URL", "http://localhost")
                .WithEnvironment("KA_LANGFUSE_CLIENT_SECRET", LangfuseClientSecret)
                .WithEnvironment("KA_LANGFUSE_URL", LangfuseUrl)
                .WithEnvironment("KA_ADMIN_PASSWORD", Password)
                .WithEnvironment("KA_USER_PASSWORD", Password)
                .Build();
            await _container.StartAsync();
            BaseAddress = new Uri(_container.GetBaseAddress());
            Authority = _container.GetBaseAddress().TrimEnd('/') + "/realms/knowledge-agent";
        }

        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }
}
