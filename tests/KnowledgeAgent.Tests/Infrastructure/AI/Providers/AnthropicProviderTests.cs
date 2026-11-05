using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Providers;

public sealed class AnthropicProviderTests : IAsyncLifetime
{
    private const string ApiKey = "sk-ant-test-key";

    private const string TextResponse = """
        {"id":"msg_test_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"thinking","thinking":"The user asks about refunds.","signature":"sig-1"},{"type":"text","text":"Hello from fake Claude"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":42,"output_tokens":7,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
        """;

    private const string StreamingResponse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_test_2","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":42,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Considering refunds."}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-2"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Hello "}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"from fake Claude"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":7}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private CapturingHttpServer _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = await CapturingHttpServer.StartAsync();
        _server.Responder = request => request.Body.Contains("\"stream\":true", StringComparison.Ordinal)
            ? CannedResponse.EventStream(StreamingResponse.Replace("\r\n", "\n", StringComparison.Ordinal))
            : CannedResponse.Json(TextResponse);
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Request_carries_model_limits_effort_adaptive_thinking_tools_fallback_and_caching()
    {
        using var host = AiTestHost.Create(Config());
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];
        options.TopP = 0.9f;

        var response = await handle.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "You are a helpful assistant."), new ChatMessage(ChatRole.User, "What is the refund policy?")],
            options,
            Ct);

        var request = _server.LastRequest;
        request.Method.ShouldBe("POST");
        request.PathAndQuery.ShouldBe("/v1/messages?beta=true");
        request.Header("anthropic-beta").ShouldNotBeNull().ShouldContain("server-side-fallback-2026-07-01");
        ((request.Header("x-api-key") ?? string.Empty) + (request.Header("Authorization") ?? string.Empty)).ShouldContain(ApiKey);

        var body = request.Json;
        body["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        body["max_tokens"]!.GetValue<int>().ShouldBe(4096);
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("adaptive");
        body["thinking"]!["display"]!.GetValue<string>().ShouldBe("summarized");
        body.ContainsKey("temperature").ShouldBeFalse();
        body.ContainsKey("top_p").ShouldBeFalse();
        body.ContainsKey("top_k").ShouldBeFalse();
        AssertNoForcedToolChoice(body);

        var tool = body["tools"]!.AsArray().ShouldHaveSingleItem()!;
        tool["name"]!.GetValue<string>().ShouldBe(ToolNames.SearchKnowledgeBase);
        tool["input_schema"]!["properties"]!["query"].ShouldNotBeNull();

        body["fallbacks"]!.GetValue<string>().ShouldBe("default");
        body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        body["cache_control"]!.AsObject().ContainsKey("ttl").ShouldBeFalse();

        body["system"]![0]!["text"]!.GetValue<string>().ShouldBe("You are a helpful assistant.");
        body["messages"]!.AsArray().ShouldHaveSingleItem()!["role"]!.GetValue<string>().ShouldBe("user");

        response.Text.ShouldBe("Hello from fake Claude");
        response.FinishReason.ShouldBe(ChatFinishReason.Stop);
        response.Messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>().ShouldHaveSingleItem().Text.ShouldBe("The user asks about refunds.");
        response.Usage.ShouldNotBeNull().InputTokenCount.ShouldBe(42);
    }

    [Fact]
    public async Task Streaming_request_has_the_same_shape_and_streams_reasoning_and_text()
    {
        using var host = AiTestHost.Create(Config());
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in handle.Client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Refunds?")], handle.CreateOptions(), Ct))
        {
            updates.Add(update);
        }

        var body = _server.LastRequest.Json;
        body["stream"]!.GetValue<bool>().ShouldBeTrue();
        body["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("high");
        body["fallbacks"]!.GetValue<string>().ShouldBe("default");
        body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        body.ContainsKey("temperature").ShouldBeFalse();

        updates.Count(u => u.Contents.OfType<TextContent>().Any(t => t.Text.Length > 0)).ShouldBe(2);
        var response = updates.ToChatResponse();
        response.Text.ShouldBe("Hello from fake Claude");
        response.FinishReason.ShouldBe(ChatFinishReason.Stop);
        string.Concat(updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Select(r => r.Text)).ShouldBe("Considering refunds.");
    }

    [Fact]
    public async Task Disabling_the_refusal_fallback_uses_the_GA_endpoint_and_keeps_caching()
    {
        using var host = AiTestHost.Create(Config(("AI:Providers:anthropic:Settings:RefusalFallback", "none")));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], handle.CreateOptions(), Ct);

        var request = _server.LastRequest;
        request.PathAndQuery.ShouldBe("/v1/messages");
        (request.Header("anthropic-beta") ?? string.Empty).ShouldNotContain("server-side-fallback");
        var body = request.Json;
        body.ContainsKey("fallbacks").ShouldBeFalse();
        body["cache_control"]!["type"]!.GetValue<string>().ShouldBe("ephemeral");
        body["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        body["max_tokens"]!.GetValue<int>().ShouldBe(4096);
    }

    [Fact]
    public async Task Named_fallback_models_use_the_array_form_and_its_beta_header()
    {
        using var host = AiTestHost.Create(Config(
            ("AI:Providers:anthropic:Settings:RefusalFallback", "claude-opus-4-8, claude-opus-5"),
            ("AI:Providers:anthropic:Settings:PromptCaching", "1h")));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], handle.CreateOptions(), Ct);

        var request = _server.LastRequest;
        request.Header("anthropic-beta").ShouldNotBeNull().ShouldContain("server-side-fallback-2026-06-01");
        var fallbacks = request.Json["fallbacks"]!.AsArray();
        fallbacks.Select(f => f!["model"]!.GetValue<string>()).ShouldBe(["claude-opus-4-8", "claude-opus-5"]);
        request.Json["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");
    }

    [Fact]
    public async Task Prompt_caching_can_be_turned_off()
    {
        using var host = AiTestHost.Create(Config(("AI:Providers:anthropic:Settings:PromptCaching", "none")));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], handle.CreateOptions(), Ct);

        _server.LastRequest.Json.ContainsKey("cache_control").ShouldBeFalse();
        _server.LastRequest.Json["fallbacks"]!.GetValue<string>().ShouldBe("default");
    }

    [Fact]
    public async Task Forced_tool_choice_disabled_thinking_and_sampling_set_by_callers_are_corrected()
    {
        using var host = AiTestHost.Create(Config());
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = new ChatOptions
        {
            ModelId = "claude-opus-5-5",
            MaxOutputTokens = 1000,
            Temperature = 0.2f,
            TopK = 5,
            ToolMode = ChatToolMode.RequireAny,
            Tools = [ChatClientFactoryTests.SearchTool()],
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
        };

        await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options, Ct);

        var body = _server.LastRequest.Json;
        AssertNoForcedToolChoice(body);
        body["thinking"]!["type"]!.GetValue<string>().ShouldBe("adaptive");
        body["output_config"]!["effort"]!.GetValue<string>().ShouldBe("low");
        body["max_tokens"]!.GetValue<int>().ShouldBe(1000);
        body.ContainsKey("temperature").ShouldBeFalse();
        body.ContainsKey("top_k").ShouldBeFalse();

        options.ToolMode.ShouldBe(ChatToolMode.RequireAny);
        options.Reasoning!.Effort.ShouldBe(ReasoningEffort.None);
        options.Temperature.ShouldBe(0.2f);
    }

    [Fact]
    public async Task Refusals_surface_as_content_filter()
    {
        _server.Responder = _ => CannedResponse.Json("""
            {"id":"msg_test_3","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],
             "stop_reason":"refusal","stop_sequence":null,"stop_details":{"type":"refusal","category":"cyber","explanation":"declined"},
             "usage":{"input_tokens":10,"output_tokens":0}}
            """);
        using var host = AiTestHost.Create(Config());
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        var response = await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], handle.CreateOptions(), Ct);

        response.FinishReason.ShouldBe(ChatFinishReason.ContentFilter);
    }

    [Fact]
    public async Task Tool_use_after_a_server_side_fallback_maps_to_function_calls()
    {
        _server.Responder = _ => CannedResponse.Json("""
            {"id":"msg_test_4","type":"message","role":"assistant","model":"claude-opus-4-8",
             "content":[{"type":"fallback","from":{"model":"claude-opus-5-5"},"to":{"model":"claude-opus-4-8"}},
                        {"type":"text","text":"Let me search."},
                        {"type":"tool_use","id":"toolu_1","name":"search_knowledge_base","input":{"query":"refunds","top_k":4}}],
             "stop_reason":"tool_use","stop_sequence":null,
             "usage":{"input_tokens":10,"output_tokens":5,"iterations":[
                {"type":"message","model":"claude-opus-5-5","input_tokens":10,"output_tokens":0},
                {"type":"fallback_message","model":"claude-opus-4-8","input_tokens":10,"output_tokens":5}]}}
            """);
        using var host = AiTestHost.Create(Config());
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];

        var response = await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "refunds?")], options, Ct);

        response.FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        response.ModelId.ShouldBe("claude-opus-4-8");
        var call = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ShouldHaveSingleItem();
        call.CallId.ShouldBe("toolu_1");
        call.Name.ShouldBe(ToolNames.SearchKnowledgeBase);
        call.Arguments.ShouldNotBeNull().ContainsKey("query").ShouldBeTrue();
    }

    [Fact]
    public void Base_url_is_always_explicit_so_ambient_settings_cannot_redirect_the_app()
    {
        var settings = AnthropicSettings.From(new AiProviderOptions());

        AnthropicAiProvider.CreateAnthropicClient(new AiProviderOptions { ApiKey = ApiKey }, settings).BaseUrl.ShouldBe("https://api.anthropic.com");
        AnthropicAiProvider.CreateAnthropicClient(new AiProviderOptions { ApiKey = ApiKey, Endpoint = "https://gateway.example.test/anthropic" }, settings)
            .BaseUrl.ShouldBe("https://gateway.example.test/anthropic");
    }

    [Fact]
    public void Settings_parse_fallback_caching_and_thinking_mode()
    {
        var defaults = AnthropicSettings.From(new AiProviderOptions());
        defaults.RefusalFallback.ShouldBe(RefusalFallbackMode.Default);
        defaults.UsesBetaEndpoint.ShouldBeTrue();
        defaults.PromptCaching.ShouldBeTrue();
        defaults.OneHourCache.ShouldBeFalse();
        defaults.ThinkingMode.ShouldBe(AnthropicThinkingMode.Adaptive);

        var custom = AnthropicSettings.From(Options(
            ("refusalfallback", "NONE"),
            ("PromptCaching", "off"),
            ("ThinkingMode", "extended"),
            ("MaxRetries", "0"),
            ("TimeoutSeconds", "30")));
        custom.RefusalFallback.ShouldBe(RefusalFallbackMode.None);
        custom.UsesBetaEndpoint.ShouldBeFalse();
        custom.PromptCaching.ShouldBeFalse();
        custom.ThinkingMode.ShouldBe(AnthropicThinkingMode.Extended);
        custom.MaxRetries.ShouldBe(0);
        custom.Timeout.ShouldBe(TimeSpan.FromSeconds(30));

        var models = AnthropicSettings.From(Options(("RefusalFallback", "claude-opus-4-8;claude-opus-5 claude-opus-4-8, a, b")));
        models.RefusalFallback.ShouldBe(RefusalFallbackMode.Models);
        models.FallbackModels.ShouldBe(["claude-opus-4-8", "claude-opus-5", "a"]);
    }

    [Fact]
    public void A_caller_supplied_raw_representation_factory_is_left_alone()
    {
        var provider = new AnthropicAiProvider(NullLogger<AnthropicAiProvider>.Instance);
        var profile = Profile();
        using var client = provider.CreateChatClient(new AiProviderOptions { Type = "anthropic", ApiKey = ApiKey, Endpoint = _server.BaseAddress.ToString() }, profile);
        var claude = client.ShouldBeOfType<ClaudeChatClient>();
        Func<IChatClient, object?> factory = _ => null;

        claude.Prepare(new ChatOptions { RawRepresentationFactory = factory }).RawRepresentationFactory.ShouldBeSameAs(factory);
        claude.Prepare(null).RawRepresentationFactory.ShouldNotBeNull();
    }

    [Fact]
    public void Requires_an_api_key_and_offers_no_embeddings()
    {
        var provider = new AnthropicAiProvider(NullLogger<AnthropicAiProvider>.Instance);

        provider.IsConfigured(new AiProviderOptions { Type = "anthropic" }).ShouldBeFalse();
        provider.IsConfigured(new AiProviderOptions { Type = "anthropic", ApiKey = ApiKey }).ShouldBeTrue();
        provider.SupportsEmbeddings.ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => provider.CreateEmbeddingGenerator(new AiProviderOptions { ApiKey = ApiKey }, Profile()));
        provider.DescribeMissingConfiguration("anthropic", new AiProviderOptions { Type = "anthropic" })
            .ShouldBe("add an API key under Models → Providers, or set AI__Providers__anthropic__ApiKey (or ANTHROPIC_API_KEY)");
        provider.DescribeMissingConfiguration("claude-proxy", new AiProviderOptions { Type = "anthropic", Endpoint = "https://proxy.example.test" })
            .ShouldBe("add an API key under Models → Providers, or set AI__Providers__claude-proxy__ApiKey");
    }

    private Dictionary<string, string?> Config(params (string Key, string? Value)[] extra) => TestConfig.Base()
        .With(
            ("AI:Providers:anthropic:ApiKey", ApiKey),
            ("AI:Providers:anthropic:Endpoint", _server.BaseAddress.ToString()),
            ("AI:Providers:anthropic:Settings:MaxRetries", "0"),
            ("AI:Profiles:orchestrator:Provider", "anthropic"),
            ("AI:Profiles:orchestrator:ReasoningEffort", "high"),
            ("AI:Profiles:orchestrator:ReasoningOutput", "summary"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "4096"),
            ("AI:Profiles:orchestrator:Temperature", "0.4"))
        .With(extra);

    private static void AssertNoForcedToolChoice(JsonObject body)
    {
        if (body["tool_choice"] is JsonObject toolChoice)
        {
            toolChoice["type"]!.GetValue<string>().ShouldBe("auto");
        }
    }

    private static AiProviderOptions Options(params (string Key, string Value)[] settings)
    {
        var options = new AiProviderOptions { Type = "anthropic" };
        foreach (var (key, value) in settings)
        {
            options.Settings[key] = value;
        }

        return options;
    }

    private static ResolvedModelProfile Profile() => new()
    {
        Name = "orchestrator",
        Kind = ModelKind.Chat,
        ProviderKey = "anthropic",
        ProviderType = "anthropic",
        Model = "claude-opus-5-5",
        Source = ModelProfileSource.Configuration,
    };
}
