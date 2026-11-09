using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class ConversationHistoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task In_verbatim_mode_the_second_turn_replays_the_first_turn_unchanged_and_never_re_renders_the_system_prompt()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:History:Mode"] = "Verbatim" });
        host.Search.AddHit("Handbook", "Employees get 25 vacation days.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .Then((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent("I should search the handbook.") { ProtectedData = "signature-1" },
                new TextReasoningContent(string.Empty) { ProtectedData = "redacted-block" },
                new FunctionCallContent("call_1", ToolNames.SearchKnowledgeBase, new Dictionary<string, object?> { ["query"] = "vacation days", ["top_k"] = 3 }),
            ])) { FinishReason = ChatFinishReason.ToolCalls })
            .ThenText("Employees get 25 vacation days [S1].", reasoning: "The passage answers it.")
            .ThenText("You are welcome.");

        var first = await host.ChatAsync("How many vacation days?", cancellationToken: Ct);
        var conversationId = first.ConversationId();

        host.Prompts.SetTemplate(PromptNames.OrchestratorSystem, "A completely different system prompt", version: 99);
        var second = await host.ChatAsync("Thanks!", conversationId, Ct);

        second.OfType<ConversationEvent>().ShouldHaveSingleItem().IsNew.ShouldBeFalse();
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Count.ShouldBe(1);

        orchestrator.Requests.Count.ShouldBe(3);
        var firstTurnFinal = orchestrator.Requests[1].Messages;
        var secondTurn = orchestrator.Requests[2].Messages;

        secondTurn.Count.ShouldBe(6);
        secondTurn[0].Role.ShouldBe(ChatRole.System);
        secondTurn[0].Text.ShouldBe(orchestrator.Requests[0].Messages[0].Text);
        secondTurn[0].Text.ShouldBe("You are Knowledge Agent (Knowledge Agent). Today is 2026-10-22. Ready sources: 0.");
        secondTurn.Take(4).ToJson().ShouldBe(firstTurnFinal.ToJson());
        secondTurn[^1].Role.ShouldBe(ChatRole.User);
        secondTurn[^1].Text.ShouldBe("Thanks!");

        var assistantCall = secondTurn[2];
        var reasoning = assistantCall.Contents.OfType<TextReasoningContent>().ToList();
        reasoning.Select(r => r.ProtectedData).ShouldBe(["signature-1", "redacted-block"]);
        reasoning[0].Text.ShouldBe("I should search the handbook.");
        var call = assistantCall.Contents.OfType<FunctionCallContent>().Single();
        call.CallId.ShouldBe("call_1");
        call.Name.ShouldBe(ToolNames.SearchKnowledgeBase);
        call.Arguments!["query"]!.ToString().ShouldBe("vacation days");
        var result = secondTurn[3].ToolResults().Single();
        result.CallId.ShouldBe("call_1");
        result.ResultText().ShouldBe(firstTurnFinal[3].ToolResults().Single().ResultText());
        result.ResultText().ShouldContain("Found 1 passage(s) for \"vacation days\".");
        var finalAnswer = secondTurn[4];
        finalAnswer.Text.ShouldBe("Employees get 25 vacation days [S1].");
        finalAnswer.Contents.OfType<TextReasoningContent>().Single().ProtectedData.ShouldStartWith("sig-");

        var conversation = host.Conversations.Get(conversationId);
        conversation.SystemPrompt.ShouldNotContain("different");
        conversation.PromptVersion.ShouldBe(3);
        conversation.Turns.Select(t => t.Sequence).ShouldBe([1, 2]);
        conversation.Turns[1].ModelMessages().Select(m => m.Role).ShouldBe([ChatRole.User, ChatRole.Assistant]);
        conversation.Title.ShouldBe("How many vacation days?", "the second turn does not regenerate the title");
    }

    [Fact]
    public async Task By_default_the_second_turn_replays_the_first_without_thinking_and_with_a_stub_for_the_long_result()
    {
        using var host = new AgentTestHost();
        var hit = host.Search.AddHit("Handbook", "Employees get 25 vacation days. " + new string('x', 2_000));
        var orchestrator = host.ChatClients.Script("orchestrator")
            .Then((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent("I should search the handbook.") { ProtectedData = "signature-1" },
                new FunctionCallContent("call_1", ToolNames.SearchKnowledgeBase, new Dictionary<string, object?> { ["query"] = "vacation days", ["top_k"] = 3 }),
            ])) { FinishReason = ChatFinishReason.ToolCalls })
            .ThenText("Employees get 25 vacation days [S1].", reasoning: "The passage answers it.")
            .ThenText("You are welcome.");

        var first = await host.ChatAsync("How many vacation days?", cancellationToken: Ct);
        var second = await host.ChatAsync("Thanks!", first.ConversationId(), Ct);

        second.OfType<ErrorEvent>().ShouldBeEmpty();
        var withinFirstTurn = orchestrator.Requests[1].Messages;
        withinFirstTurn[2].Contents.OfType<TextReasoningContent>().ShouldHaveSingleItem().ProtectedData.ShouldBe("signature-1", "the turn in progress is never changed");
        withinFirstTurn[3].ToolResults().Single().ResultText().ShouldContain(new string('x', 1_000));

        var replay = orchestrator.Requests[2].Messages;
        replay.Select(message => message.Role).ShouldBe([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User]);
        replay[0].Text.ShouldBe(withinFirstTurn[0].Text);
        replay.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ShouldBeEmpty();
        var call = replay[2].Contents.ShouldHaveSingleItem().ShouldBeOfType<FunctionCallContent>();
        call.CallId.ShouldBe("call_1");
        call.Arguments!["query"]!.ToString().ShouldBe("vacation days");
        var stub = replay[3].ToolResults().ShouldHaveSingleItem();
        stub.CallId.ShouldBe("call_1");
        stub.ResultText().ShouldStartWith("(Earlier result, shortened from");
        stub.ResultText().ShouldContain($"\n[S1] Handbook | source_id={hit.SourceId}");
        stub.ResultText().ShouldNotContain("xxxxxxxx");
        replay[4].Text.ShouldBe("Employees get 25 vacation days [S1].");

        var stored = host.Conversations.Get(first.ConversationId()).Turns[0].ModelMessages();
        stored.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Count().ShouldBe(2, "the stored turn keeps everything");
    }

    [Fact]
    public async Task Citation_references_continue_across_turns_and_repeated_chunks_keep_their_reference()
    {
        using var host = new AgentTestHost();
        var chunkA = host.Search.AddHit("Doc A", "Alpha passage.");
        var chunkB = host.Search.AddHit("Doc B", "Beta passage.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "first" })
            .ThenText("First [S1] [S2].")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "second" })
            .ThenText("Second [S2] [S3].");

        var first = await host.ChatAsync("First question", cancellationToken: Ct);
        var conversationId = first.ConversationId();
        first.OfType<ToolResultEvent>().Single().Citations.Select(c => (c.Ref, c.ChunkId)).ShouldBe([("S1", chunkA.ChunkId), ("S2", chunkB.ChunkId)]);

        host.Search.Handler = (_, _) =>
        {
            IReadOnlyList<KnowledgeSearchHit> hits =
            [
                chunkB,
                new KnowledgeSearchHit(Guid.NewGuid(), Guid.NewGuid(), 4, "Doc C", null, 7, "Gamma passage.", 0.42),
            ];
            return Task.FromResult(hits);
        };
        var second = await host.ChatAsync("Second question", conversationId, Ct);

        var citations = second.OfType<ToolResultEvent>().Single().Citations;
        citations.Select(c => c.Ref).ShouldBe(["S2", "S3"]);
        citations[0].ChunkId.ShouldBe(chunkB.ChunkId);
        orchestrator.Requests[3].Messages[^1].ToolResults().Single().ResultText().Unwrapped().ShouldBe(
            "Found 2 passage(s) for \"second\".\n\n"
            + $"[S2] Doc B | score 0.90 | source_id={chunkB.SourceId}\nBeta passage.\n\n"
            + $"[S3] Doc C (page 7) | score 0.42 | source_id={citations[1].SourceId}\nGamma passage.");

        var turns = host.Conversations.Get(conversationId).Turns;
        turns[0].Citations().Select(c => c.Ref).ShouldBe(["S1", "S2"]);
        turns[1].Citations().Select(c => c.Ref).ShouldBe(["S2", "S3"]);
    }

    [Fact]
    public async Task Request_variables_profile_and_source_scope_are_used_for_a_new_conversation()
    {
        using var host = new AgentTestHost();
        var older = host.Sources.Add("Older guide", type: SourceType.Url, url: "https://example.com/guide", createdAt: AgentTestHost.Now.AddDays(-2));
        var newer = host.Sources.Add("Newer notes", type: SourceType.Markdown, createdAt: AgentTestHost.Now.AddDays(-1));
        host.Sources.Add("Still processing", status: SourceStatus.Processing);
        host.Search.AddHit("Newer notes", "Scoped passage.", sourceId: newer.Id);
        host.Search.AddHit("Older guide", "Out of scope passage.", sourceId: older.Id);
        var fast = host.ChatClients.Script("fast")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "notes" })
            .ThenText("Odpoveď [S1].");

        var variables = new Dictionary<string, JsonElement>
        {
            ["language"] = JsonSerializer.SerializeToElement("Slovak"),
            ["appName"] = JsonSerializer.SerializeToElement("Acme Docs"),
        };
        var events = await host.ChatAsync(new ChatRequestDto(null, "Čo je v poznámkach?", [newer.Id], "fast", variables), Ct);

        var render = host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Single();
        render.Variables["agentName"].ShouldBe("Knowledge Agent");
        render.Variables["appName"].ShouldBeOfType<JsonElement>().GetString().ShouldBe("Acme Docs");
        render.Variables["language"].ShouldBeOfType<JsonElement>().GetString().ShouldBe("Slovak");
        render.Variables["today"].ShouldBe("2026-10-22");
        render.Variables["sourceCount"].ShouldBe(2);
        render.Variables["chunkCount"].ShouldBe(6L);
        render.Variables["pendingSourceCount"].ShouldBe(1);
        render.Variables["scopedSourceCount"].ShouldBe(1);
        render.Variables["researcherEnabled"].ShouldBe(true);
        render.Variables["allowGeneralKnowledge"].ShouldBe(true);
        var types = render.Variables["sourceTypes"].ShouldBeAssignableTo<IList<object?>>()!
            .Cast<IReadOnlyDictionary<string, object?>>().ToList();
        types.Select(t => (t["type"], t["count"])).ShouldBe([("url", (object?)1), ("markdown", (object?)1)]);
        render.Variables.Keys.ShouldNotContain("sources");
        render.Variables.Keys.ShouldNotContain("scopedSources");

        host.ChatClients.Requests.Where(r => r.Profile != "utility").Select(r => r.Profile).ShouldBe(["fast"]);
        fast.Requests.Count.ShouldBe(2);
        host.Search.Requests.Single().SourceIds.ShouldBe([newer.Id]);
        events.OfType<ToolResultEvent>().Single().Citations.Single().SourceId.ShouldBe(newer.Id);

        var conversation = host.Conversations.Get(events.ConversationId());
        conversation.Profile.ShouldBe("fast");
        conversation.SourceIds.ShouldBe([newer.Id]);
        conversation.SystemPrompt.ShouldBe("You are Knowledge Agent (Acme Docs). Today is 2026-10-22. Ready sources: 2.");
    }

    [Fact]
    public async Task Source_scope_drops_sources_that_no_longer_exist_and_keeps_ones_still_processing()
    {
        using var host = new AgentTestHost();
        var kept = host.Sources.Add("Kept guide");
        var processing = host.Sources.Add("Still processing", status: SourceStatus.Processing);
        host.ChatClients.Script("orchestrator").ThenText("Nothing about that yet.");

        var events = await host.ChatAsync(
            new ChatRequestDto(null, "What is in my guide?", [Guid.NewGuid(), kept.Id, Guid.Empty, processing.Id, kept.Id]),
            Ct);

        events.OfType<ErrorEvent>().ShouldBeEmpty();
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Single().Variables["scopedSourceCount"].ShouldBe(2);
        host.Conversations.Get(events.ConversationId()).SourceIds.ShouldBe([kept.Id, processing.Id]);
    }

    [Fact]
    public async Task Source_scope_without_any_existing_source_ends_the_turn_with_not_found()
    {
        using var host = new AgentTestHost();
        host.Sources.Add("Unrelated notes");

        var events = await host.ChatAsync(new ChatRequestDto(null, "What is in my guide?", [Guid.NewGuid(), Guid.NewGuid()]), Ct);

        var error = events.ShouldHaveSingleItem().ShouldBeOfType<ErrorEvent>();
        error.Code.ShouldBe(ChatErrorCodes.NotFound);
        error.Message.ShouldBe(ChatService.ScopeNotFoundMessage);
        host.Conversations.Count.ShouldBe(0);
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem).ShouldBeEmpty();
        host.ChatClients.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Source_scopes_larger_than_one_lookup_page_are_checked_in_batches()
    {
        using var host = new AgentTestHost();
        var ids = Enumerable.Range(0, SourceQuery.MaxLimit + 50).Select(i => host.Sources.Add($"Doc {i:000}").Id).ToList();
        host.ChatClients.Script("orchestrator").ThenText("Done.");
        var requested = ids.Take(120).Append(Guid.NewGuid()).Concat(ids.Skip(120)).ToList();

        var events = await host.ChatAsync(new ChatRequestDto(null, "Summarise them.", requested), Ct);

        events.OfType<ErrorEvent>().ShouldBeEmpty();
        host.Conversations.Get(events.ConversationId()).SourceIds.ShouldBe(ids);
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Single().Variables["scopedSourceCount"].ShouldBe(ids.Count);
    }

    [Fact]
    public async Task System_prompt_variables_hold_only_totals_whatever_the_number_of_sources()
    {
        var small = await SystemPromptVariablesJsonAsync(sourceCount: 10);
        var large = await SystemPromptVariablesJsonAsync(sourceCount: 2_000);

        large.ShouldContain("\"sourceCount\":1800");
        large.ShouldContain("\"pendingSourceCount\":200");
        large.ShouldNotContain("Source 1999", Case.Sensitive, "no per-source data may reach the prompt");
        // Only the digits of the totals differ: the prompt does not grow with the knowledge base.
        (large.Length - small.Length).ShouldBeLessThan(16);
    }

    private static async Task<string> SystemPromptVariablesJsonAsync(int sourceCount)
    {
        using var host = new AgentTestHost();
        for (var i = 0; i < sourceCount; i++)
        {
            host.Sources.Add(
                $"Source {i}",
                status: i % 10 == 9 ? SourceStatus.Processing : SourceStatus.Ready,
                type: i % 2 == 0 ? SourceType.Pdf : SourceType.Url,
                chunkCount: 5);
        }

        host.ChatClients.Script("orchestrator").ThenText("ok");
        await host.ChatAsync("Hi", cancellationToken: Ct);

        var variables = host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Single().Variables;
        variables.Keys.ShouldBe(
            [
                "agentName", "appName", "today", "sourceCount", "chunkCount", "sourceTypes", "pendingSourceCount", "researcherEnabled",
                "allowGeneralKnowledge", "feedbackEnabled", "handoverEnabled", "offerHandoverWhenUnsure", "isVisitor",
            ],
            ignoreOrder: true);
        return JsonSerializer.Serialize(variables);
    }

    [Fact]
    public async Task Prompt_config_model_settings_are_read_on_every_turn()
    {
        using var host = new AgentTestHost();
        host.Prompts.SetTemplate(PromptNames.OrchestratorSystem, "System", version: 4, config: new JsonObject { ["model"] = "model-v1", ["reasoningEffort"] = "high" });
        host.ChatClients.Script("orchestrator").ThenText("one").ThenText("two");

        var first = await host.ChatAsync("Hi", cancellationToken: Ct);
        first.OfType<AgentStartedEvent>().Single().Model.ShouldBe("model-v1");

        host.Prompts.SetTemplate(PromptNames.OrchestratorSystem, "System changed", version: 5, config: new JsonObject { ["model"] = "model-v2" });
        var second = await host.ChatAsync("Again", first.ConversationId(), Ct);

        second.OfType<AgentStartedEvent>().Single().Model.ShouldBe("model-v2");
        var orchestratorRequests = host.ChatClients.Requests.Where(r => r.Profile == "orchestrator").ToList();
        orchestratorRequests[0].PromptPatch.ShouldBe(new ModelProfilePatch(Model: "model-v1", ReasoningEffort: "high"));
        orchestratorRequests[1].PromptPatch.ShouldBe(new ModelProfilePatch(Model: "model-v2"));
        orchestratorRequests.ShouldAllBe(r => r.RequestPatch == null);
        host.Prompts.TemplateFetches.Count(name => name == PromptNames.OrchestratorSystem).ShouldBe(2);
        host.Conversations.Get(first.ConversationId()).SystemPrompt.ShouldBe("System");
    }

    [Fact]
    public async Task Prompt_config_for_another_profile_does_not_override_an_explicit_profile()
    {
        using var host = new AgentTestHost();
        host.Prompts.SetTemplate(PromptNames.OrchestratorSystem, "System", config: new JsonObject { ["profile"] = "orchestrator", ["model"] = "big-model" });
        host.ChatClients.Script("fast").ThenText("ok");

        var events = await host.ChatAsync(new ChatRequestDto(null, "Hi", Profile: "fast"), Ct);

        events.OfType<AgentStartedEvent>().Single().Model.ShouldBe("fast-model");
        host.ChatClients.Requests.Single(r => r.Profile == "fast").PromptPatch.ShouldBeNull();
    }

    [Fact]
    public async Task Existing_conversations_honour_a_request_profile_for_that_turn()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("one");
        host.ChatClients.Script("fast").ThenText("two");

        var first = await host.ChatAsync("Hi", cancellationToken: Ct);
        var second = await host.ChatAsync(new ChatRequestDto(first.ConversationId(), "Again", Profile: "fast"), Ct);

        second.OfType<AgentStartedEvent>().Single().Model.ShouldBe("fast-model");
        host.TurnModels(first.ConversationId()).ShouldBe(["orchestrator-model", "fast-model"]);
    }

    [Fact]
    public async Task Unknown_conversation_emits_not_found_and_stops()
    {
        using var host = new AgentTestHost();
        var orchestrator = host.ChatClients.Script("orchestrator");
        var missing = Guid.NewGuid();

        var events = await host.ChatAsync("Hello?", missing, Ct);

        var error = events.ShouldHaveSingleItem().ShouldBeOfType<ErrorEvent>();
        error.Code.ShouldBe("not_found");
        error.Message.ShouldContain(missing.ToString());
        orchestrator.Requests.ShouldBeEmpty();
        host.Conversations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Broken_system_prompt_emits_an_error_and_creates_nothing()
    {
        using var host = new AgentTestHost();
        host.Prompts.FailRenderOf = PromptNames.OrchestratorSystem;

        var events = await host.ChatAsync("Hello?", cancellationToken: Ct);

        var error = events.ShouldHaveSingleItem().ShouldBeOfType<ErrorEvent>();
        error.Code.ShouldBe("internal_error");
        error.Message.ShouldContain("system prompt");
        host.Conversations.Count.ShouldBe(0);
    }
}

internal static class ConversationAssertions
{
    public static IReadOnlyList<string?> TurnModels(this AgentTestHost host, Guid conversationId) =>
        host.Conversations.Get(conversationId).Turns.Select(turn => turn.Model).ToList();
}
