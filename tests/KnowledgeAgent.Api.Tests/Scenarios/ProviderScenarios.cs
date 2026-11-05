using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.SystemInfo;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ProviderScenarios(AppFixture fixture)
{
    private const string AnthropicKey = "sk-ant-api03-integration-test-key-9f8e7d6c";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Providers_report_capabilities_and_key_state()
    {
        var app = await fixture.GetAppAsync();
        await ResetAsync(app);

        var providers = await app.Client.GetJsonAsync<ProviderDto[]>("/api/models/providers");

        var anthropic = providers.Single(p => p.Key == "anthropic");
        anthropic.Configured.ShouldBeFalse();
        anthropic.UsesApiKey.ShouldBeTrue();
        anthropic.ApiKeySource.ShouldBe("none");
        anthropic.HasOverride.ShouldBeFalse();
        providers.Single(p => p.Key == "ollama").EndpointRequired.ShouldBeTrue();
        providers.Single(p => p.Key == FakeModels.ProviderKey).Configured.ShouldBeTrue();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_key_saved_in_the_portal_makes_the_provider_usable_without_ever_being_returned()
    {
        var app = await fixture.GetAppAsync();
        await ResetAsync(app);

        try
        {
            using var put = await app.Client.PutJsonAsync("/api/models/providers/anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey));
            var body = await put.Content.ReadAsStringAsync(Ct);
            put.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            body.ShouldNotContain(AnthropicKey);
            var saved = await put.ReadJsonAsync<ProviderDto>();
            saved.Configured.ShouldBeTrue();
            saved.ApiKeySource.ShouldBe("portal");
            saved.ApiKeyHint.ShouldBe("…7d6c");

            (await app.Client.GetStringAsync("/api/models/providers", Ct)).ShouldNotContain(AnthropicKey);
            (await app.Client.GetStringAsync("/api/system/info", Ct)).ShouldNotContain(AnthropicKey);
            var info = await app.Client.GetJsonAsync<SystemInfoDto>("/api/system/info");
            info.Providers.Single(p => p.Key == "anthropic").Configured.ShouldBeTrue();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var stored = await scope.ServiceProvider.GetRequiredService<IProviderSettingsStore>().GetAsync("anthropic", Ct);
                stored.ShouldNotBeNull();
                stored.ProtectedApiKey.ShouldNotBeNullOrWhiteSpace();
                stored.ProtectedApiKey.ShouldNotContain(AnthropicKey);
            }

            using var profilePut = await app.Client.PutJsonAsync("/api/models/profiles/orchestrator", new UpdateModelProfileRequest("anthropic"));
            var profile = await profilePut.ReadJsonAsync<ModelProfileDto>();
            profile.Error.ShouldBeNull();
            var effective = profile.Effective.ShouldNotBeNull();
            effective.ProviderKey.ShouldBe("anthropic");
            effective.Model.ShouldBe("claude-opus-5-5");

            using var reset = await app.Client.DeleteAsync("/api/models/providers/anthropic/override", Ct);
            var restored = await reset.ReadJsonAsync<ProviderDto>();
            restored.Configured.ShouldBeFalse();
            restored.HasOverride.ShouldBeFalse();

            var afterReset = (await app.Client.GetJsonAsync<ModelProfileDto[]>("/api/models/profiles")).Single(p => p.Name == "orchestrator");
            afterReset.Effective.ShouldBeNull();
            afterReset.Error.ShouldNotBeNull().ShouldContain("Models → Providers");
        }
        finally
        {
            await ResetAsync(app);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Saving_other_fields_keeps_the_key_until_it_is_cleared()
    {
        var app = await fixture.GetAppAsync();
        await ResetAsync(app);

        try
        {
            (await (await app.Client.PutJsonAsync("/api/models/providers/anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey)))
                .ReadJsonAsync<ProviderDto>()).ApiKeySource.ShouldBe("portal");

            var kept = await (await app.Client.PutJsonAsync(
                "/api/models/providers/anthropic",
                new UpdateProviderRequest(DefaultChatModel: "claude-sonnet-5-5"))).ReadJsonAsync<ProviderDto>();
            kept.ApiKeySource.ShouldBe("portal");
            kept.DefaultChatModel.ShouldBe("claude-sonnet-5-5");

            var cleared = await (await app.Client.PutJsonAsync(
                "/api/models/providers/anthropic",
                new UpdateProviderRequest(ClearApiKey: true, DefaultChatModel: "claude-sonnet-5-5"))).ReadJsonAsync<ProviderDto>();
            cleared.ApiKeySource.ShouldBe("none");
            cleared.Configured.ShouldBeFalse();
            cleared.DefaultChatModel.ShouldBe("claude-sonnet-5-5");
        }
        finally
        {
            await ResetAsync(app);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_settings_and_unknown_providers_are_rejected()
    {
        var app = await fixture.GetAppAsync();
        await ResetAsync(app);

        await ExpectInvalidAsync(app, "anthropic", new UpdateProviderRequest(ApiKey: "has spaces in it"), "apiKey");
        await ExpectInvalidAsync(app, "anthropic", new UpdateProviderRequest(Endpoint: "not-a-url"), "endpoint");
        await ExpectInvalidAsync(app, FakeModels.ProviderKey, new UpdateProviderRequest(ApiKey: AnthropicKey), "apiKey");
        await ExpectInvalidAsync(app, "anthropic", new UpdateProviderRequest(DefaultEmbeddingModel: "voyage-3"), "defaultEmbeddingModel");

        using var unknownPut = await app.Client.PutJsonAsync("/api/models/providers/nope", new UpdateProviderRequest(ApiKey: AnthropicKey));
        await unknownPut.ReadProblemAsync(HttpStatusCode.NotFound);
        using var unknownReset = await app.Client.DeleteAsync("/api/models/providers/nope/override", Ct);
        await unknownReset.ReadProblemAsync(HttpStatusCode.NotFound);
        using var unknownTest = await app.Client.PostAsync("/api/models/providers/nope/test", content: null, Ct);
        await unknownTest.ReadProblemAsync(HttpStatusCode.NotFound);

        (await app.Client.GetJsonAsync<ProviderDto[]>("/api/models/providers")).ShouldAllBe(p => !p.HasOverride);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Connection_tests_explain_missing_settings_and_pass_for_keyless_providers()
    {
        var app = await fixture.GetAppAsync();
        await ResetAsync(app);

        using var missing = await app.Client.PostAsync("/api/models/providers/anthropic/test", content: null, Ct);
        var missingResult = await missing.ReadJsonAsync<ProviderTestResultDto>();
        missingResult.Key.ShouldBe("anthropic");
        missingResult.Ok.ShouldBeFalse();
        missingResult.Message.ShouldContain("Models → Providers");

        using var keyless = await app.Client.PostAsync($"/api/models/providers/{FakeModels.ProviderKey}/test", content: null, Ct);
        (await keyless.ReadJsonAsync<ProviderTestResultDto>()).Ok.ShouldBeTrue();
    }

    private static async Task ExpectInvalidAsync(KnowledgeAgentApp app, string provider, UpdateProviderRequest request, string field)
    {
        using var response = await app.Client.PutJsonAsync($"/api/models/providers/{provider}", request);
        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.FieldErrors.ShouldContainKey(field);
    }

    private static async Task ResetAsync(KnowledgeAgentApp app)
    {
        foreach (var url in new[] { "/api/models/providers/anthropic/override", "/api/models/providers/openai/override", "/api/models/providers/ollama/override", "/api/models/profiles/orchestrator/override" })
        {
            using var response = await app.Client.DeleteAsync(url, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }
    }
}

public sealed class InMemoryProviderScenarios(InMemoryAppFixture fixture) : ProviderScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersProviderScenarios(ContainersAppFixture fixture) : ProviderScenarios(fixture), IClassFixture<ContainersAppFixture>;
