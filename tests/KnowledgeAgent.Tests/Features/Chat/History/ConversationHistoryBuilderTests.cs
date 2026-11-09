using System.Globalization;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Chat.History;

public sealed class ConversationHistoryBuilderTests
{
    [Fact]
    public void Verbatim_replays_every_stored_message_unchanged()
    {
        var conversation = HistoryFixtures.Conversation(turns: 3);

        var history = Builder(HistoryMode.Verbatim).Build(conversation);

        var stored = conversation.Turns.SelectMany(turn => turn.ModelMessages()).ToList();
        history.Messages.Skip(1).ToJson().ShouldBe(stored.ToJson());
        history.Messages[0].Role.ShouldBe(ChatRole.System);
        history.Mode.ShouldBe(HistoryMode.Verbatim);
        history.StubbedResults.ShouldBe(0);
        history.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Count().ShouldBe(6);
    }

    [Fact]
    public void Compact_drops_thinking_keeps_calls_and_answers_and_stubs_long_results()
    {
        var conversation = HistoryFixtures.Conversation(turns: 2);

        var history = Builder().Build(conversation);

        var messages = history.Messages;
        messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ShouldBeEmpty();
        messages.Select(message => message.Role).ShouldBe(
            [ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);

        var storedCalls = conversation.Turns.SelectMany(turn => turn.ModelMessages()).SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        var calls = messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        calls.Select(call => (call.CallId, call.Name, ToolArguments.ToJson(call.Arguments).GetRawText()))
            .ShouldBe(storedCalls.Select(call => (call.CallId, call.Name, ToolArguments.ToJson(call.Arguments).GetRawText())));

        messages[1].Text.ShouldBe("Question 1");
        messages[4].Text.ShouldBe("Answer 1 [S1].");
        var results = messages[3].ToolResults();
        results.Select(result => result.CallId).ShouldBe(["call_1_search", "call_1_read", "call_1_feedback"]);
        results[0].ResultText().ShouldStartWith("(Earlier result, shortened from 12,");
        results[0].ResultText().ShouldContain($"[S1] Handbook 1 (page 1) | source_id={HistoryFixtures.SourceId(1)}");
        results[1].ResultText().ShouldStartWith("(Earlier result, shortened from");
        results[2].ResultText().ShouldBe("Feedback recorded.");
        history.StubbedResults.ShouldBe(4);
        history.Turns.ShouldBe(2);
    }

    [Fact]
    public void Every_tool_call_keeps_a_result_with_the_same_id_right_after_it()
    {
        var conversation = HistoryFixtures.Conversation(turns: 4, searchesPerTurn: 3);

        var messages = Builder().Build(conversation).Messages;

        for (var i = 0; i < messages.Count; i++)
        {
            var calls = messages[i].Contents.OfType<FunctionCallContent>().Select(call => call.CallId).ToList();
            if (calls.Count > 0)
            {
                messages[i + 1].Role.ShouldBe(ChatRole.Tool);
                messages[i + 1].ToolResults().Select(result => result.CallId).ShouldBe(calls);
            }
        }
    }

    [Fact]
    public void The_history_of_a_turn_is_a_prefix_of_the_next_one()
    {
        var builder = Builder();
        var longer = HistoryFixtures.Conversation(turns: 5);

        for (var turns = 1; turns < 5; turns++)
        {
            var shorter = HistoryFixtures.Conversation(turns: turns);
            var before = builder.Build(shorter).Messages.ToJson();
            var after = builder.Build(longer).Messages.ToJson();

            after.Take(before.Count).ShouldBe(before);
        }

        builder.Build(longer).Messages.ToJson().ShouldBe(builder.Build(longer).Messages.ToJson());
    }

    [Fact]
    public void Kept_full_turns_lose_their_thinking_but_not_their_results()
    {
        var conversation = HistoryFixtures.Conversation(turns: 3);

        var messages = Builder(keepFullTurns: 1).Build(conversation).Messages;

        messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ShouldBeEmpty();
        var stored = conversation.Turns[2].ModelMessages()[2].ToolResults();
        var lastResults = messages[^2].ToolResults();
        lastResults.Select(result => result.ResultText()).ShouldBe(stored.Select(result => result.ResultText()));
        messages[3].ToolResults()[0].ResultText().ShouldStartWith("(Earlier result");
    }

    [Fact]
    public void A_message_left_without_content_is_left_out()
    {
        var turn = HistoryFixtures.Turn(1, [
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("only thinking") { ProtectedData = "sig" }]),
            new ChatMessage(ChatRole.Assistant, [new TextContent("Hello.")]),
        ]);

