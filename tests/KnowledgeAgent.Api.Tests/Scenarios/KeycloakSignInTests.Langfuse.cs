using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.TestKit;
using Microsoft.AspNetCore.WebUtilities;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Langfuse signs admins in through the realm's langfuse client and denies everyone else.
public sealed partial class KeycloakSignInTests
{
    private const string LangfuseClientSecret = "test-langfuse-secret-0123456789";
    private const string LangfuseUrl = "http://localhost:3000";
    private const string LangfuseCallback = LangfuseUrl + "/api/auth/callback/custom";
    private const string LangfuseDenial = "Langfuse is for admins.";

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Admins_sign_in_to_langfuse_through_its_client()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var browser = keycloak.CreateBrowser();

        var form = await GetLoginFormAsync(browser, LangfuseAuthorize());
        using var signedIn = await browser.PostAsync(form, Credentials("admin@knowledge-agent.local", Password), Ct);

        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect, await signedIn.Content.ReadAsStringAsync(Ct));
        var idToken = await RedeemLangfuseCodeAsync(signedIn.Headers.Location!);
        idToken["email"]!.GetValue<string>().ShouldBe("admin@knowledge-agent.local");
        idToken["email_verified"]!.GetValue<bool>().ShouldBeTrue("Langfuse links the seeded admin by this email");
        idToken["iss"]!.GetValue<string>().ShouldBe(keycloak.Authority);
        idToken["aud"]!.ToString().ShouldContain("langfuse");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_users_are_denied_langfuse()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var browser = keycloak.CreateBrowser();

        var form = await GetLoginFormAsync(browser, LangfuseAuthorize());
        using var denied = await browser.PostAsync(form, Credentials("user@knowledge-agent.local", Password), Ct);

        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "Keycloak shows its denial page and returns no code");
        denied.Headers.Location.ShouldBeNull();
        (await denied.Content.ReadAsStringAsync(Ct)).ShouldContain(LangfuseDenial);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_admin_signed_in_to_the_portal_opens_langfuse_without_a_password()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var browser = keycloak.CreateBrowser();
        await CompleteSignInAsync(api, await SignInAtKeycloakAsync(api, browser, "admin@knowledge-agent.local", "/"), "/");

        using var authorize = await browser.GetAsync(LangfuseAuthorize(), Ct);

        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the Keycloak session signs the admin in");
        (await RedeemLangfuseCodeAsync(authorize.Headers.Location!))["email"]!.GetValue<string>().ShouldBe("admin@knowledge-agent.local");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_users_signed_in_to_the_portal_are_denied_langfuse_and_keep_their_portal_session()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var app = await CreateAppAsync();
        using var api = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var browser = keycloak.CreateBrowser();
        await CompleteSignInAsync(api, await SignInAtKeycloakAsync(api, browser, "user@knowledge-agent.local", "/"), "/");

        using var denied = await browser.GetAsync(LangfuseAuthorize(), Ct);

        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await denied.Content.ReadAsStringAsync(Ct)).ShouldContain(LangfuseDenial);
        (await api.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Users_given_the_admin_role_get_into_langfuse()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var admin = keycloak.CreateAdmin();
        var user = await admin.CreateUserAsync(AuthRoles.Support);
        using var browser = keycloak.CreateBrowser();
        var form = await GetLoginFormAsync(browser, LangfuseAuthorize());
        using var denied = await browser.PostAsync(form, Credentials(user.Name, Password), Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await admin.AddRoleAsync(user, AuthRoles.Admin);
        using var again = keycloak.CreateBrowser();
        using var signedIn = await again.PostAsync(await GetLoginFormAsync(again, LangfuseAuthorize()), Credentials(user.Name, Password), Ct);

        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect, await signedIn.Content.ReadAsStringAsync(Ct));
        (await RedeemLangfuseCodeAsync(signedIn.Headers.Location!))["email"]!.GetValue<string>().ShouldBe(user.Name);
    }

    private Uri LangfuseAuthorize() => new(QueryHelpers.AddQueryString(keycloak.Authority + "/protocol/openid-connect/auth", new Dictionary<string, string?>
    {
        ["client_id"] = "langfuse",
        ["response_type"] = "code",
        ["scope"] = "openid email profile",
        ["redirect_uri"] = LangfuseCallback,
        ["state"] = Guid.NewGuid().ToString("N"),
    }));

    // Redeems the code as Langfuse does, with the client secret, and returns the ID token's claims.
    private async Task<JsonNode> RedeemLangfuseCodeAsync(Uri callback)
    {
        callback.GetLeftPart(UriPartial.Path).ShouldBe(LangfuseCallback);
        var code = QueryHelpers.ParseQuery(callback.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, keycloak.Authority + "/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = LangfuseCallback,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("langfuse:" + LangfuseClientSecret)));
        using var response = await http.SendAsync(request, Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, json);

        var payload = JsonNode.Parse(json)!["id_token"]!.GetValue<string>().Split('.')[1];
        return JsonNode.Parse(WebEncoders.Base64UrlDecode(payload))!;
    }
}
