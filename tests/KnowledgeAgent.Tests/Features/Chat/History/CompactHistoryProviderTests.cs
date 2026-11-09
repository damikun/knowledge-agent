using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Tests.Infrastructure.AI;
using KnowledgeAgent.Tests.Infrastructure.AI.Providers;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Chat.History;

// The compact history goes through each provider's real adapter: no thinking is sent and every tool call keeps its result.
public sealed class CompactHistoryProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anthropic_gets_tool_use_and_tool_result_pairs_without_thinking()
    {
        var body = await SendAsync("anthropic", """
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"ok"}],
             "stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
            """, "application/json");

        var messages = body["messages"]!.AsArray();
        var blocks = messages.SelectMany(message => message!["content"] is JsonArray content ? content : []).ToList();
        blocks.ShouldNotContain(block => block!["type"]!.GetValue<string>().Contains("thinking"));
        var uses = blocks.Where(block => block!["type"]!.GetValue<string>() == "tool_use").Select(block => block!["id"]!.GetValue<string>()).ToList();
        var results = blocks.Where(block => block!["type"]!.GetValue<string>() == "tool_result").Select(block => block!["tool_use_id"]!.GetValue<string>()).ToList();
        uses.Count.ShouldBe(6);
        results.ShouldBe(uses);
        body.ToJsonString().ShouldContain("Earlier result, shortened from");
        body["thinking"]!["block_binding"]!["prefix_mismatch_behavior"]!.GetValue<string>().ShouldBe("drop_block");
    }

    [Fact]
    public async Task OpenAI_gets_tool_calls_and_tool_messages_with_matching_ids()
    {
        var body = await SendAsync("openai", """
            {"id":"chatcmpl-1","object":"chat.completion","created":1700000000,"model":"gpt-5-mini",
             "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}
            """, "application/json");

        var messages = body["messages"]!.AsArray();
        var calls = messages.SelectMany(message => message!["tool_calls"] is JsonArray toolCalls ? toolCalls : []).Select(call => call!["id"]!.GetValue<string>()).ToList();
        var results = messages.Where(message => message!["role"]!.GetValue<string>() == "tool").Select(message => message!["tool_call_id"]!.GetValue<string>()).ToList();
        calls.Count.ShouldBe(6);
        results.ShouldBe(calls);
        body.ToJsonString().ShouldContain("Earlier result, shortened from");
    }

    [Fact]
    public async Task Ollama_gets_one_tool_message_per_call_and_the_context_size()
    {
        var body = await SendAsync("ollama", OpenAiAndOllamaProviderTests.OllamaReply, "application/x-ndjson");

        var messages = body["messages"]!.AsArray();
        var calls = messages.Sum(message => message!["tool_calls"] is JsonArray toolCalls ? toolCalls.Count : 0);
        var results = messages.Count(message => message!["role"]!.GetValue<string>() == "tool");
        calls.ShouldBe(6);
        results.ShouldBe(calls);
        messages.ShouldNotContain(message => message!["thinking"] != null);
        body.ToJsonString().ShouldContain("Earlier result, shortened from");
        body["options"]!["num_ctx"]!.GetValue<int>().ShouldBe(32768);
    }

    private static async Task<JsonObject> SendAsync(string provider, string reply, string contentType)
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => new CannedResponse(200, contentType, reply);
        var endpoint = provider == "openai" ? new Uri(server.BaseAddress, "v1").ToString() : server.BaseAddress.ToString();
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ($"AI:Providers:{provider}:ApiKey", "test-key"),
            ($"AI:Providers:{provider}:Endpoint", endpoint),
            ("AI:Providers:anthropic:Settings:MaxRetries", "0"),
            ("AI:Profiles:orchestrator:Provider", provider)));
        var handle = await host.ChatClients.CreateAsync("orchestrator", cancellationToken: Ct);

        var history = new ConversationHistoryBuilder(Options.Create(new HistoryOptions()), NullLogger<ConversationHistoryBuilder>.Instance)
            .Build(HistoryFixtures.Conversation(turns: 2))
            .Messages;
        history.Add(new ChatMessage(ChatRole.User, "Next question"));
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];

        await handle.Client.GetResponseAsync(history, options, Ct);
        return server.LastRequest.Json;
    }
}
