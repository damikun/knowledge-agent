using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ModelScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Profiles_list_configured_override_and_effective_models()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);

        var profiles = await app.Client.GetJsonAsync<ModelProfileDto[]>("/api/models/profiles");

        profiles.Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(new[] { "contextualizer", "embedding", "judge", "orchestrator", "rerank", "researcher", "utility" });

        var orchestrator = profiles.Single(p => p.Name == "orchestrator");
        orchestrator.Kind.ShouldBe("chat");
        orchestrator.Configured.Provider.ShouldBe(FakeModels.ProviderKey);
        orchestrator.Configured.ReasoningEffort.ShouldBe("medium");
        orchestrator.Configured.ReasoningOutput.ShouldBe("summary");
        orchestrator.Configured.MaxOutputTokens.ShouldBe(16000);
        orchestrator.Override.ShouldBeNull();
        orchestrator.OverrideUpdatedAt.ShouldBeNull();
        orchestrator.Error.ShouldBeNull();
        var effective = orchestrator.Effective.ShouldNotBeNull();
        effective.ProviderKey.ShouldBe(FakeModels.ProviderKey);
        effective.ProviderType.ShouldBe(FakeAiProvider.TypeName);
        effective.Model.ShouldBe(FakeModels.ChatModel);
        effective.Source.ShouldBe(nameof(ModelProfileSource.Configuration));

        profiles.Single(p => p.Name == "utility").Effective.ShouldNotBeNull().Model.ShouldBe(FakeModels.UtilityModel);

        var embedding = profiles.Single(p => p.Name == "embedding");
        embedding.Kind.ShouldBe("embedding");
        embedding.Effective.ShouldNotBeNull().Model.ShouldBe(FakeModels.EmbeddingModel);

        var rerank = profiles.Single(p => p.Name == "rerank");
        rerank.Kind.ShouldBe("rerank");
        rerank.Effective.ShouldBeNull("auto only picks dedicated rerankers, and the tests configure no Voyage key");
        rerank.Error.ShouldNotBeNull().ShouldContain("voyage (not configured");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Override_switches_the_model_of_the_next_chat_and_delete_restores_configuration()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);
        app.ChatModels.Reset();

        try
        {
            using var put = await app.Client.PutJsonAsync(
                "/api/models/profiles/orchestrator",
                new UpdateModelProfileRequest(FakeModels.ProviderKey, "fake-override-model", ReasoningEffort: "HIGH", ReasoningOutput: "full"));
            var updated = await put.ReadJsonAsync<ModelProfileDto>();
            updated.Name.ShouldBe("orchestrator");
            var stored = updated.Override.ShouldNotBeNull();
            stored.Provider.ShouldBe(FakeModels.ProviderKey);
            stored.Model.ShouldBe("fake-override-model");
            stored.ReasoningEffort.ShouldBe("high");
            stored.ReasoningOutput.ShouldBe("full");
            updated.OverrideUpdatedAt.ShouldNotBeNull();
            var effective = updated.Effective.ShouldNotBeNull();
            effective.Model.ShouldBe("fake-override-model");
            effective.ReasoningEffort.ShouldBe("high");
            effective.Source.ShouldBe(nameof(ModelProfileSource.Override));

            var listed = await app.Client.GetJsonAsync<ModelProfileDto[]>("/api/models/profiles");
            listed.Single(p => p.Name == "orchestrator").Effective.ShouldNotBeNull().Model.ShouldBe("fake-override-model");

            var createdBefore = CreatedProfiles(app).Count;
            var overridden = await app.ChatAsync(new ChatRequestDto(null, "Which model answers this question?"));
            overridden.Errors.ShouldBeEmpty(overridden.Describe());
            overridden.OrchestratorStarted.Model.ShouldBe("fake-override-model");
            CreatedProfiles(app).Skip(createdBefore)
                .ShouldContain(p => p.Name == "orchestrator" && p.Model == "fake-override-model" && p.ProviderKey == FakeModels.ProviderKey);

            using var delete = await app.Client.DeleteAsync("/api/models/profiles/orchestrator/override", Ct);
            var restored = await delete.ReadJsonAsync<ModelProfileDto>();
            restored.Override.ShouldBeNull();
            restored.OverrideUpdatedAt.ShouldBeNull();
            restored.Effective.ShouldNotBeNull().Model.ShouldBe(FakeModels.ChatModel);
            restored.Effective.Source.ShouldBe(nameof(ModelProfileSource.Configuration));

            var configured = await app.ChatAsync(new ChatRequestDto(null, "And which model answers now?"));
            configured.Errors.ShouldBeEmpty(configured.Describe());
            configured.OrchestratorStarted.Model.ShouldBe(FakeModels.ChatModel);
        }
        finally
        {
            await ClearOverridesAsync(app);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_overrides_are_rejected_and_nothing_is_stored()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);

        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest("does-not-exist"), "provider");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest("  "), "provider");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest("voyage"), "provider");
        await ExpectInvalidAsync(app, "embedding", new UpdateModelProfileRequest("anthropic"), "provider");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest(FakeModels.ProviderKey, ReasoningEffort: "extreme"), "reasoningEffort");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest(FakeModels.ProviderKey, ReasoningOutput: "verbose"), "reasoningOutput");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest(FakeModels.ProviderKey, Temperature: 3.5f), "temperature");
        await ExpectInvalidAsync(app, "orchestrator", new UpdateModelProfileRequest(FakeModels.ProviderKey, MaxOutputTokens: 0), "maxOutputTokens");

        using (var unknownProfile = await app.Client.PutJsonAsync("/api/models/profiles/nope", new UpdateModelProfileRequest("auto")))
        {
            await unknownProfile.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var unknownDelete = await app.Client.DeleteAsync("/api/models/profiles/nope/override", Ct))
        {
            await unknownDelete.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        var profiles = await app.Client.GetJsonAsync<ModelProfileDto[]>("/api/models/profiles");
        profiles.ShouldAllBe(p => p.Override == null);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Rerank_profile_takes_dedicated_rerankers_and_chat_providers()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);

        try
        {
            using (var voyage = await app.Client.PutJsonAsync("/api/models/profiles/rerank", new UpdateModelProfileRequest("voyage")))
            {
                var updated = await voyage.ReadJsonAsync<ModelProfileDto>();
                updated.Kind.ShouldBe("rerank");
                updated.Override.ShouldNotBeNull().Provider.ShouldBe("voyage");
                updated.Error.ShouldNotBeNull().ShouldContain("not configured");
            }

            using var chat = await app.Client.PutJsonAsync("/api/models/profiles/rerank", new UpdateModelProfileRequest(FakeModels.ProviderKey));
            var effective = (await chat.ReadJsonAsync<ModelProfileDto>()).Effective.ShouldNotBeNull();
            effective.ProviderKey.ShouldBe(FakeModels.ProviderKey);
            effective.Model.ShouldBe(FakeModels.ChatModel, "a chat provider reranks with its chat model");
        }
        finally
        {
            await ClearOverridesAsync(app);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Auto_provider_override_is_accepted()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);

        try
        {
            using var put = await app.Client.PutJsonAsync("/api/models/profiles/utility", new UpdateModelProfileRequest("AUTO", MaxOutputTokens: 500));
            var updated = await put.ReadJsonAsync<ModelProfileDto>();
            updated.Override.ShouldNotBeNull().Provider.ShouldBe("auto");
            updated.Override.MaxOutputTokens.ShouldBe(500);
        }
        finally
        {
            await ClearOverridesAsync(app);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Override_to_an_unconfigured_provider_streams_a_model_not_configured_error()
    {
        var app = await fixture.GetAppAsync();
        await ClearOverridesAsync(app);
        app.ChatModels.Reset();

        try
        {
            using var put = await app.Client.PutJsonAsync("/api/models/profiles/orchestrator", new UpdateModelProfileRequest("anthropic"));
            var updated = await put.ReadJsonAsync<ModelProfileDto>();
            updated.Override.ShouldNotBeNull().Provider.ShouldBe("anthropic");
            updated.Effective.ShouldBeNull("anthropic has no API key in the tests");
            updated.Error.ShouldNotBeNullOrWhiteSpace();

            var stream = await app.ChatAsync(new ChatRequestDto(null, "Will this work without a key?"));

            stream.Types[0].ShouldBe(ChatEventTypes.Conversation, stream.Describe());
            stream.Types[^1].ShouldBe(ChatEventTypes.Done, stream.Describe());
            var error = stream.Errors.ShouldHaveSingleItem(stream.Describe());
            error.Code.ShouldBe("model_not_configured");
            error.Message.ShouldNotBeNullOrWhiteSpace();
            stream.All<TextDeltaEvent>(ChatEventTypes.TextDelta).ShouldBeEmpty();
        }
        finally
        {
            await ClearOverridesAsync(app);
        }
    }

    private static async Task ExpectInvalidAsync(KnowledgeAgentApp app, string profile, UpdateModelProfileRequest request, string field)
    {
        using var response = await app.Client.PutJsonAsync($"/api/models/profiles/{profile}", request);
        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.FieldErrors.ShouldContainKey(field);
    }

    private static List<ResolvedModelProfile> CreatedProfiles(KnowledgeAgentApp app)
    {
        lock (app.AiProvider.CreatedChatProfiles)
        {
            return app.AiProvider.CreatedChatProfiles.ToList();
        }
    }

    private static async Task ClearOverridesAsync(KnowledgeAgentApp app)
    {
        foreach (var profile in new[] { "orchestrator", "researcher", "utility", "embedding", "rerank" })
        {
            using var response = await app.Client.DeleteAsync($"/api/models/profiles/{profile}/override", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }
    }
}

public sealed class InMemoryModelScenarios(InMemoryAppFixture fixture) : ModelScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersModelScenarios(ContainersAppFixture fixture) : ModelScenarios(fixture), IClassFixture<ContainersAppFixture>;
