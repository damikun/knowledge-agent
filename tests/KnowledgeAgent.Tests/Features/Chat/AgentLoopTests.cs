using System.Net;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class AgentLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parallel_tool_calls_run_concurrently_and_return_one_tool_message_in_call_order()
    {
        using var host = new AgentTestHost();
        var source = host.Sources.Add("Handbook", text: "Vacation policy text.");
        var slowSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Search.Handler = async (request, cancellationToken) =>
        {
            if (request.Query == "slow")
            {
                slowSearchStarted.TrySetResult();
                await releaseSlowSearch.Task.WaitAsync(cancellationToken);
            }

            return [new KnowledgeSearchHit(Guid.NewGuid(), source.Id, 0, "Handbook", null, null, $"Passage for {request.Query}", 0.5)];
        };

        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCalls(
                (ToolNames.SearchKnowledgeBase, new { query = "slow" }),
                (ToolNames.FindSources, null),
                (ToolNames.SearchKnowledgeBase, new { query = "fast" }))
            .ThenText("Done [S1] [S2].");

        var chat = host.ChatAsync("Compare things", cancellationToken: Ct);
        await slowSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The fast search finishing while the slow one is blocked proves the calls run concurrently.
        await WaitUntilAsync(() => host.Search.Requests.Any(r => r.Query == "fast"));
        releaseSlowSearch.SetResult();
        var events = await chat;

        var calls = events.OfType<ToolCallEvent>().ToList();
        calls.Select(c => c.ToolName).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.FindSources, ToolNames.SearchKnowledgeBase]);

        var results = events.OfType<ToolResultEvent>().ToList();
        results.Count.ShouldBe(3);
        results[^1].CallId.ShouldBe(calls[0].CallId, "the slow call finished last");

        var second = orchestrator.Requests[1].Messages;
        second.Count(m => m.Role == ChatRole.Tool).ShouldBe(1);
        var toolResults = second[^1].ToolResults();
        toolResults.Select(r => r.CallId).ShouldBe(calls.Select(c => c.CallId));
        toolResults[0].ResultText().ShouldContain("Passage for slow");
        toolResults[1].ResultText().ShouldContain("Handbook");
        toolResults[2].ResultText().ShouldContain("Passage for fast");

        // Citation numbers follow registration order, and the slow search registered last.
        toolResults[2].ResultText().ShouldContain("[S1]");
        toolResults[0].ResultText().ShouldContain("[S2]");
    }

    [Fact]
    public async Task Concurrent_tool_calls_get_their_own_service_scope()
    {
        AgentTestHost? hostRef = null;
        using var host = new AgentTestHost(configureServices: services =>
            services.AddScoped<ISourceRepository>(_ => new SingleOperationSourceRepository(hostRef!.Sources)));
        hostRef = host;
        var a = host.Sources.Add("Doc A", text: "Alpha text.");
        var b = host.Sources.Add("Doc B", text: "Beta text.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCalls(
                (ToolNames.FindSources, null),
                (ToolNames.ReadSource, new { source_id = a.Id.ToString() }),
                (ToolNames.ReadSource, new { source_id = b.Id.ToString() }))
            .ThenText("Read both.");

        var events = await host.ChatAsync("Read everything", cancellationToken: Ct);

        events.OfType<ToolResultEvent>().ShouldAllBe(result => !result.IsError);
        var results = orchestrator.Requests[1].Messages[^1].ToolResults();
        results[1].ResultText().Unwrapped().ShouldEndWith("Alpha text.");
        results[2].ResultText().Unwrapped().ShouldEndWith("Beta text.");
    }

    [Fact]
    public async Task The_scope_guard_detects_overlapping_operations_on_one_instance()
    {
        var shared = new SingleOperationSourceRepository(new InMemorySourceRepository());

        var overlapping = Task.WhenAll(shared.ListAsync(Ct), shared.ListAsync(Ct));

        await Should.ThrowAsync<InvalidOperationException>(overlapping);
    }

    [Fact]
    public async Task Unknown_tools_and_invalid_arguments_are_fed_back_and_the_loop_continues()
    {
        using var host = new AgentTestHost();
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCalls(
                ("does_not_exist", new { x = 1 }),
                (ToolNames.SearchKnowledgeBase, new { top_k = "many" }),
                (ToolNames.ReadSource, new { source_id = "not-a-guid" }),
                (ToolNames.FindSources, new { status = "archived" }))
            .ThenText("Sorry, I could not look that up.");

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        var results = events.OfType<ToolResultEvent>().ToList();
        results.Count.ShouldBe(4);
        results.ShouldAllBe(r => r.IsError);
        results[0].Summary.ShouldBe(
            "Unknown tool 'does_not_exist'. Available tools: search_knowledge_base, grep_sources, find_sources, read_source, delegate_research, record_feedback.");
        results[1].Summary.ShouldBe("Invalid arguments for search_knowledge_base: 'query' is required. 'top_k' must be an integer.");
        results[2].Summary.ShouldContain("'source_id' must be a source id (GUID)");
        results[3].Summary.ShouldBe("Invalid arguments for find_sources: 'status' must be one of pending, processing, ready, failed or any.");

        var toolResults = orchestrator.Requests[1].Messages[^1].ToolResults();
        toolResults.Count.ShouldBe(4);
        toolResults.ShouldAllBe(r => r.ResultText().StartsWith("Error: "));
        toolResults[0].ResultText().ShouldContain("Unknown tool 'does_not_exist'");

        events.Text().ShouldBe("Sorry, I could not look that up.");
        events.Done().FinishReason.ShouldBe("stop");
        host.Search.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tool_exceptions_become_error_results()
    {
        using var host = new AgentTestHost();
        host.Search.Handler = (_, _) => throw new InvalidOperationException("vector store unavailable");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "anything" })
            .ThenText("The knowledge base is unavailable right now.");

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        var result = events.OfType<ToolResultEvent>().Single();
        result.IsError.ShouldBeTrue();
        result.Summary.ShouldBe("search_knowledge_base failed: vector store unavailable");
        orchestrator.Requests[1].Messages[^1].ToolResults().Single().ResultText()
            .ShouldBe("Error: search_knowledge_base failed: vector store unavailable");
        events.Done().FinishReason.ShouldBe("stop");
        host.SingleTurn(events.ConversationId()).ToolCalls().Single().IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Tool_timeouts_become_error_results()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:ToolTimeoutSeconds"] = "5" });
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Search.Handler = async (_, cancellationToken) =>
        {
            searchStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        };
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "slow" })
            .ThenText("Search timed out.");

        var chat = host.ChatAsync("Question", cancellationToken: Ct);
        await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        host.Time.Advance(TimeSpan.FromSeconds(6));
        var events = await chat.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var result = events.OfType<ToolResultEvent>().Single();
        result.IsError.ShouldBeTrue();
        result.Summary.ShouldBe("search_knowledge_base timed out after 5 seconds. Try a narrower request.");
        orchestrator.Requests.Count.ShouldBe(2);
        events.Done().FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task Unparseable_arguments_are_reported_to_the_model()
    {
        using var host = new AgentTestHost();
        var orchestrator = host.ChatClients.Script("orchestrator")
            .Then((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call_bad", ToolNames.SearchKnowledgeBase) { Exception = new FormatException("Unexpected end of JSON") },
            ])) { FinishReason = ChatFinishReason.ToolCalls })
            .ThenText("Retrying failed.");

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        events.OfType<ToolResultEvent>().Single().Summary.ShouldStartWith("The arguments for search_knowledge_base could not be parsed (Unexpected end of JSON)");
        orchestrator.Requests[1].Messages[^1].ToolResults().Single().CallId.ShouldBe("call_bad");
    }

    [Fact]
    public async Task Max_iterations_forces_a_final_call_without_tools_but_with_the_same_tool_list()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:MaxIterations"] = "2" });
        host.Search.AddHit("Doc", "Some passage.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "first" })
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "second" })
            .ThenText("Final answer from what I found [S1].");

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        orchestrator.Requests.Count.ShouldBe(3);
        orchestrator.Requests[0].Options!.ToolMode.ShouldBe(ChatToolMode.Auto);
        orchestrator.Requests[1].Options!.ToolMode.ShouldBe(ChatToolMode.Auto);
        orchestrator.Requests[2].Options!.ToolMode.ShouldBeOfType<NoneChatToolMode>();
        var toolNames = orchestrator.Requests[0].Options!.Tools!.Select(t => t.Name).ToList();
        orchestrator.Requests[2].Options!.Tools!.Select(t => t.Name).ShouldBe(toolNames);
        orchestrator.Requests[2].Options!.AllowMultipleToolCalls.ShouldBe(orchestrator.Requests[0].Options!.AllowMultipleToolCalls);

        var completed = events.OfType<AgentCompletedEvent>().Single();
        completed.Iterations.ShouldBe(3);
        completed.FinishReason.ShouldBe(ChatFinishReasons.MaxIterations);
        events.Done().FinishReason.ShouldBe(ChatFinishReasons.MaxIterations);
        events.Text().ShouldBe("Final answer from what I found [S1].");
    }

    [Fact]
    public async Task Tool_calls_ignoring_tool_mode_none_are_answered_so_the_history_stays_valid()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:MaxIterations"] = "1" });
        host.Search.AddHit("Doc", "Some passage.");
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "first" })
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "again" });

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        events.OfType<ToolCallEvent>().Count().ShouldBe(1, "the ignored call is not executed");
        events.Done().FinishReason.ShouldBe(ChatFinishReasons.MaxIterations);
        var messages = host.SingleTurn(events.ConversationId()).ModelMessages();
        messages.Select(m => m.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.Tool]);
        var rejected = messages[^1].ToolResults().Single();
        rejected.CallId.ShouldBe(messages[3].Contents.OfType<FunctionCallContent>().Single().CallId);
        rejected.ResultText().ShouldBe(AgentRunner.ToolsDisabledResult);
    }

    [Fact]
    public async Task Refusal_emits_a_refusal_error_and_does_not_commit_model_messages()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").Then((_, _) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "I can't help with")) { FinishReason = ChatFinishReason.ContentFilter });
        host.ChatClients.Script("utility").ThenText("Sensitive request");

        var events = await host.ChatAsync("Something problematic", cancellationToken: Ct);

        events.CollapsedTypes().ShouldBe(
        [
            ChatEventTypes.Conversation,
            ChatEventTypes.AgentStarted,
            ChatEventTypes.TextDelta,
            ChatEventTypes.StepFinished,
            ChatEventTypes.Usage,
            ChatEventTypes.AgentCompleted,
            ChatEventTypes.Conversation,
            ChatEventTypes.Error,
            ChatEventTypes.Done,
        ]);
        var error = events.OfType<ErrorEvent>().Single();
        error.Code.ShouldBe("refusal");
        error.Message.ShouldBe(AgentRunner.RefusalMessage);
        events.OfType<AgentCompletedEvent>().Single().FinishReason.ShouldBe("refusal");
        events.Done().FinishReason.ShouldBe("refusal");

        var turn = host.SingleTurn(events.ConversationId());
        turn.FinishReason.ShouldBe("refusal");
        turn.Error.ShouldBe(AgentRunner.RefusalMessage);
        turn.AssistantMessage.ShouldBe("I can't help with");
        turn.ModelMessagesJson.ShouldBe("[]");
    }

    [Fact]
    public async Task Model_not_configured_emits_error_persists_the_turn_and_finishes()
    {
        using var host = new AgentTestHost();
        host.ChatClients.NotConfigured("orchestrator", "No chat provider is configured. Set AI:Providers:anthropic:ApiKey.");

        var events = await host.ChatAsync("What is in my documents about onboarding new employees in the Berlin office?", cancellationToken: Ct);

        events.CollapsedTypes().ShouldBe(
        [
            ChatEventTypes.Conversation,
            ChatEventTypes.Conversation,
            ChatEventTypes.Error,
            ChatEventTypes.Done,
        ]);
        var error = events.OfType<ErrorEvent>().Single();
        error.Code.ShouldBe("model_not_configured");
        error.Message.ShouldBe("No chat provider is configured. Set AI:Providers:anthropic:ApiKey.");
        events.Done().FinishReason.ShouldBe("error");

        // The utility profile is not configured either, so the title falls back to the message prefix.
        events.OfType<ConversationEvent>().Last().Title.ShouldBe("What is in my documents about onboarding new employees in th");

        var turn = host.SingleTurn(events.ConversationId());
        turn.Error.ShouldBe(error.Message);
        turn.FinishReason.ShouldBe("error");
        turn.Provider.ShouldBeNull();
        turn.ModelMessagesJson.ShouldBe("[]");
    }

    [Fact]
    public async Task Provider_errors_emit_a_safe_provider_error()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenThrow(new HttpRequestException("secret upstream detail", null, HttpStatusCode.TooManyRequests));

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        var error = events.OfType<ErrorEvent>().Single();
        error.Code.ShouldBe("provider_error");
        error.Message.ShouldBe("The model provider returned an error (HTTP 429). Please try again.");
        error.Message.ShouldNotContain("secret");
        events.OfType<AgentCompletedEvent>().Single().FinishReason.ShouldBe("error");
        events.Done().FinishReason.ShouldBe("error");
        host.SingleTurn(events.ConversationId()).Error.ShouldBe(error.Message);
    }

    [Fact]
    public async Task Length_finish_reason_is_reported()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").Then((_, _) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "A very long answer that was cut")) { FinishReason = ChatFinishReason.Length });

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        events.OfType<ErrorEvent>().ShouldBeEmpty();
        events.Done().FinishReason.ShouldBe("length");
        host.SingleTurn(events.ConversationId()).ModelMessages().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Long_tool_output_is_truncated_for_the_model()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:MaxToolResultChars"] = "1500" });
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.FindSources)
            .ThenText("ok");
        for (var i = 0; i < 40; i++)
        {
            host.Sources.Add($"Source number {i} with a fairly long descriptive title", url: $"https://example.com/{i}");
        }

        await host.ChatAsync("List everything", cancellationToken: Ct);

        var text = orchestrator.Requests[1].Messages[^1].ToolResults().Single().ResultText();
        text.Length.ShouldBeLessThanOrEqualTo(1500);
        text.ShouldContain("[… output truncated:");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10, Ct);
        }
    }
}