        var messages = Builder().Build(HistoryFixtures.Conversation([turn])).Messages;

        messages.Select(message => (message.Role, message.Text)).ShouldBe([(ChatRole.System, "System prompt"), (ChatRole.User, "Hi"), (ChatRole.Assistant, "Hello.")]);
    }

    [Fact]
    public void A_confirmed_action_appended_to_the_question_is_shortened()
    {
        var output = new string('o', 5_000);
        var plain = HistoryFixtures.Turn(1, [
            new ChatMessage(ChatRole.User, "Yes\n\n(The person pressed Confirm. It ran; the tool returned:\n" + output + "\nTell them the outcome.)"),
            new ChatMessage(ChatRole.Assistant, "Done."),
        ]);
        plain.UserMessage = "Yes";
        var wrapped = HistoryFixtures.Turn(2, [
            new ChatMessage(ChatRole.User, "Go\n\n(It ran; the tool returned:\n" + UntrustedText.Wrap(output, "0a1b2c3d4e5f") + "\nTell them the outcome.)"),
            new ChatMessage(ChatRole.Assistant, "Done."),
        ]);
        wrapped.UserMessage = "Go";

        var messages = Builder(mcpExcerptChars: 100).Build(HistoryFixtures.Conversation([plain, wrapped])).Messages;

        messages[1].Text.ShouldStartWith("Yes\n\n(The person pressed Confirm.");
        messages[1].Text.ShouldEndWith("…\n(Shortened in later turns.)");
        messages[1].Text.Length.ShouldBeLessThan(700);
        var second = messages[3].Text;
        second.ShouldStartWith("Go\n\n(It ran; the tool returned:\n" + UntrustedText.Notice);
        second.ShouldEndWith("</untrusted-0a1b2c3d4e5f>\nTell them the outcome.)");
        second.ShouldContain(new string('o', 100) + "…\n(Shortened in later turns.)");
        second.Length.ShouldBeLessThan(500);
    }

    [Fact]
    public void Unreadable_turns_are_skipped()
    {
        var broken = HistoryFixtures.Turn(1, []);
        broken.ModelMessagesJson = "{not json";
        var conversation = HistoryFixtures.Conversation([broken, HistoryFixtures.SearchTurn(2)]);

        var history = Builder().Build(conversation);

        history.Messages[1].Text.ShouldBe("Question 2");
    }

    [Fact]
    public void Ten_turns_with_three_searches_each_shrink_to_a_fraction()
    {
        var conversation = HistoryFixtures.Conversation(turns: 10, searchesPerTurn: 3);

        var verbatim = Builder(HistoryMode.Verbatim).Build(conversation);
        var compact = Builder().Build(conversation);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Replayed history before the 11th question: verbatim ~{verbatim.EstimatedTokens} tokens, compact ~{compact.EstimatedTokens} tokens."));
        verbatim.EstimatedTokens.ShouldBeGreaterThan(80_000);
        compact.EstimatedTokens.ShouldBeLessThan(verbatim.EstimatedTokens / 10);
        compact.StubbedResults.ShouldBe(30);
    }

    [Fact]
    public void Dropping_the_current_turn_thinking_leaves_earlier_turns_and_the_tool_pairs_alone()
    {
        var earlier = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("old") { ProtectedData = "sig-old" }, new TextContent("Earlier answer.")]);
        var thinkingOnly = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("hmm") { ProtectedData = "sig-a" }]);
        var call = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("transfer") { ProtectedData = "sig-b" }, new FunctionCallContent("call_t", ToolNames.TransferToAgent)]);
        var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_t", "Handed over.")]);
        List<ChatMessage> history = [new(ChatRole.System, "front"), new(ChatRole.User, "Earlier"), earlier, new(ChatRole.User, "Refund?"), thinkingOnly, call, result];

        var dropped = ConversationHistoryBuilder.DropReasoningOfCurrentTurn(history);

        dropped.ShouldBe(2);
        history.Count.ShouldBe(6);
        history[2].ShouldBeSameAs(earlier);
        history[3].Text.ShouldBe("Refund?");
        history[4].Contents.ShouldHaveSingleItem().ShouldBeOfType<FunctionCallContent>().CallId.ShouldBe("call_t");
        history[5].ShouldBeSameAs(result);
        call.Contents.Count.ShouldBe(2, "the message the model returned is not changed in place");
    }

    private static ConversationHistoryBuilder Builder(HistoryMode mode = HistoryMode.Compact, int keepFullTurns = 0, int mcpExcerptChars = 1000) =>
        new(Options.Create(new HistoryOptions { Mode = mode, KeepFullTurns = keepFullTurns, McpExcerptChars = mcpExcerptChars }), NullLogger<ConversationHistoryBuilder>.Instance);
}

