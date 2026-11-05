using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class SerializationTests
{
    [Fact]
    public void Model_messages_round_trip_reasoning_signatures_and_tool_contents()
    {
        var original = new[]
        {
            new ChatMessage(ChatRole.User, "How many vacation days?"),
            new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent("Let me search.") { ProtectedData = "EqQBCkYIARgCKkA-signature" },
                new TextReasoningContent(string.Empty) { ProtectedData = "redacted-thinking-payload" },
                new TextContent("Searching the handbook."),
                new FunctionCallContent("toolu_01", ToolNames.SearchKnowledgeBase, new Dictionary<string, object?>
                {
                    ["query"] = "vacation days",
                    ["top_k"] = 4,
                    ["source_ids"] = new[] { "11111111-1111-1111-1111-111111111111" },
                }),
            ]) { MessageId = "msg_1" },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_01", "Found 1 passage(s) for \"vacation days\".")]),
            new ChatMessage(ChatRole.Assistant, "Employees get 25 days [S1]."),
        };

        var json = ChatJson.SerializeMessages(original);
        var restored = ChatJson.DeserializeMessages(json);

        restored.Count.ShouldBe(4);
        restored.Select(m => m.Role).ShouldBe(original.Select(m => m.Role));
        restored.ToJson().ShouldBe(original.ToJson());

        var assistant = restored[1];
        assistant.MessageId.ShouldBe("msg_1");
        var reasoning = assistant.Contents.OfType<TextReasoningContent>().ToList();
        reasoning.Select(r => (r.Text, r.ProtectedData)).ShouldBe(
            [("Let me search.", "EqQBCkYIARgCKkA-signature"), (string.Empty, "redacted-thinking-payload")]);
        assistant.Contents.Select(c => c.GetType()).ShouldBe(
            [typeof(TextReasoningContent), typeof(TextReasoningContent), typeof(TextContent), typeof(FunctionCallContent)]);

        var call = assistant.Contents.OfType<FunctionCallContent>().Single();
        call.CallId.ShouldBe("toolu_01");
        call.Name.ShouldBe(ToolNames.SearchKnowledgeBase);
        call.Arguments!["query"].ShouldBeOfType<JsonElement>().GetString().ShouldBe("vacation days");
        call.Arguments["top_k"].ShouldBeOfType<JsonElement>().GetInt32().ShouldBe(4);

        var result = restored[2].Contents.OfType<FunctionResultContent>().Single();
        result.CallId.ShouldBe("toolu_01");
        result.Result!.ToString().ShouldBe("Found 1 passage(s) for \"vacation days\".");
    }

    [Fact]
    public void Unsupported_provider_content_never_fails_persistence()
    {
        var dropped = new List<string>();
        var messages = new[]
        {
            new ChatMessage(ChatRole.Assistant, [new ProviderSpecificContent("x"), new TextContent("Visible answer.")]),
            new ChatMessage(ChatRole.Assistant, [new ProviderSpecificContent("only")]),
        };

        var json = ChatJson.SerializeMessages(messages, dropped.Add);
        var restored = ChatJson.DeserializeMessages(json);

        // M.E.AI cannot serialize unknown AIContent subclasses, so they are dropped and the rest is kept.
        dropped.Count.ShouldBe(2);
        dropped.ShouldAllBe(name => name.EndsWith(nameof(ProviderSpecificContent)));
        restored.Count.ShouldBe(1, "a message left without contents is dropped");
        restored[0].Contents.ShouldHaveSingleItem().ShouldBeOfType<TextContent>().Text.ShouldBe("Visible answer.");
    }

    [Fact]
    public void Persisted_turn_projections_use_camel_case_and_tolerate_bad_json()
    {
        var calls = new[] { new ToolCallDto("c1", ToolNames.FindSources, "orchestrator", null, null, "2 sources", false, 12) };

        var json = ChatJson.SerializeDtos(calls);

        json.ShouldContain("\"callId\":\"c1\"");
        json.ShouldContain("\"durationMs\":12");
        ChatJson.DeserializeDtos<ToolCallDto>(json).ShouldBe(calls);
        ChatJson.DeserializeDtos<ToolCallDto>("not json").ShouldBeEmpty();
        ChatJson.DeserializeDtos<ToolCallDto>(null).ShouldBeEmpty();
        ChatJson.DeserializeMessages("").ShouldBeEmpty();
        Should.Throw<JsonException>(() => ChatJson.DeserializeMessages("{broken"));
    }
}

public sealed class ProviderSpecificContent(string payload) : AIContent
{
    public string Payload { get; } = payload;
}
