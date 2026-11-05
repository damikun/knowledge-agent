using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.SystemInfo;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class SystemScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Health_endpoints_report_healthy()
    {
        var app = await fixture.GetAppAsync();

        using var health = await app.Client.GetAsync("/health", Ct);
        var healthBody = await health.Content.ReadAsStringAsync(Ct);
        health.StatusCode.ShouldBe(HttpStatusCode.OK, healthBody);
        healthBody.ShouldBe("Healthy");

        using var alive = await app.Client.GetAsync("/alive", Ct);
        alive.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task System_info_describes_stores_prompts_langfuse_and_providers()
    {
        var app = await fixture.GetAppAsync();

        var info = await app.Client.GetJsonAsync<SystemInfoDto>("/api/system/info");

        info.Version.ShouldNotBeNullOrWhiteSpace();
        info.Environment.ShouldBe(KnowledgeAgentFactory.EnvironmentName);
        info.PersistenceProvider.ShouldBe(fixture.Mode == TestMode.InMemory ? "InMemory" : "Postgres", StringCompareShould.IgnoreCase);
        info.VectorStoreProvider.ShouldContain(fixture.Mode == TestMode.InMemory ? "InMemory" : "ClickHouse", Case.Insensitive);
        info.PromptStore.ShouldBe("Langfuse");
        info.LangfuseConfigured.ShouldBeTrue();
        info.LangfuseUrl.ShouldBe(app.Langfuse.BaseAddress.ToString().TrimEnd('/'));
        info.ResearcherEnabled.ShouldBeTrue();

        var fake = info.Providers.Single(p => p.Key == FakeModels.ProviderKey);
        fake.Type.ShouldBe(FakeAiProvider.TypeName);
        fake.Configured.ShouldBeTrue();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Every_staff_member_reads_the_public_address_and_only_admins_change_it()
    {
        await using var app = await fixture.CreateAppAsync(options =>
        {
            options.ConfigureServices = TestUsers.Install;
            options.Settings[PublicAddressStore.ConfigurationKey] = "https://config.shop.test/";
        });
        const string url = "/api/system/public-address";

        (await SendAsync(app, HttpMethod.Get, url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(app, HttpMethod.Put, url, new SavePublicAddressRequest("https://support.shop.test"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using (var configured = await SendAsync(app, HttpMethod.Get, url, roles: AuthRoles.Support))
        {
            (await configured.ReadJsonAsync<PublicAddressDto>()).ShouldBe(new PublicAddressDto("https://config.shop.test", PublicAddressSources.Configuration));
        }

        (await SendAsync(app, HttpMethod.Put, url, new SavePublicAddressRequest("https://support.shop.test"), AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var invalid = await SendAsync(app, HttpMethod.Put, url, new SavePublicAddressRequest("support.shop.test/?a=1"), AuthRoles.Admin))
        {
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys.ShouldBe(["url"]);
        }

        using (var saved = await SendAsync(app, HttpMethod.Put, url, new SavePublicAddressRequest("https://support.shop.test/help/"), AuthRoles.Admin))
        {
            (await saved.ReadJsonAsync<PublicAddressDto>()).ShouldBe(new PublicAddressDto("https://support.shop.test/help", PublicAddressSources.Saved));
        }

        using (var info = await SendAsync(app, HttpMethod.Get, "/api/system/info", roles: AuthRoles.Support))
        {
            (await info.ReadJsonAsync<SystemInfoDto>()).PublicAddress.Url.ShouldBe("https://support.shop.test/help");
        }

        using var cleared = await SendAsync(app, HttpMethod.Put, url, new SavePublicAddressRequest(null), AuthRoles.Admin);
        (await cleared.ReadJsonAsync<PublicAddressDto>()).Source.ShouldBe(PublicAddressSources.Configuration);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task System_health_reports_every_component_without_addresses_or_secrets()
    {
        var app = await fixture.GetAppAsync();

        using var response = await app.Client.GetAsync("/api/system/health", Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, json);
        var health = JsonSerializer.Deserialize<SystemHealthDto>(json, TestJson.Options)!;

        health.Components.Select(c => c.Key).ShouldBe(
        [
            SystemComponents.Api, SystemComponents.Database, SystemComponents.VectorStore, SystemComponents.Jobs, SystemComponents.SignIn,
            SystemComponents.Langfuse, SystemComponents.Models, SystemComponents.Email, SystemComponents.Slack,
        ]);
        var byKey = health.Components.ToDictionary(c => c.Key);
        var inMemory = fixture.Mode == TestMode.InMemory;

        byKey[SystemComponents.Api].Status.ShouldBe(SystemComponentStatus.Healthy);
        byKey[SystemComponents.Database].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.Healthy, c.Detail),
            c => c.Variant.ShouldBe(inMemory ? "inmemory" : "postgres"));
        byKey[SystemComponents.VectorStore].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.Healthy, c.Detail),
            c => c.Variant.ShouldBe(inMemory ? "inmemory" : "clickhouse"));
        if (inMemory)
        {
            byKey[SystemComponents.Database].Detail.ShouldContain("lost when the API restarts");
            byKey[SystemComponents.VectorStore].Detail.ShouldContain("after a restart");
        }

        byKey[SystemComponents.Jobs].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.Healthy, c.Detail),
            c => c.Variant.ShouldBe("inprocess"));
        byKey[SystemComponents.SignIn].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.NotConfigured),
            c => c.Variant.ShouldBe("none"));
        byKey[SystemComponents.Langfuse].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.Healthy, c.Detail),
            c => c.Detail.ShouldBe("Reachable, version fake"));
        byKey[SystemComponents.Models].ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(SystemComponentStatus.Healthy, c.Detail),
            c => c.Detail.ShouldContain(FakeModels.ChatModel));
        byKey[SystemComponents.Email].Status.ShouldBe(SystemComponentStatus.NotConfigured);
        byKey[SystemComponents.Slack].Status.ShouldBe(SystemComponentStatus.NotConfigured);

        json.ShouldContain("\"status\":\"notConfigured\"");
        json.ShouldNotContain(app.Langfuse.BaseAddress.Authority);
        json.ShouldNotContain("://");
        json.ShouldNotContain("password", Case.Insensitive);
        json.ShouldNotContain("Host=", Case.Insensitive);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task System_health_is_cached_for_a_short_while()
    {
        var app = await fixture.GetAppAsync();

        var first = await app.Client.GetJsonAsync<SystemHealthDto>("/api/system/health");
        var second = await app.Client.GetJsonAsync<SystemHealthDto>("/api/system/health");

        second.Components.Select(c => c.CheckedAt).ShouldBe(first.Components.Select(c => c.CheckedAt));
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Providers_endpoint_reports_configuration_and_capabilities()
    {
        var app = await fixture.GetAppAsync();

        var providers = await app.Client.GetJsonAsync<ProviderDto[]>("/api/models/providers");

        providers.Select(p => p.Key).ShouldBe(providers.Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase));
        providers.Select(p => p.Key).ShouldBe(["anthropic", "fake", "ollama", "openai", "voyage"]);

        var fake = providers.Single(p => p.Key == FakeModels.ProviderKey);
        fake.ShouldBe(new ProviderDto(
            FakeModels.ProviderKey,
            FakeAiProvider.TypeName,
            Configured: true,
            SupportsChat: true,
            SupportsEmbeddings: true,
            FakeModels.ChatModel,
            FakeModels.EmbeddingModel,
            DefaultEmbeddingDimensions: 64));

        var anthropic = providers.Single(p => p.Key == "anthropic");
        anthropic.Type.ShouldBe("anthropic");
        anthropic.Configured.ShouldBeFalse("the tests configure no Anthropic API key");
        anthropic.SupportsChat.ShouldBeTrue();
        anthropic.DefaultChatModel.ShouldBe("claude-opus-5-5");

        var voyage = providers.Single(p => p.Key == "voyage");
        voyage.Configured.ShouldBeFalse("the tests configure no Voyage API key");
        voyage.SupportsChat.ShouldBeFalse();
        voyage.SupportsEmbeddings.ShouldBeTrue();
        voyage.SupportsRerank.ShouldBeTrue();
        voyage.DefaultRerankModel.ShouldBe("rerank-2.5");
        anthropic.SupportsRerank.ShouldBeFalse("chat providers rerank only through the LLM prompt");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task OpenApi_document_and_Scalar_reference_are_served()
    {
        var app = await fixture.GetAppAsync();

        using var response = await app.Client.GetAsync("/openapi/v1.json", Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, json);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("openapi").GetString().ShouldNotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("info").GetProperty("title").GetString().ShouldBe("Knowledge Agent API");
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.ShouldContain("/api/sources");
        paths.ShouldContain("/api/sources/{id}");
        paths.ShouldContain("/api/sources/upload");
        paths.ShouldContain("/api/sources/{id}/chunks");
        paths.ShouldContain("/api/search");
        paths.ShouldContain("/api/chat");
        paths.ShouldContain("/api/conversations/{id}");
        paths.ShouldContain("/api/models/profiles/{name}");
        paths.ShouldContain("/api/models/profiles/{name}/override");
        paths.ShouldContain("/api/models/providers");
        paths.ShouldContain("/api/prompts/{name}/render");
        paths.ShouldContain("/api/prompts/render");
        paths.ShouldContain("/api/system/info");
        paths.ShouldContain("/api/system/health");
        paths.ShouldContain("/api/system/public-address");

        using var scalar = await app.Client.GetAsync("/scalar", Ct);
        scalar.StatusCode.ShouldBe(HttpStatusCode.OK);
        scalar.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Cors_allows_the_configured_portal_origin_only()
    {
        var app = await fixture.GetAppAsync();

        using var allowed = await app.Client.SendAsync(Preflight("http://localhost:5173"), Ct);
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(new[] { "http://localhost:5173" });

        using var denied = await app.Client.SendAsync(Preflight("http://evil.example"), Ct);
        denied.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();

        static HttpRequestMessage Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/chat");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type");
            return request;
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task App_starts_serves_a_chat_turn_and_shuts_down_cleanly()
    {
        var app = await fixture.CreateAppAsync();
        try
        {
            var stream = await app.ChatAsync(new ChatRequestDto(null, "Hello before shutdown"));
            stream.Errors.ShouldBeEmpty(stream.Describe());
            await app.StopAsync();

            app.ShutdownErrors.ShouldBeEmpty(string.Join(Environment.NewLine, app.ShutdownErrors));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Unknown_routes_and_malformed_bodies_return_problem_details()
    {
        var app = await fixture.GetAppAsync();

        using var missing = await app.Client.GetAsync("/api/does-not-exist", Ct);
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);

        using var malformed = await app.Client.PostAsync(
            "/api/search",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"),
            Ct);
        var problem = await malformed.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.Title.ShouldNotBeNullOrWhiteSpace();
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
}

public sealed class InMemorySystemScenarios(InMemoryAppFixture fixture) : SystemScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersSystemScenarios(ContainersAppFixture fixture) : SystemScenarios(fixture), IClassFixture<ContainersAppFixture>;