internal static class HistoryFixtures
{
    public static Guid SourceId(int turn) => new(turn, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    public static Conversation Conversation(int turns, int searchesPerTurn = 1) =>
        Conversation(Enumerable.Range(1, turns).Select(i => SearchTurn(i, searchesPerTurn)).ToList());

    public static Conversation Conversation(IReadOnlyList<ConversationTurn> turns)
    {
        var conversation = new Conversation { Id = Guid.NewGuid(), SystemPrompt = "System prompt" };
        foreach (var turn in turns)
        {
            turn.ConversationId = conversation.Id;
            conversation.Turns.Add(turn);
        }

        return conversation;
    }

    // A question, thinking and parallel tool calls in one round, then thinking and the answer.
    public static ConversationTurn SearchTurn(int n, int searchesPerTurn = 1)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, $"Question {n}") };
        var round = new List<AIContent> { new TextReasoningContent($"Search for {n}.") { ProtectedData = $"sig-{n}-a" } };
        var results = new List<AIContent>();
        for (var s = 0; s < searchesPerTurn; s++)
        {
            var callId = s == 0 ? $"call_{n}_search" : $"call_{n}_search_{s}";
            round.Add(new FunctionCallContent(callId, ToolNames.SearchKnowledgeBase, new Dictionary<string, object?> { ["query"] = $"topic {n}.{s}", ["top_k"] = 6 }));
            results.Add(new FunctionResultContent(callId, SearchText(n)));
        }

        if (searchesPerTurn == 1)
        {
            round.Add(new FunctionCallContent($"call_{n}_read", ToolNames.ReadSource, new Dictionary<string, object?> { ["source_id"] = SourceId(n).ToString() }));
            results.Add(new FunctionResultContent($"call_{n}_read", $"Source: Handbook {n} (id: {SourceId(n)}, type: pdf)\nCharacters 0-6000 of 9000. Next offset: 6000.\n\n" + new string('r', 6_000)));
            round.Add(new FunctionCallContent($"call_{n}_feedback", ToolNames.RecordFeedback, new Dictionary<string, object?> { ["note"] = "ok" }));
            results.Add(new FunctionResultContent($"call_{n}_feedback", "Feedback recorded."));
        }

        messages.Add(new ChatMessage(ChatRole.Assistant, round));
        messages.Add(new ChatMessage(ChatRole.Tool, results));

        messages.Add(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("It answers it.") { ProtectedData = $"sig-{n}-b" }, new TextContent($"Answer {n} [S1].")]));

        var turn = Turn(n, messages);
        turn.CitationsJson = ChatJson.SerializeDtos<CitationDto>([new CitationDto("S1", SourceId(n), Guid.NewGuid(), $"Handbook {n}", null, 1, "snippet", 0.9)]);
        return turn;
    }

    public static ConversationTurn Turn(int sequence, IReadOnlyList<ChatMessage> messages) => new()
    {
        Id = Guid.NewGuid(),
        Sequence = sequence,
        UserMessage = messages.FirstOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty,
        ModelMessagesJson = ChatJson.SerializeMessages(messages),
        CreatedAt = new DateTimeOffset(2026, 11, 9, 10, sequence, 0, TimeSpan.Zero),
    };

    private static string SearchText(int n) =>
        $"Found 3 passage(s) for \"topic {n}\"."
        + $"\n\n[S1] Handbook {n} (page 1) | score 0.90 | source_id={SourceId(n)}\n" + new string('a', 3_900)
        + $"\n\n[S2] Handbook {n} (page 2) | score 0.80 | source_id={SourceId(n)}\n" + new string('b', 3_900)
        + $"\n\n[S3] Handbook {n} (page 3) | score 0.70 | source_id={SourceId(n)}\n" + new string('c', 3_900);
}
