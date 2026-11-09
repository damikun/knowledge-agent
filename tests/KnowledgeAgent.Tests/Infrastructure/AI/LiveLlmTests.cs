using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Infrastructure.AI;

public sealed class LiveLlmTests
{
    private const string ToolResult = """
        Found 1 passage(s) for "X100 warranty".

        [S1] X100 Drill Manual (page 12) | score 0.93 | source_id=6f1c2a9e-0d4b-4c51-9a77-2b8f3c1d5e01
        The X100 cordless drill comes with a 3-year limited warranty for registered owners.
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool LiveEnabled => Environment.GetEnvironmentVariable("RUN_LIVE_LLM_TESTS") is "1" or "true";

    // A cheaper model for routine live runs, e.g. LIVE_ANTHROPIC_MODEL=claude-haiku-5-5.
    private static string AnthropicModel => Environment.GetEnvironmentVariable("LIVE_ANTHROPIC_MODEL") is { Length: > 0 } model ? model : "claude-opus-5-5";

    [Fact]
    public async Task Anthropic_tool_calling_round_trip()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Assert.SkipUnless(LiveEnabled && !string.IsNullOrWhiteSpace(key), "Set RUN_LIVE_LLM_TESTS=1 and ANTHROPIC_API_KEY to run live Anthropic tests.");

        await RunRoundTripAsync(TestConfig.Base().With(
            ("ANTHROPIC_API_KEY", key),
            ("AI:Profiles:orchestrator:Provider", "anthropic"),
            ("AI:Providers:anthropic:DefaultChatModel", AnthropicModel),
            ("AI:Profiles:orchestrator:ReasoningEffort", "low"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "4000")));
    }

    [Fact]
    public async Task OpenAI_tool_calling_round_trip()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.SkipUnless(LiveEnabled && !string.IsNullOrWhiteSpace(key), "Set RUN_LIVE_LLM_TESTS=1 and OPENAI_API_KEY to run live OpenAI tests.");

        await RunRoundTripAsync(TestConfig.Base().With(
            ("OPENAI_API_KEY", key),
            ("AI:Profiles:orchestrator:Provider", "openai"),
            ("AI:Profiles:orchestrator:ReasoningEffort", "low"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "4000")));
    }

    // Earlier turns go back compacted and a hand-off drops the thinking of the turn so far; Claude must accept both.
    [Fact]
    public async Task Anthropic_accepts_a_compacted_history_and_a_hand_off_without_thinking()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Assert.SkipUnless(LiveEnabled && !string.IsNullOrWhiteSpace(key), "Set RUN_LIVE_LLM_TESTS=1 and ANTHROPIC_API_KEY to run live Anthropic tests.");

        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("ANTHROPIC_API_KEY", key),
            ("AI:Providers:anthropic:Settings:ThinkingMismatch", "error"),
            ("AI:Profiles:orchestrator:Provider", "anthropic"),
            ("AI:Providers:anthropic:DefaultChatModel", AnthropicModel),
            ("AI:Profiles:orchestrator:ReasoningEffort", "low"),
            ("AI:Profiles:orchestrator:MaxOutputTokens", "4000")));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];
        const string System = "You answer questions only with facts returned by the search_knowledge_base tool and cite them as [S#].";

        List<ChatMessage> turn = [new(ChatRole.User, "How long is the warranty of the X100 drill? Use the search tool before answering.")];
        var first = await handle.Client.GetResponseAsync([new(ChatRole.System, System), .. turn], options, Ct);
        var call = first.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().First();
        turn.AddRange(first.Messages);
        turn.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, ToolResult + new string(' ', 600))]));

        // A hand-off mid-turn: another system prompt, and no thinking from the turn so far.
        List<ChatMessage> handedOff = [new(ChatRole.System, "You are the warranty specialist. " + System), .. turn];
        ConversationHistoryBuilder.DropReasoningOfCurrentTurn(handedOff);
        var answer = await handle.Client.GetResponseAsync(handedOff, options, Ct);
        answer.Text.ShouldContain("3");
        turn.AddRange(answer.Messages);

        var stored = new ConversationTurn
        {
            Sequence = 1,
            UserMessage = turn[0].Text,
            ModelMessagesJson = ChatJson.SerializeMessages(turn),
        };
        var conversation = new Conversation { SystemPrompt = System, Turns = [stored] };
        var history = new ConversationHistoryBuilder(Options.Create(new HistoryOptions()), NullLogger<ConversationHistoryBuilder>.Instance)
            .Build(conversation).Messages;
        history.Add(new ChatMessage(ChatRole.User, "Which page of the manual says that? Answer from the earlier result."));

        var next = await handle.Client.GetResponseAsync(history, options, Ct);

        next.FinishReason.ShouldBeOneOf(ChatFinishReason.Stop, ChatFinishReason.ToolCalls);
    }

    private static async Task RunRoundTripAsync(Dictionary<string, string?> configuration)
    {
        using var host = AiTestHost.Create(configuration);
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You answer questions only with facts returned by the search_knowledge_base tool and cite them as [S#]."),
            new(ChatRole.User, "How long is the warranty of the X100 drill? Use the search tool before answering."),
        ];

        var first = await handle.Client.GetResponseAsync(messages, options, Ct);
        var call = first.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault();
        call.ShouldNotBeNull($"Expected a {ToolNames.SearchKnowledgeBase} call, got: {first.Text}");
        call.Name.ShouldBe(ToolNames.SearchKnowledgeBase);

        messages.AddRange(first.Messages);
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, ToolResult)]));

        var second = await handle.Client.GetResponseAsync(messages, options, Ct);

        second.Text.ShouldContain("3");
        second.Text.ShouldContain("S1");
        second.FinishReason.ShouldBe(ChatFinishReason.Stop);
    }
}
