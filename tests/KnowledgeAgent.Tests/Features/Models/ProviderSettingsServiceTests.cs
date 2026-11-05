using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Providers.OpenAI;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Models;

public sealed class ProviderSettingsServiceTests : IDisposable
{
    private const string AnthropicKey = "sk-ant-api03-test-key-0123456789abcdef";

    private readonly InMemoryProviderSettingsState _saved = new();
    private readonly AiTestHost _host;

    public ProviderSettingsServiceTests() => _host = CreateHost(TestConfig.Base());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IProviderSettingsService Service => _host.Get<IProviderSettingsService>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Lists_providers_with_their_capabilities_and_no_key()
    {
        var providers = await Service.ListAsync(Ct);

        providers.Select(p => p.Key).ShouldBe(["anthropic", "fake", "ollama", "openai", "voyage"]);
        var anthropic = providers.Single(p => p.Key == "anthropic");
        anthropic.Configured.ShouldBeFalse();
        anthropic.UsesApiKey.ShouldBeTrue();
        anthropic.UsesEndpoint.ShouldBeTrue();
        anthropic.ApiKeySource.ShouldBe("none");
        anthropic.ApiKeyHint.ShouldBeNull();
        anthropic.HasOverride.ShouldBeFalse();

        var ollama = providers.Single(p => p.Key == "ollama");
        ollama.UsesApiKey.ShouldBeFalse();
        ollama.EndpointRequired.ShouldBeTrue();

        var voyage = providers.Single(p => p.Key == "voyage");
        voyage.Configured.ShouldBeFalse();
        voyage.UsesApiKey.ShouldBeTrue();

        var fake = providers.Single(p => p.Key == "fake");
        fake.Configured.ShouldBeTrue();
        fake.UsesApiKey.ShouldBeFalse();
        fake.UsesEndpoint.ShouldBeFalse();
    }

    [Fact]
    public async Task A_saved_key_is_encrypted_masked_and_used_by_auto_profiles_immediately()
    {
        await Should.ThrowAsync<ModelNotConfiguredException>(() => _host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct));

