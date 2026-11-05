using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ChatScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Chat_streams_the_full_event_sequence_with_tool_call_and_citations()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync(
            "Lighthouse keeping",
            $"The {marker} lighthouse beam rotates every twelve seconds. Keepers polish the {marker} lighthouse lens daily.");
        var question = $"How often does the {marker} lighthouse beam rotate?";

        var stream = await app.ChatAsync(new ChatRequestDto(null, question));

        stream.ContentType.ShouldBe("text/event-stream");
        stream.CacheControl.ShouldNotBeNull().ShouldContain("no-cache");
        stream.AccelBuffering.ShouldBe("no");
        stream.Errors.ShouldBeEmpty(stream.Describe());

        var types = stream.Types;
        types[0].ShouldBe(ChatEventTypes.Conversation, stream.Describe());
        types[^1].ShouldBe(ChatEventTypes.Done, stream.Describe());
        var started = types.IndexOf(ChatEventTypes.AgentStarted);
        var toolCall = types.IndexOf(ChatEventTypes.ToolCall);
        var toolResult = types.IndexOf(ChatEventTypes.ToolResult);
        var firstText = types.IndexOf(ChatEventTypes.TextDelta);
        var lastText = types.LastIndexOf(ChatEventTypes.TextDelta);
        var lastUsage = types.LastIndexOf(ChatEventTypes.Usage);
        var completed = types.LastIndexOf(ChatEventTypes.AgentCompleted);
        var titled = types.LastIndexOf(ChatEventTypes.Conversation);
        var order = stream.Describe();
        started.ShouldBeGreaterThan(0, order);
        toolCall.ShouldBeGreaterThan(started, order);
        toolResult.ShouldBeGreaterThan(toolCall, order);
        firstText.ShouldBeGreaterThan(toolResult, order);
        lastUsage.ShouldBeGreaterThan(lastText, order);
        completed.ShouldBeGreaterThan(lastUsage, order);
        titled.ShouldBeGreaterThan(completed, order);
        (types.Count - 1).ShouldBeGreaterThan(titled, order);

        var opened = stream.Conversation;
        opened.IsNew.ShouldBeTrue();
        opened.ConversationId.ShouldNotBe(Guid.Empty);

        var agent = stream.OrchestratorStarted;
        agent.Provider.ShouldBe(FakeModels.ProviderKey);
        agent.Model.ShouldBe(FakeModels.ChatModel);
        agent.ParentCallId.ShouldBeNull();

        var call = stream.First<ToolCallEvent>(ChatEventTypes.ToolCall);
        call.ToolName.ShouldBe(ToolNames.SearchKnowledgeBase);
        call.Agent.ShouldBe("orchestrator");
        call.Arguments.ShouldNotBeNull().GetProperty("query").GetString().ShouldBe(question);

        var result = stream.First<ToolResultEvent>(ChatEventTypes.ToolResult);
        result.CallId.ShouldBe(call.CallId);
        result.ToolName.ShouldBe(ToolNames.SearchKnowledgeBase);
        result.IsError.ShouldBeFalse(result.Summary);
        result.Summary.ShouldNotBeNullOrWhiteSpace();
        result.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
        result.Citations.ShouldNotBeEmpty();
        var citation = result.Citations.First(c => c.Ref == "S1");
        citation.SourceId.ShouldBe(source.Id);
        citation.SourceTitle.ShouldBe("Lighthouse keeping");
        citation.Snippet.ShouldContain(marker);

        stream.Text.ShouldContain("[S1]");

        var usage = stream.All<UsageEvent>(ChatEventTypes.Usage).Last();
        usage.Provider.ShouldBe(FakeModels.ProviderKey);
        usage.Model.ShouldBe(FakeModels.ChatModel);
        usage.TotalTokens.ShouldNotBeNull().ShouldBeGreaterThan(0);

        var agentCompleted = stream.All<AgentCompletedEvent>(ChatEventTypes.AgentCompleted).Last(e => e.Agent == "orchestrator");
        agentCompleted.Iterations.ShouldBeGreaterThanOrEqualTo(1);
        agentCompleted.FinishReason.ShouldNotBeNullOrWhiteSpace();

        var renamed = stream.All<ConversationEvent>(ChatEventTypes.Conversation).Last();
        renamed.ConversationId.ShouldBe(opened.ConversationId);
        renamed.Title.ShouldNotBeNullOrWhiteSpace();

        var done = stream.Done;
        done.ConversationId.ShouldBe(opened.ConversationId);
        done.TurnId.ShouldNotBe(Guid.Empty);
        done.FinishReason.ShouldNotBeNullOrWhiteSpace();

        foreach (var item in stream.Events)
        {
            var json = item.Json;
            json.TryGetProperty("agent", out var agentName).ShouldBeTrue(item.Data);
            agentName.GetString().ShouldNotBeNullOrWhiteSpace();
            json.TryGetProperty("parentCallId", out _).ShouldBeTrue(item.Data);
            json.TryGetProperty("eventType", out _).ShouldBeFalse(item.Data);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Conversation_is_persisted_with_turn_tool_calls_and_citations()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync(
            "Coffee roasting",
            $"The {marker} roastery roasts Ethiopian beans for eleven minutes at two hundred degrees.");
        var question = $"How long does the {marker} roastery roast Ethiopian beans?";

        var stream = await app.ChatAsync(new ChatRequestDto(null, question));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        var call = stream.First<ToolCallEvent>(ChatEventTypes.ToolCall);
        var title = stream.All<ConversationEvent>(ChatEventTypes.Conversation).Last().Title;

        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{stream.ConversationId}");
        conversation.Id.ShouldBe(stream.ConversationId);
        conversation.Title.ShouldBe(title);
        conversation.AgentName.ShouldBe("orchestrator");
        conversation.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        conversation.PromptVersion.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(1);
        conversation.PromptOrigin.ShouldBe("langfuse");
        conversation.SourceIds.ShouldBeEmpty();

        var turn = conversation.Turns.ShouldHaveSingleItem();
        turn.Id.ShouldBe(stream.Done.TurnId);
        turn.Sequence.ShouldBeGreaterThanOrEqualTo(0);
        turn.UserMessage.ShouldBe(question);
        turn.AssistantMessage.ShouldBe(stream.Text);
        turn.AssistantMessage.ShouldContain("[S1]");
        turn.Provider.ShouldBe(FakeModels.ProviderKey);
        turn.Model.ShouldBe(FakeModels.ChatModel);
        turn.InputTokens.ShouldNotBeNull().ShouldBeGreaterThan(0);
        turn.OutputTokens.ShouldNotBeNull().ShouldBeGreaterThan(0);
        turn.Error.ShouldBeNull();

        var toolCall = turn.ToolCalls.ShouldHaveSingleItem();
        toolCall.CallId.ShouldBe(call.CallId);
        toolCall.ToolName.ShouldBe(ToolNames.SearchKnowledgeBase);
        toolCall.Agent.ShouldBe("orchestrator");
        toolCall.ParentCallId.ShouldBeNull();
        toolCall.IsError.ShouldBeFalse();
        toolCall.Arguments.ShouldNotBeNull().GetProperty("query").GetString().ShouldBe(question);

        turn.Citations.ShouldContain(c => c.Ref == "S1" && c.SourceId == source.Id);

        var summaries = (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations")).Items;
        summaries.ShouldContain(s => s.Id == stream.ConversationId);
        summaries.First(s => s.Id == stream.ConversationId).TurnCount.ShouldBe(1);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Second_turn_reuses_the_frozen_system_prompt_and_replays_history()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var orchestrator = app.ChatModels.Use("orchestrator", new RuleBasedChatClient());
        var marker = ApiClient.UniqueWord();
        await app.AddReadyTextSourceAsync(
            "Ferry timetable",
            $"The {marker} ferry departs the harbour at seven fifteen. The {marker} ferry crossing takes forty minutes.");

        var first = await app.ChatAsync(new ChatRequestDto(null, $"When does the {marker} ferry depart?"));
        first.Errors.ShouldBeEmpty(first.Describe());
        var turnOne = orchestrator.Snapshot();
        turnOne.Count.ShouldBeGreaterThanOrEqualTo(2, "a tool call round and an answer");
        var systemPrompt = turnOne[0].SystemPrompt();
        systemPrompt.ShouldNotBeNullOrWhiteSpace();

        var second = await app.ChatAsync(new ChatRequestDto(first.ConversationId, $"How long does the {marker} crossing take?"));
        second.Errors.ShouldBeEmpty(second.Describe());
        second.Conversation.ConversationId.ShouldBe(first.ConversationId);
        second.Conversation.IsNew.ShouldBeFalse();
        second.Done.ConversationId.ShouldBe(first.ConversationId);

        var turnTwo = orchestrator.Snapshot().Skip(turnOne.Count).ToList();
        turnTwo.ShouldNotBeEmpty();
        var replay = turnTwo[0];

        replay.SystemPrompt().ShouldBe(systemPrompt);

        var history = replay.ConversationMessages();
        var firstQuestion = history.ToList().FindIndex(m => m.Role == ChatRole.User && m.Text.Contains("depart", StringComparison.Ordinal));
        var replayedCall = history.ToList().FindIndex(m => m.Contents.OfType<FunctionCallContent>().Any(c => c.Name == ToolNames.SearchKnowledgeBase));
        var replayedResult = history.ToList().FindIndex(m => m.Contents.OfType<FunctionResultContent>().Any());
        var replayedAnswer = history.ToList().FindIndex(m => m.Role == ChatRole.Assistant && m.Text.Contains("[S1]", StringComparison.Ordinal));
        firstQuestion.ShouldBeGreaterThanOrEqualTo(0);
        replayedCall.ShouldBeGreaterThan(firstQuestion);
        replayedResult.ShouldBeGreaterThan(replayedCall);
        replayedAnswer.ShouldBeGreaterThan(replayedResult);
        history[^1].Role.ShouldBe(ChatRole.User);
        history[^1].Text.ShouldContain("crossing take");

        var turnOneFinal = turnOne[^1].ConversationMessages();
        for (var i = 0; i < turnOneFinal.Count; i++)
        {
            history[i].Role.ShouldBe(turnOneFinal[i].Role);
            history[i].Text.ShouldBe(turnOneFinal[i].Text);
        }

        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{first.ConversationId}");
        conversation.Turns.Count.ShouldBe(2);
        conversation.Turns.Select(t => t.Sequence).ShouldBeInOrder(SortDirection.Ascending);
        conversation.Turns[1].UserMessage.ShouldContain("crossing take");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Scripted_model_streams_reasoning_and_answer_text()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync("Bridge facts", $"The {marker} bridge spans one thousand two hundred metres.");
        var scripted = app.ChatModels.Use(
            "orchestrator",
            new ScriptedChatClient()
                .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = $"{marker} bridge length" })
                .ThenText($"The {marker} bridge is 1200 m long [S1].", reasoning: "Result S1 states the span of the bridge."));

        var stream = await app.ChatAsync(new ChatRequestDto(null, "How long is the bridge?"));

        stream.Errors.ShouldBeEmpty(stream.Describe());
        stream.Text.ShouldBe($"The {marker} bridge is 1200 m long [S1].");
        stream.All<TextDeltaEvent>(ChatEventTypes.TextDelta).Count.ShouldBeGreaterThan(1, "text is streamed in several deltas");
        string.Concat(stream.All<ReasoningDeltaEvent>(ChatEventTypes.ReasoningDelta).Select(e => e.Text))
            .ShouldContain("states the span");
        stream.First<ToolResultEvent>(ChatEventTypes.ToolResult).Citations.ShouldContain(c => c.SourceId == source.Id);

        scripted.RemainingSteps.ShouldBe(0);
        scripted.Requests.Count.ShouldBe(2);
        scripted.Requests[1].Messages.ContentsOf<FunctionResultContent>().ShouldHaveSingleItem();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_chat_requests_are_rejected_before_streaming()
    {
        var app = await fixture.GetAppAsync();

        using (var unknown = await app.Client.PostJsonAsync("/api/chat", new ChatRequestDto(Guid.NewGuid(), "Hello?")))
        {
            await unknown.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using var empty = await app.Client.PostJsonAsync("/api/chat", new ChatRequestDto(null, "   "));
        (await empty.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("message");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Source_scope_ignores_deleted_sources_and_fails_when_none_exist()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var kept = await app.AddReadyTextSourceAsync("Kept glacier notes", $"The {marker} glacier retreats forty metres a year.");
        var deleted = await app.AddReadyTextSourceAsync("Deleted glacier notes", $"The {marker} glacier feeds a turquoise lake.");
        using (var delete = await app.Client.DeleteAsync($"/api/sources/{deleted.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var scoped = await app.ChatAsync(new ChatRequestDto(null, $"How fast does the {marker} glacier retreat?", [deleted.Id, kept.Id]));
        scoped.Errors.ShouldBeEmpty(scoped.Describe());
        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{scoped.ConversationId}");
        conversation.SourceIds.ShouldBe([kept.Id]);

        var gone = await app.ChatAsync(new ChatRequestDto(null, $"What does the {marker} glacier feed?", [deleted.Id]));
        gone.Types.ShouldBe([ChatEventTypes.Error], gone.Describe());
        gone.Errors.ShouldHaveSingleItem().Code.ShouldBe(ChatErrorCodes.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Conversations_can_be_listed_fetched_and_deleted()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();

        var stream = await app.ChatAsync(new ChatRequestDto(null, "Is anything about penguins stored?"));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        var id = stream.ConversationId;

        (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?limit=100")).Items.ShouldContain(c => c.Id == id);

        using (var delete = await app.Client.DeleteAsync($"/api/conversations/{id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var get = await app.Client.GetAsync($"/api/conversations/{id}", Ct))
        {
            await get.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?limit=100")).Items.ShouldNotContain(c => c.Id == id);

        using (var again = await app.Client.DeleteAsync($"/api/conversations/{id}", Ct))
        {
            await again.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using var badLimit = await app.Client.GetAsync("/api/conversations?limit=0", Ct);
        (await badLimit.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("limit");
    }
}

public sealed class InMemoryChatScenarios(InMemoryAppFixture fixture) : ChatScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersChatScenarios(ContainersAppFixture fixture) : ChatScenarios(fixture), IClassFixture<ContainersAppFixture>;
