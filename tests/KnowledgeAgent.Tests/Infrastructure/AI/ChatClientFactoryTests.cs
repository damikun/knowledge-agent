using System.Collections.Concurrent;
using System.Diagnostics;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Telemetry;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Infrastructure.AI;

public sealed class ChatClientFactoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uses_a_provider_registered_in_DI_and_caches_its_client()
    {
        var fake = new FakeAiProvider();
        using var host = CreateHost(fake, ("AI:Profiles:orchestrator:Provider", "fake"));

        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        handle.Profile.ProviderKey.ShouldBe("fake");
        handle.Profile.ProviderType.ShouldBe("fake");
        handle.Profile.Model.ShouldBe("fake-chat");

        var response = await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello there friend")], handle.CreateOptions(), Ct);
        response.Text.ShouldBe("Hello there friend");

        var again = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        again.Client.ShouldBeSameAs(handle.Client);
        fake.CreatedChatProfiles.Count.ShouldBe(1);

        var otherModel = await host.ChatClients.CreateAsync("orchestrator", requestPatch: new ModelProfilePatch(Model: "fake-large"), cancellationToken: Ct);
        otherModel.Client.ShouldNotBeSameAs(handle.Client);
        fake.CreatedChatProfiles.Select(p => p.Model).ShouldBe(["fake-chat", "fake-large"]);
    }

    [Fact]
    public async Task CreateOptions_maps_the_profile_to_fresh_chat_options()
    {
        using var host = CreateHost(
            new FakeAiProvider(),
            ("AI:Profiles:utility:Provider", "fake"),
            ("AI:Profiles:utility:Temperature", "0.3"),
            ("AI:Profiles:utility:MaxOutputTokens", "1234"),
            ("AI:Profiles:utility:ReasoningEffort", "extra_high"),
            ("AI:Profiles:utility:ReasoningOutput", "full"));

        var handle = await host.ChatClients.CreateAsync("utility", cancellationToken: Ct);
        var options = handle.CreateOptions();

        options.ModelId.ShouldBe("fake-chat");
        options.MaxOutputTokens.ShouldBe(1234);
        options.Temperature.ShouldBe(0.3f);
        options.Reasoning.ShouldNotBeNull();
        options.Reasoning.Effort.ShouldBe(ReasoningEffort.ExtraHigh);
        options.Reasoning.Output.ShouldBe(ReasoningOutput.Full);
        options.Tools.ShouldBeNull();
        options.RawRepresentationFactory.ShouldBeNull();

        handle.CreateOptions().ShouldNotBeSameAs(options);
    }

    [Fact]
    public async Task CreateOptions_for_anthropic_drops_sampling_and_sends_an_explicit_effort()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:anthropic:ApiKey", "sk-ant-test"),
            ("AI:Profiles:orchestrator:Provider", "anthropic"),
            ("AI:Profiles:orchestrator:Temperature", "0.7"),
            ("AI:Profiles:orchestrator:ReasoningEffort", null),
            ("AI:Profiles:orchestrator:ReasoningOutput", "summary"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "4096")));

        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();

        handle.Profile.Temperature.ShouldBe(0.7f);
        options.ModelId.ShouldBe("claude-opus-5-5");
        options.MaxOutputTokens.ShouldBe(4096);
        options.Temperature.ShouldBeNull();
        options.Reasoning.ShouldNotBeNull();
        options.Reasoning.Effort.ShouldBe(ReasoningEffort.Medium);
        options.Reasoning.Output.ShouldBe(ReasoningOutput.Summary);

        var high = await host.ChatClients.CreateAsync("orchestrator", requestPatch: new ModelProfilePatch(ReasoningEffort: "high"), cancellationToken: Ct);
        high.CreateOptions().Reasoning!.Effort.ShouldBe(ReasoningEffort.High);

        // Claude Opus 5.5 rejects "none" because it always thinks, so the closest valid effort is used.
        var none = await host.ChatClients.CreateAsync("orchestrator", requestPatch: new ModelProfilePatch(ReasoningEffort: "none"), cancellationToken: Ct);
        none.CreateOptions().Reasoning!.Effort.ShouldBe(ReasoningEffort.Low);
    }

    [Theory]
    [InlineData("gpt-5-mini", true)]
    [InlineData("o4-mini", true)]
    [InlineData("gpt-4.1", false)]
    [InlineData("gpt-5-chat-latest", false)]
    [InlineData("meta-llama/Llama-3.3-70B-Instruct", false)]
    public async Task CreateOptions_for_openai_keeps_either_reasoning_or_temperature(string model, bool reasoningModel)
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:openai:ApiKey", "sk-openai-test"),
            ("AI:Profiles:utility:Provider", "openai"),
            ("AI:Profiles:utility:Model", model),
            ("AI:Profiles:utility:Temperature", "0.2"),
            ("AI:Profiles:utility:ReasoningEffort", "low"),
            ("AI:Profiles:utility:ReasoningOutput", "summary")));

        var options = (await host.ChatClients.CreateAsync("utility", cancellationToken: Ct)).CreateOptions();

        if (reasoningModel)
        {
            options.Temperature.ShouldBeNull();
            options.Reasoning.ShouldNotBeNull().Effort.ShouldBe(ReasoningEffort.Low);
            options.Reasoning.Output.ShouldBeNull();
        }
        else
        {
            options.Temperature.ShouldBe(0.2f);
            options.Reasoning.ShouldBeNull();
        }
    }

    [Fact]
    public async Task CreateOptions_for_ollama_does_not_forward_reasoning()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:ollama:Endpoint", "http://localhost:11434"),
            ("AI:Profiles:utility:Provider", "ollama"),
            ("AI:Profiles:utility:Temperature", "0.5")));

        var options = (await host.ChatClients.CreateAsync("utility", cancellationToken: Ct)).CreateOptions();

        options.ModelId.ShouldBe("qwen3");
        options.Reasoning.ShouldBeNull();
        options.Temperature.ShouldBe(0.5f);
    }

    [Fact]
    public async Task Embedding_profiles_cannot_be_used_for_chat()
    {
        using var host = CreateHost(new FakeAiProvider(), ("AI:Profiles:embedding:Provider", "fake"));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.ChatClients.CreateAsync("embedding", cancellationToken: Ct));

        error.Message.ShouldContain("embedding profile");
    }

    [Fact]
    public async Task Tool_calls_are_returned_to_the_caller_and_never_invoked()
    {
        var scripted = new ScriptedChatClient().ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "refunds" });
        using var host = CreateHost(new FakeAiProvider(_ => scripted), ("AI:Profiles:orchestrator:Provider", "fake"));
        var invoked = false;
        var tool = AIFunctionFactory.Create((string query) => { invoked = true; return "result"; }, ToolNames.SearchKnowledgeBase);

        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [tool];
        var response = await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "refunds?")], options, Ct);

        response.FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ShouldHaveSingleItem();
        invoked.ShouldBeFalse();
        scripted.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Prompt_metadata_is_tagged_on_the_GenAI_span_and_removed_before_the_provider_call()
    {
        var promptName = "orchestrator-system-" + Guid.NewGuid().ToString("N");
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KnowledgeAgentTelemetry.AI,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var scripted = new ScriptedChatClient().ThenText("first answer").ThenText("second answer");
        using var host = CreateHost(new FakeAiProvider(_ => scripted), ("AI:Profiles:orchestrator:Provider", "fake"));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        var options = handle.CreateOptions();
        options.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [PromptTelemetryChatClient.PromptNameKey] = promptName,
            [PromptTelemetryChatClient.PromptVersionKey] = 7,
            ["custom"] = "kept",
        };

        await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options, Ct);
        await foreach (var _ in handle.Client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi again")], options, Ct))
        {
        }

        var spans = stopped.Where(a => Equals(a.GetTagItem(PromptTelemetryChatClient.PromptNameTag), promptName)).ToList();
        spans.Count.ShouldBe(2);
        spans.ShouldAllBe(a => Equals(a.GetTagItem(PromptTelemetryChatClient.PromptVersionTag), 7L));
        spans.ShouldAllBe(a => a.Source.Name == KnowledgeAgentTelemetry.AI);

        scripted.Requests.Count.ShouldBe(2);
        foreach (var request in scripted.Requests)
        {
            var sent = request.Options.ShouldNotBeNull().AdditionalProperties.ShouldNotBeNull();
            sent.ContainsKey(PromptTelemetryChatClient.PromptNameKey).ShouldBeFalse();
            sent.ContainsKey(PromptTelemetryChatClient.PromptVersionKey).ShouldBeFalse();
            sent["custom"].ShouldBe("kept");
        }

        // The caller's options stay untouched because they may be reused for the next iteration.
        options.AdditionalProperties.ContainsKey(PromptTelemetryChatClient.PromptNameKey).ShouldBeTrue();
    }

    [Fact]
    public void Prompt_keys_are_removed_even_without_a_listener()
    {
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [PromptTelemetryChatClient.PromptNameKey] = "p",
                [PromptTelemetryChatClient.PromptVersionKey] = "3",
            },
        };

        var applied = PromptTelemetryChatClient.Apply(options).ShouldNotBeNull();

        applied.ShouldNotBeSameAs(options);
        applied.AdditionalProperties.ShouldBeNull();
        PromptTelemetryChatClient.Apply(null).ShouldBeNull();

        var plain = new ChatOptions();
        PromptTelemetryChatClient.Apply(plain).ShouldBeSameAs(plain);
    }

    internal static AIFunction SearchTool() =>
        AIFunctionFactory.Create(
            (string query, int top_k) => "unused",
            ToolNames.SearchKnowledgeBase,
            "Searches the knowledge base and returns passages tagged [S#].");

    private static AiTestHost CreateHost(FakeAiProvider fake, params (string Key, string? Value)[] values) =>
        AiTestHost.Create(TestConfig.WithFakeProviders().With(values), services => services.AddSingleton<IAiProvider>(fake));
}