        var updated = await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: $"  {AnthropicKey}  "), Ct);

        updated.ShouldNotBeNull();
        updated.Configured.ShouldBeTrue();
        updated.ApiKeySource.ShouldBe("portal");
        updated.ApiKeyHint.ShouldBe("…cdef");
        updated.HasOverride.ShouldBeTrue();
        updated.OverrideUpdatedAt.ShouldNotBeNull();

        var stored = _saved.Items["anthropic"];
        stored.ProtectedApiKey.ShouldNotBeNullOrWhiteSpace();
        stored.ProtectedApiKey.ShouldNotContain(AnthropicKey);
        stored.ApiKeyHint.ShouldBe("…cdef");

        var orchestrator = await _host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        orchestrator.ProviderKey.ShouldBe("anthropic");
        orchestrator.Model.ShouldBe("claude-opus-5-5");

        var effective = await _host.Get<IAiOptionsSource>().GetCurrentAsync(Ct);
        effective.Providers["anthropic"].ApiKey.ShouldBe(AnthropicKey);
        _host.Options.Providers["anthropic"].ApiKey.ShouldBeNullOrEmpty("configuration itself must stay untouched");
    }

    [Fact]
    public async Task Saving_other_fields_keeps_the_key_and_clearing_removes_it()
    {
        await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey), Ct);

        var kept = await Service.UpdateAsync("anthropic", new UpdateProviderRequest(DefaultChatModel: "claude-sonnet-5-5"), Ct);
        kept!.ApiKeySource.ShouldBe("portal");
        kept.DefaultChatModel.ShouldBe("claude-sonnet-5-5");

        var cleared = await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ClearApiKey: true, DefaultChatModel: "claude-sonnet-5-5"), Ct);
        cleared!.ApiKeySource.ShouldBe("none");
        cleared.Configured.ShouldBeFalse();
        cleared.HasOverride.ShouldBeTrue("the default model is still saved");

        var empty = await Service.UpdateAsync("anthropic", new UpdateProviderRequest(), Ct);
        empty!.HasOverride.ShouldBeFalse("a save without any value removes the row");
        empty.DefaultChatModel.ShouldBe("claude-opus-5-5");
        _saved.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_restores_configuration()
    {
        await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey, Endpoint: "https://proxy.example.com"), Ct);

        var reset = await Service.ResetAsync("ANTHROPIC", Ct);

        reset.ShouldNotBeNull();
        reset.Key.ShouldBe("anthropic");
        reset.HasOverride.ShouldBeFalse();
        reset.Endpoint.ShouldBeNull();
        reset.Configured.ShouldBeFalse();
        _saved.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_configured_key_is_reported_and_a_portal_key_wins_over_it()
    {
        using var host = CreateHost(TestConfig.Base().With(("AI:Providers:anthropic:ApiKey", "sk-ant-from-configuration-9999")));
        var service = host.Get<IProviderSettingsService>();

        var configured = (await service.ListAsync(Ct)).Single(p => p.Key == "anthropic");
        configured.ApiKeySource.ShouldBe("configuration");
        configured.ApiKeyHint.ShouldBe("…9999");
        configured.Configured.ShouldBeTrue();

        var portal = await service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey), Ct);
        portal!.ApiKeySource.ShouldBe("portal");
        (await host.Get<IAiOptionsSource>().GetCurrentAsync(Ct)).Providers["anthropic"].ApiKey.ShouldBe(AnthropicKey);

        var back = await service.UpdateAsync("anthropic", new UpdateProviderRequest(ClearApiKey: true), Ct);
        back!.ApiKeySource.ShouldBe("configuration");
        back.ApiKeyHint.ShouldBe("…9999");
    }

    [Fact]
    public async Task A_saved_ollama_endpoint_makes_ollama_usable()
    {
        var updated = await Service.UpdateAsync("ollama", new UpdateProviderRequest(Endpoint: " http://localhost:11434 ", DefaultEmbeddingDimensions: 768), Ct);

        updated!.Configured.ShouldBeTrue();
        updated.Endpoint.ShouldBe("http://localhost:11434");
        updated.DefaultEmbeddingDimensions.ShouldBe(768);
        (await _host.Get<IAiOptionsSource>().GetCurrentAsync(Ct)).Providers["ollama"].Endpoint.ShouldBe("http://localhost:11434");
    }

    [Theory]
    [InlineData("anthropic", "sk ant with spaces", null, null, "apiKey")]
    [InlineData("anthropic", "   ", null, null, "apiKey")]
    [InlineData("fake", "sk-ant-api03-test-key-0123456789abcdef", null, null, "apiKey")]
    [InlineData("anthropic", null, "not a url", null, "endpoint")]
    [InlineData("anthropic", null, "ftp://example.com", null, "endpoint")]
    [InlineData("fake", null, "http://localhost:1234", null, "endpoint")]
    [InlineData("anthropic", null, null, "claude opus", "defaultChatModel")]
    public async Task Rejects_invalid_values_with_field_errors(string provider, string? apiKey, string? endpoint, string? chatModel, string field)
    {
        var error = await Should.ThrowAsync<ValidationFailedException>(
            () => Service.UpdateAsync(provider, new UpdateProviderRequest(ApiKey: apiKey, Endpoint: endpoint, DefaultChatModel: chatModel), Ct));

        error.Errors.Keys.ShouldContain(field);
        _saved.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejects_fields_a_provider_does_not_have()
    {
        var error = await Should.ThrowAsync<ValidationFailedException>(() => Service.UpdateAsync(
            "anthropic",
            new UpdateProviderRequest(ApiKey: AnthropicKey, ClearApiKey: true, DefaultEmbeddingModel: "voyage-3", DefaultEmbeddingDimensions: 1024),
            Ct));

        error.Errors.Keys.ShouldBe(["apiKey", "defaultEmbeddingModel", "defaultEmbeddingDimensions"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_providers_return_null()
    {
        (await Service.UpdateAsync("nope", new UpdateProviderRequest(ApiKey: AnthropicKey), Ct)).ShouldBeNull();
        (await Service.ResetAsync("nope", Ct)).ShouldBeNull();
        (await Service.TestAsync("nope", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task An_undecryptable_saved_key_counts_as_missing()
    {
        _saved.Items["anthropic"] = new ProviderSettingsOverride { Key = "anthropic", ProtectedApiKey = "not-a-protected-payload", ApiKeyHint = "…zzzz" };

        var anthropic = (await Service.ListAsync(Ct)).Single(p => p.Key == "anthropic");

        anthropic.Configured.ShouldBeFalse();
        anthropic.ApiKeySource.ShouldBe("none");
        await Should.ThrowAsync<ModelNotConfiguredException>(() => _host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct));
    }

    [Fact]
    public async Task A_failing_store_falls_back_to_configuration()
    {
        using var host = CreateHost(TestConfig.Base().With(("AI:Providers:openai:ApiKey", "sk-openai-test")));
        _saved.Failure = new InvalidOperationException("database down");

        (await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct)).ProviderKey.ShouldBe("openai");
        (await host.Get<IProviderSettingsService>().ListAsync(Ct)).ShouldContain(p => p.Key == "anthropic" && !p.HasOverride);
    }

    [Fact]
    public async Task Testing_an_unconfigured_provider_explains_what_is_missing()
    {
        var result = await Service.TestAsync("anthropic", Ct);

        result.ShouldNotBeNull();
        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("Models → Providers");
    }

    [Fact]
    public async Task Providers_without_a_connection_pass_the_test()
    {
        var result = await Service.TestAsync("fake", Ct);

        result!.Ok.ShouldBeTrue();
        result.Models.ShouldBeEmpty();
    }

    [Fact]
    public async Task Anthropic_test_lists_models_with_the_saved_key()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json("""
            {"data":[
              {"type":"model","id":"claude-sonnet-5-5","display_name":"Claude Sonnet 5.5","created_at":"2026-08-01T00:00:00Z"},
              {"type":"model","id":"claude-opus-5-5","display_name":"Claude Opus 5.5","created_at":"2026-09-01T00:00:00Z"}
            ],"has_more":false,"first_id":"claude-opus-5-5","last_id":"claude-sonnet-5-5"}
            """);
        await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey, Endpoint: server.BaseAddress.ToString().TrimEnd('/')), Ct);

        var result = await Service.TestAsync("anthropic", Ct);

        result!.Ok.ShouldBeTrue(result.Message);
        result.Models.ShouldBe(["claude-opus-5-5", "claude-sonnet-5-5"]);
        server.LastRequest.Method.ShouldBe("GET");
        server.LastRequest.PathAndQuery.ShouldStartWith("/v1/models");
        ((server.LastRequest.Header("x-api-key") ?? string.Empty) + (server.LastRequest.Header("Authorization") ?? string.Empty))
            .ShouldContain(AnthropicKey);
    }

    [Fact]
    public async Task A_rejected_key_fails_the_test_without_echoing_the_key()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json(
            $$$"""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key {{{AnthropicKey}}}"}}""",
            status: 401);
        await Service.UpdateAsync("anthropic", new UpdateProviderRequest(ApiKey: AnthropicKey, Endpoint: server.BaseAddress.ToString().TrimEnd('/')), Ct);

        var result = await Service.TestAsync("anthropic", Ct);

        result!.Ok.ShouldBeFalse();
        result.Message.ShouldNotContain(AnthropicKey);
        result.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenAI_compatible_servers_report_all_their_models()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json("""
            {"object":"list","data":[
              {"id":"llama-3.3-70b","object":"model","created":1,"owned_by":"local"},
              {"id":"bge-m3","object":"model","created":1,"owned_by":"local"}
            ]}
            """);
        await Service.UpdateAsync("openai", new UpdateProviderRequest(Endpoint: server.BaseAddress + "v1"), Ct);

        var result = await Service.TestAsync("openai", Ct);

        result!.Ok.ShouldBeTrue(result.Message);
        result.Models.ShouldBe(["bge-m3", "llama-3.3-70b"]);
        server.LastRequest.PathAndQuery.ShouldBe("/v1/models");
    }

    [Theory]
    [InlineData("gpt-5-mini", true)]
    [InlineData("o4-mini", true)]
    [InlineData("text-embedding-3-small", true)]
    [InlineData("chatgpt-4o-latest", true)]
    [InlineData("dall-e-3", false)]
    [InlineData("whisper-1", false)]
    [InlineData("omni-moderation-latest", false)]
    public void Official_OpenAI_model_suggestions_keep_chat_and_embedding_families(string model, bool expected) =>
        OpenAiProvider.IsChatOrEmbeddingModel(model).ShouldBe(expected);

    // The fake provider stands for one with no key, endpoint or connection test, which no built-in provider is.
    private AiTestHost CreateHost(Dictionary<string, string?> configuration) =>
        AiTestHost.Create(configuration.With(("AI:Providers:fake:Type", FakeAiProvider.TypeName)), services =>
        {
            services.AddSingleton<IAiProvider>(new FakeAiProvider());
            services.AddSingleton(_saved);
            services.AddScoped<IProviderSettingsStore, InMemoryProviderSettingsStore>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
}
