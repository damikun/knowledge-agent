using System.Net;
using System.Text.Json.Nodes;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Each test uses a dedicated app because it changes Langfuse state.
public abstract class LangfuseScenarios(AppFixture fixture)
{
    private static readonly TimeSpan SeedTimeout = TimeSpan.FromSeconds(20);

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task New_Langfuse_prompt_version_changes_system_prompt_and_model_of_new_conversations_only()
    {
        await using var app = await fixture.CreateAppAsync();
        (await ApiClient.EventuallyAsync(() => app.Langfuse.HasPrompt(PromptNames.OrchestratorSystem), SeedTimeout))
            .ShouldBeTrue($"orchestrator prompt not seeded.{Environment.NewLine}{app.Logs.Dump()}");
        var orchestrator = app.ChatModels.Use("orchestrator", new RuleBasedChatClient());

        var first = await app.ChatAsync(new ChatRequestDto(null, "What is in the knowledge base?"));
        first.Errors.ShouldBeEmpty(first.Describe());
        first.OrchestratorStarted.Model.ShouldBe(FakeModels.ChatModel);
        var originalPrompt = orchestrator.Snapshot()[0].SystemPrompt();
        var originalConversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{first.ConversationId}");

        var marker = ApiClient.UniqueWord("marker");
        var newVersion = app.Langfuse.SetTextPrompt(
            PromptNames.OrchestratorSystem,
            $"You are the {marker} assistant. Answer from the knowledge base and cite sources as [S1], [S2].",
            new JsonObject { ["model"] = "langfuse-config-model" });
        newVersion.ShouldBeGreaterThan(originalConversation.PromptVersion ?? 0);

        var before = orchestrator.Snapshot().Count;
        var followUp = await app.ChatAsync(new ChatRequestDto(first.ConversationId, "Anything else?"));
        followUp.Errors.ShouldBeEmpty(followUp.Describe());
        var followUpPrompt = orchestrator.Snapshot()[before].SystemPrompt();
        followUpPrompt.ShouldBe(originalPrompt);
        followUpPrompt.ShouldNotContain(marker);

        before = orchestrator.Snapshot().Count;
        var second = await app.ChatAsync(new ChatRequestDto(null, "Who are you?"));
        second.Errors.ShouldBeEmpty(second.Describe());
        orchestrator.Snapshot()[before].SystemPrompt().ShouldContain(marker);
        second.OrchestratorStarted.Model.ShouldBe("langfuse-config-model");
        lock (app.AiProvider.CreatedChatProfiles)
        {
            app.AiProvider.CreatedChatProfiles.ShouldContain(p => p.Name == "orchestrator" && p.Model == "langfuse-config-model");
        }

        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{second.ConversationId}");
        conversation.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        conversation.PromptVersion.ShouldBe(newVersion);
        conversation.PromptOrigin.ShouldBe("langfuse");

        var detail = await app.Client.GetJsonAsync<PromptDetailDto>($"/api/prompts/{PromptNames.OrchestratorSystem}");
        detail.Version.ShouldBe(newVersion);
        detail.Text.ShouldNotBeNull().ShouldContain(marker);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Langfuse_outage_falls_back_to_local_prompts_and_chat_keeps_working()
    {
        await using var app = await fixture.CreateAppAsync(options => options.LangfuseUnavailable = true);
        app.ChatModels.Reset();

        var prompts = await app.Client.GetJsonAsync<PromptSummaryDto[]>("/api/prompts");
        PromptNames.All.ShouldBeSubsetOf(prompts.Select(p => p.Name));
        prompts.Where(p => PromptNames.All.Contains(p.Name)).ShouldAllBe(p => p.Origin == "local");

        var detail = await app.Client.GetJsonAsync<PromptDetailDto>($"/api/prompts/{PromptNames.OrchestratorSystem}");
        detail.Origin.ShouldBe("local");
        (detail.Text ?? string.Join('\n', detail.Messages.Select(m => m.Content))).ShouldNotBeNullOrWhiteSpace();

        // No local copy exists for this name, so with Langfuse down the store is unavailable.
        using (var unavailable = await app.Client.GetAsync($"/api/prompts/e2e-langfuse-only-{Guid.NewGuid():N}", TestContext.Current.CancellationToken))
        {
            (await unavailable.ReadProblemAsync(HttpStatusCode.ServiceUnavailable)).Title.ShouldBe("Prompt store unavailable");
        }

        var marker = ApiClient.UniqueWord();
        await app.AddReadyTextSourceAsync("Outage notes", $"The {marker} generator starts automatically during a power outage.");
        var stream = await app.ChatAsync(new ChatRequestDto(null, $"What happens to the {marker} generator during an outage?"));

        stream.Errors.ShouldBeEmpty(stream.Describe());
        stream.Text.ShouldContain("[S1]");
        stream.Done.TurnId.ShouldNotBe(Guid.Empty);

        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{stream.ConversationId}");
        conversation.PromptOrigin.ShouldBe("local");
        conversation.Turns.ShouldHaveSingleItem();
        app.Langfuse.HasPrompt(PromptNames.OrchestratorSystem).ShouldBeFalse("nothing can be seeded while Langfuse is down");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Agent_traces_are_exported_to_the_Langfuse_OTLP_endpoint()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();

        var stream = await app.ChatAsync(new ChatRequestDto(null, "Please create a trace."));
        stream.Errors.ShouldBeEmpty(stream.Describe());

        // The OTLP exporter batches every 5 seconds by default, and stopping the host flushes what is pending.
        if (!await ApiClient.EventuallyAsync(() => app.Langfuse.TraceExportCount > 0, TimeSpan.FromSeconds(12)))
        {
            await app.StopAsync();
        }

        app.Langfuse.TraceExportCount.ShouldBeGreaterThan(0, $"no OTLP export reached the fake Langfuse.{Environment.NewLine}{app.Logs.Dump()}");
        app.Langfuse.RequestLog.ShouldContain(r => r.StartsWith("POST /api/public/otel/v1/traces", StringComparison.Ordinal));
    }
}

public sealed class InMemoryLangfuseScenarios(InMemoryAppFixture fixture) : LangfuseScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersLangfuseScenarios(ContainersAppFixture fixture) : LangfuseScenarios(fixture), IClassFixture<ContainersAppFixture>;
