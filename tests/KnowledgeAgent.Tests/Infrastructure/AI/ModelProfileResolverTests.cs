using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Infrastructure.AI;

public sealed class ModelProfileResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_any_keys_neither_chat_nor_embeddings_resolve_and_both_say_what_to_configure()
    {
        using var host = AiTestHost.Create();

        var chat = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct));
        chat.Message.ShouldContain("anthropic (not configured");
        chat.Message.ShouldContain("openai (not configured");
        chat.Message.ShouldContain("ollama (not configured");

        var embedding = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("embedding", cancellationToken: Ct));
        embedding.Message.ShouldContain("openai (not configured");
        embedding.Message.ShouldContain("voyage (not configured");
        embedding.Message.ShouldContain("ollama (not configured");
    }

    [Fact]
    public async Task Auto_prefers_anthropic_for_chat_and_voyage_for_its_embeddings()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:anthropic:ApiKey", "sk-ant-test"),
            ("AI:Providers:voyage:ApiKey", "pa-voyage-test")));

        var chat = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        chat.ProviderKey.ShouldBe("anthropic");
        chat.Model.ShouldBe("claude-opus-5-5");
        chat.Kind.ShouldBe(ModelKind.Chat);
        chat.Source.ShouldBe(ModelProfileSource.Configuration);
        chat.ReasoningEffort.ShouldBe("medium");
        chat.ReasoningOutput.ShouldBe("summary");
        chat.MaxOutputTokens.ShouldBe(16000);
        chat.Dimensions.ShouldBeNull();

        var embedding = await host.Resolver.ResolveAsync("embedding", cancellationToken: Ct);
        embedding.ProviderKey.ShouldBe("voyage");
        embedding.Model.ShouldBe("voyage-4");
        embedding.Kind.ShouldBe(ModelKind.Embedding);
        embedding.Dimensions.ShouldBe(1024);
    }

    [Fact]
    public async Task Claude_alone_has_no_embedding_model()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(("AI:Providers:anthropic:ApiKey", "sk-ant-test")));

        (await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct)).ProviderKey.ShouldBe("anthropic");
        await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("embedding", cancellationToken: Ct));
    }

    [Fact]
    public async Task Auto_uses_openai_for_chat_and_embeddings_when_only_openai_is_configured()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(("AI:Providers:openai:ApiKey", "sk-openai-test")));

        var chat = await host.Resolver.ResolveAsync("utility", cancellationToken: Ct);
        chat.ProviderKey.ShouldBe("openai");
        chat.Model.ShouldBe("gpt-5-mini");

        var embedding = await host.Resolver.ResolveAsync("embedding", cancellationToken: Ct);
        embedding.ProviderKey.ShouldBe("openai");
        embedding.Model.ShouldBe("text-embedding-3-small");
        embedding.Dimensions.ShouldBe(1536);
    }

    [Fact]
    public async Task Auto_uses_ollama_when_only_its_endpoint_is_configured()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(("AI:Providers:ollama:Endpoint", "http://localhost:11434")));

        var chat = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        chat.ProviderKey.ShouldBe("ollama");
        chat.Model.ShouldBe("qwen3");

        var embedding = await host.Resolver.ResolveAsync("embedding", cancellationToken: Ct);
        embedding.ProviderKey.ShouldBe("ollama");
        embedding.Model.ShouldBe("nomic-embed-text");
        embedding.Dimensions.ShouldBeNull();
    }

    [Fact]
    public async Task Auto_follows_the_configured_provider_order_and_skips_unusable_entries()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:anthropic:ApiKey", "sk-ant-test"),
            ("AI:Providers:openai:ApiKey", "sk-openai-test"),
            ("AI:Providers:voyage:ApiKey", "pa-voyage-test"),
            ("AI:AutoChatProviders:0", "missing"),
            ("AI:AutoChatProviders:1", "voyage"),
            ("AI:AutoChatProviders:2", "openai"),
            ("AI:AutoChatProviders:3", "anthropic"),
            ("AI:AutoEmbeddingProviders:0", "anthropic"),
            ("AI:AutoEmbeddingProviders:1", "voyage")));

        (await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct)).ProviderKey.ShouldBe("openai");
        (await host.Resolver.ResolveAsync("embedding", cancellationToken: Ct)).ProviderKey.ShouldBe("voyage");
    }

    [Fact]
    public async Task Auto_without_any_usable_provider_explains_every_candidate()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:AutoChatProviders:0", "anthropic"),
            ("AI:AutoChatProviders:1", "openai"),
            ("AI:AutoChatProviders:2", "nope")));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct));

        error.Message.ShouldContain("'orchestrator' profile (Provider = auto)");
        error.Message.ShouldContain("anthropic (not configured: add an API key under Models → Providers, or set AI__Providers__anthropic__ApiKey (or ANTHROPIC_API_KEY))");
        error.Message.ShouldContain("openai (not configured: add an API key under Models → Providers, or set AI__Providers__openai__ApiKey (or OPENAI_API_KEY)");
        error.Message.ShouldContain("nope (not defined in AI:Providers)");
        error.Message.ShouldContain("switch the 'orchestrator' profile in Models");
    }

    [Fact]
    public async Task Api_keys_and_ollama_endpoint_fall_back_to_the_well_known_variables()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("ANTHROPIC_API_KEY", "sk-ant-from-env"),
            ("OPENAI_API_KEY", "sk-openai-from-env"),
            ("OLLAMA_ENDPOINT", "http://ollama:11434")));

        host.Options.Providers["anthropic"].ApiKey.ShouldBe("sk-ant-from-env");
        host.Options.Providers["openai"].ApiKey.ShouldBe("sk-openai-from-env");
        host.Options.Providers["ollama"].Endpoint.ShouldBe("http://ollama:11434");

        var chat = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        chat.ProviderKey.ShouldBe("anthropic");
    }

    [Fact]
    public void Explicit_provider_settings_win_over_the_fallback_variables()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:anthropic:ApiKey", "sk-ant-explicit"),
            ("ANTHROPIC_API_KEY", "sk-ant-from-env")));

        host.Options.Providers["anthropic"].ApiKey.ShouldBe("sk-ant-explicit");
    }

    [Fact]
    public async Task OpenAI_key_fallback_is_not_sent_to_other_openai_compatible_endpoints()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("OPENAI_API_KEY", "sk-openai-from-env"),
            ("AI:Providers:my-vllm:Type", "openai"),
            ("AI:Providers:my-vllm:Endpoint", "http://localhost:8000/v1"),
            ("AI:Providers:my-vllm:DefaultChatModel", "llama-3.3"),
            ("AI:Profiles:utility:Provider", "my-vllm")));

        host.Options.Providers["openai"].ApiKey.ShouldBe("sk-openai-from-env");
        host.Options.Providers["my-vllm"].ApiKey.ShouldBeNullOrEmpty();

        var profile = await host.Resolver.ResolveAsync("utility", cancellationToken: Ct);
        profile.ProviderKey.ShouldBe("my-vllm");
        profile.ProviderType.ShouldBe("openai");
        profile.Model.ShouldBe("llama-3.3");
    }

    [Fact]
    public async Task Provider_type_defaults_to_the_provider_key_and_lookups_ignore_case()
    {
        var config = TestConfig.Base().With(("AI:Profiles:utility:Provider", "OLLAMA"), ("AI:Providers:ollama:Endpoint", "http://localhost:11434"));
        config.Remove("AI:Providers:ollama:Type");
        using var host = AiTestHost.Create(config);

        var profile = await host.Resolver.ResolveAsync("Utility", cancellationToken: Ct);

        profile.Name.ShouldBe("utility");
        profile.ProviderKey.ShouldBe("ollama");
        profile.ProviderType.ShouldBe("ollama");
    }

    [Fact]
    public async Task Layers_apply_in_order_configuration_override_prompt_request()
    {
        using var host = CreateFakeHost(
            ("AI:Profiles:orchestrator:Provider", "fake"),
            ("AI:Profiles:orchestrator:Model", "m-config"),
            ("AI:Profiles:orchestrator:Temperature", "0.1"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "100"),
            ("AI:Profiles:orchestrator:ReasoningEffort", "low"),
            ("AI:Profiles:orchestrator:ReasoningOutput", null));

        var configured = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        configured.Source.ShouldBe(ModelProfileSource.Configuration);
        configured.Model.ShouldBe("m-config");
        configured.Temperature.ShouldBe(0.1f);
        configured.MaxOutputTokens.ShouldBe(100);
        configured.ReasoningEffort.ShouldBe("low");

        host.Overrides.Set(new ModelProfileOverride { Name = "orchestrator", Provider = "fake", Model = "m-override", MaxOutputTokens = 200 });
        var overridden = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        overridden.Source.ShouldBe(ModelProfileSource.Override);
        overridden.Model.ShouldBe("m-override");
        overridden.MaxOutputTokens.ShouldBe(200);
        overridden.Temperature.ShouldBe(0.1f);
        overridden.ReasoningEffort.ShouldBe("low");

        var promptPatch = new ModelProfilePatch(Model: "m-prompt", ReasoningEffort: "High");
        var prompted = await host.Resolver.ResolveAsync("orchestrator", promptPatch, cancellationToken: Ct);
        prompted.Source.ShouldBe(ModelProfileSource.Prompt);
        prompted.Model.ShouldBe("m-prompt");
        prompted.ReasoningEffort.ShouldBe("high");
        prompted.MaxOutputTokens.ShouldBe(200);

        var requested = await host.Resolver.ResolveAsync("orchestrator", promptPatch, new ModelProfilePatch(Temperature: 0.9f, ReasoningOutput: "summarized"), Ct);
        requested.Source.ShouldBe(ModelProfileSource.Request);
        requested.Model.ShouldBe("m-prompt");
        requested.Temperature.ShouldBe(0.9f);
        requested.ReasoningOutput.ShouldBe("summary");
        requested.ReasoningEffort.ShouldBe("high");
    }

    [Fact]
    public async Task Source_is_the_highest_layer_that_actually_changed_something()
    {
        using var host = CreateFakeHost(("AI:Profiles:utility:Provider", "fake"), ("AI:Profiles:utility:Model", "m-config"));

        host.Overrides.Set(new ModelProfileOverride { Name = "utility", Provider = "FAKE", Model = "m-config" });
        var unchanged = await host.Resolver.ResolveAsync("utility", requestPatch: new ModelProfilePatch(Provider: "fake", Model: "m-config"), cancellationToken: Ct);
        unchanged.Source.ShouldBe(ModelProfileSource.Configuration);

        var prompt = await host.Resolver.ResolveAsync(
            "utility",
            new ModelProfilePatch(MaxOutputTokens: 50),
            new ModelProfilePatch(MaxOutputTokens: 50),
            Ct);
        prompt.Source.ShouldBe(ModelProfileSource.Prompt);
        prompt.MaxOutputTokens.ShouldBe(50);
    }

    [Fact]
    public async Task Switching_provider_without_a_model_resets_to_the_new_provider_default()
    {
        using var host = CreateFakeHost(("AI:Profiles:orchestrator:Provider", "fake"), ("AI:Profiles:orchestrator:Model", "m-config"));

        var prompt = await host.Resolver.ResolveAsync("orchestrator", new ModelProfilePatch(Provider: "fake2"), cancellationToken: Ct);
        prompt.ProviderKey.ShouldBe("fake2");
        prompt.Model.ShouldBe("fake2-chat");
        prompt.Source.ShouldBe(ModelProfileSource.Prompt);

        host.Overrides.Set(new ModelProfileOverride { Name = "orchestrator", Provider = "fake2" });
        var overridden = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);
        overridden.ProviderKey.ShouldBe("fake2");
        overridden.Model.ShouldBe("fake2-chat");
        overridden.Source.ShouldBe(ModelProfileSource.Override);

        var request = await host.Resolver.ResolveAsync("orchestrator", requestPatch: new ModelProfilePatch(Provider: "fake", Model: "m-request"), cancellationToken: Ct);
        request.ProviderKey.ShouldBe("fake");
        request.Model.ShouldBe("m-request");
    }

    [Fact]
    public async Task Override_to_auto_drops_the_configured_model_and_picks_the_first_configured_provider()
    {
        using var host = CreateFakeHost(
            ("AI:Profiles:orchestrator:Provider", "fake"),
            ("AI:Profiles:orchestrator:Model", "m-config"),
            ("AI:Providers:openai:ApiKey", "sk-openai-test"));
        host.Overrides.Set(new ModelProfileOverride { Name = "orchestrator", Provider = "auto" });

        var profile = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);

        profile.ProviderKey.ShouldBe("openai");
        profile.Model.ShouldBe("gpt-5-mini");
        profile.Source.ShouldBe(ModelProfileSource.Override);
    }

    [Fact]
    public async Task Embedding_override_can_change_dimensions_and_a_provider_switch_resets_them()
    {
        using var host = AiTestHost.Create(WithOpenAi());
        host.Overrides.Set(new ModelProfileOverride { Name = "embedding", Provider = "openai", Dimensions = 512 });

        var profile = await host.Resolver.ResolveAsync("embedding", cancellationToken: Ct);
        profile.Dimensions.ShouldBe(512);
        profile.Source.ShouldBe(ModelProfileSource.Override);

        host.Overrides.Set(new ModelProfileOverride { Name = "embedding", Provider = "openai", Dimensions = 0 });
        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("embedding", cancellationToken: Ct));
        error.Message.ShouldContain("invalid embedding dimensions");
    }

    [Fact]
    public async Task Unknown_profile_names_the_configuration_key()
    {
        using var host = AiTestHost.Create();

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("summarizer", cancellationToken: Ct));

        error.Message.ShouldContain("Model profile 'summarizer' is not configured");
        error.Message.ShouldContain("AI:Profiles:summarizer");
        error.Message.ShouldContain("orchestrator");
    }

    [Fact]
    public async Task Unknown_provider_is_reported_with_the_available_ones()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(("AI:Profiles:utility:Provider", "missing")));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("utility", cancellationToken: Ct));

        error.Message.ShouldContain("provider 'missing', which is not defined in AI:Providers");
        error.Message.ShouldContain("anthropic");
        error.Message.ShouldContain("switch the 'utility' profile in Models");
    }

    [Fact]
    public async Task Unconfigured_provider_error_is_actionable()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(("AI:Profiles:orchestrator:Provider", "anthropic")));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct));

        error.Message.ShouldBe(
            "Provider 'anthropic' is not configured: add an API key under Models → Providers, or set AI__Providers__anthropic__ApiKey (or ANTHROPIC_API_KEY), "
            + "or switch the 'orchestrator' profile in Models.");
    }

    [Fact]
    public async Task Unconfigured_ollama_points_to_the_endpoint_settings()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Profiles:utility:Provider", "ollama"),
            ("AI:Providers:ollama:Endpoint", "not a url")));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("utility", cancellationToken: Ct));

        error.Message.ShouldContain("AI__Providers__ollama__Endpoint (or OLLAMA_ENDPOINT)");
    }

    [Fact]
    public async Task Unsupported_type_and_kind_mismatches_are_reported()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:weird:Type", "quantum"),
            ("AI:Profiles:utility:Provider", "weird"),
            ("AI:Providers:anthropic:ApiKey", "sk-ant-test"),
            ("AI:Profiles:embedding:Provider", "anthropic")));

        var typeError = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("utility", cancellationToken: Ct));
        typeError.Message.ShouldContain("type 'quantum', which is not supported");
        typeError.Message.ShouldContain("anthropic, openai, ollama, voyage");

        var kindError = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("embedding", cancellationToken: Ct));
        kindError.Message.ShouldContain("Provider 'anthropic' (anthropic) does not support embeddings");
    }

    [Fact]
    public async Task Missing_model_names_the_default_model_setting()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:openai:ApiKey", "sk-openai-test"),
            ("AI:Providers:openai:DefaultChatModel", ""),
            ("AI:Profiles:utility:Provider", "openai")));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("utility", cancellationToken: Ct));

        error.Message.ShouldContain("AI:Providers:openai:DefaultChatModel");
    }

    [Fact]
    public async Task List_reports_every_configured_profile_with_its_effective_settings_or_error()
    {
        using var host = AiTestHost.Create(WithOpenAi().With(("AI:Profiles:orchestrator:Provider", "anthropic")));
        host.Overrides.Set(new ModelProfileOverride { Name = "utility", Provider = "openai", Model = "gpt-custom" });
        host.Overrides.Set(new ModelProfileOverride { Name = "not-configured", Provider = "openai" });

        var states = await host.Resolver.ListAsync(Ct);

        // Configuration keys enumerate in sorted order, and overrides of unknown profiles are not listed.
        states.Select(s => s.Name).ShouldBe(["embedding", "orchestrator", "researcher", "utility"]);

        var orchestrator = states.Single(s => s.Name == "orchestrator");
        orchestrator.Effective.ShouldBeNull();
        orchestrator.Error.ShouldNotBeNull().ShouldContain("Provider 'anthropic' is not configured");
        orchestrator.Configured.Provider.ShouldBe("anthropic");

        var utility = states.Single(s => s.Name == "utility");
        utility.Error.ShouldBeNull();
        utility.Override.ShouldNotBeNull().Model.ShouldBe("gpt-custom");
        utility.Effective.ShouldNotBeNull().Model.ShouldBe("gpt-custom");
        utility.Effective.Source.ShouldBe(ModelProfileSource.Override);

        var embedding = states.Single(s => s.Name == "embedding");
        embedding.Kind.ShouldBe(ModelKind.Embedding);
        embedding.Effective.ShouldNotBeNull().ProviderKey.ShouldBe("openai");
    }

    [Fact]
    public async Task Overrides_are_read_on_every_call_so_changes_apply_immediately()
    {
        using var host = AiTestHost.Create(WithOpenAi());

        (await host.Resolver.ResolveAsync("utility", cancellationToken: Ct)).Model.ShouldBe("gpt-5-mini");

        host.Overrides.Set(new ModelProfileOverride { Name = "utility", Provider = "openai", Model = "gpt-custom" });
        (await host.Resolver.ResolveAsync("utility", cancellationToken: Ct)).Model.ShouldBe("gpt-custom");

        host.Overrides.Items.Clear();
        (await host.Resolver.ResolveAsync("utility", cancellationToken: Ct)).Model.ShouldBe("gpt-5-mini");
        host.Overrides.Reads.ShouldBe(3);
    }

    [Fact]
    public async Task Configuration_reloads_apply_without_restart()
    {
        using var host = AiTestHost.Create(WithOpenAi());
        (await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct)).ProviderKey.ShouldBe("openai");

        host.Reconfigure(("AI:Providers:anthropic:ApiKey", "sk-ant-reloaded"));

        (await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct)).ProviderKey.ShouldBe("anthropic");
    }

    [Fact]
    public async Task A_failing_override_store_degrades_to_configuration()
    {
        using var host = AiTestHost.Create(WithOpenAi());
        host.Overrides.Failure = new InvalidOperationException("database is down");

        var profile = await host.Resolver.ResolveAsync("utility", cancellationToken: Ct);
        profile.Source.ShouldBe(ModelProfileSource.Configuration);

        var states = await host.Resolver.ListAsync(Ct);
        states.ShouldAllBe(s => s.Override == null);
    }

    [Fact]
    public async Task Works_without_an_override_store()
    {
        using var host = AiTestHost.Create(WithOpenAi(), registerOverrideStore: false);

        var profile = await host.Resolver.ResolveAsync("orchestrator", cancellationToken: Ct);

        profile.ProviderKey.ShouldBe("openai");
        (await host.Resolver.ListAsync(Ct)).Count.ShouldBe(4);
    }

    private static Dictionary<string, string?> WithOpenAi() => TestConfig.Base().With(("AI:Providers:openai:ApiKey", "sk-openai-test"));

    private static AiTestHost CreateFakeHost(params (string Key, string? Value)[] values) =>
        AiTestHost.Create(
            TestConfig.WithFakeProviders().With(values),
            services => services.AddSingleton<IAiProvider>(new FakeAiProvider()));
}
