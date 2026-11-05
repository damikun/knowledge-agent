using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Prompts;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class PromptEditScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_draft_stays_unused_until_published_and_a_roll_back_brings_the_old_version_back_at_once()
    {
        // A long cache, so the renders below prove that publishing clears it.
        await using var app = await fixture.CreateAppAsync(options => options.Settings["Prompts:CacheTtlSeconds"] = "3600");
        var name = $"e2e-edit-{ApiClient.UniqueWord()}";
        app.Langfuse.SetTextPrompt(name, "Hello {{name}}.");
        (await RenderAsync(app, name)).ShouldBe("Hello Ada.");

        using (var saved = await app.Client.PostJsonAsync($"/api/prompts/{name}/versions", new SavePromptDraftRequest("Hi {{name}}!", CommitMessage: "Friendlier")))
        {
            var draft = await saved.ReadJsonAsync<PromptVersionDto>(HttpStatusCode.Created);
            draft.Version.ShouldBe(2);
            draft.Live.ShouldBeFalse();
            draft.Text.ShouldBe("Hi {{name}}!");
            draft.CommitMessage.ShouldBe("Friendlier (by Local admin)");
        }

        app.Langfuse.LabelsOf(name, 2).ShouldNotContain("production");
        (await RenderAsync(app, name)).ShouldBe("Hello Ada.", "a draft is not used");

        var versions = await app.Client.GetJsonAsync<PromptVersionsDto>($"/api/prompts/{name}/versions");
        versions.ShouldSatisfyAllConditions(
            list => list.Editable.ShouldBeTrue(),
            list => list.Label.ShouldBe("production"),
            list => list.LiveVersion.ShouldBe(1),
            list => list.TotalVersions.ShouldBe(2),
            list => list.Versions.Select(version => (version.Version, version.Live)).ShouldBe([(2, false), (1, true)]));

        using (var published = await app.Client.PostJsonAsync($"/api/prompts/{name}/publish", new PublishPromptRequest(2, ExpectedLiveVersion: 1)))
        {
            (await published.ReadJsonAsync<PublishPromptResponse>()).ShouldBe(new PublishPromptResponse(name, "production", 2, 1));
        }

        app.Langfuse.LabelsOf(name, 2).ShouldContain("production");
        app.Langfuse.LabelsOf(name, 1).ShouldNotContain("production");
        (await RenderAsync(app, name)).ShouldBe("Hi Ada!", "the new version is used at once");
        (await app.Client.GetJsonAsync<PromptDetailDto>($"/api/prompts/{name}")).Version.ShouldBe(2);

        using (var stale = await app.Client.PostJsonAsync($"/api/prompts/{name}/publish", new PublishPromptRequest(1, ExpectedLiveVersion: 1)))
        {
            await stale.ReadProblemAsync(HttpStatusCode.Conflict);
        }

        using (var rolledBack = await app.Client.PostJsonAsync($"/api/prompts/{name}/publish", new PublishPromptRequest(1, ExpectedLiveVersion: 2)))
        {
            (await rolledBack.ReadJsonAsync<PublishPromptResponse>()).PreviousVersion.ShouldBe(2);
        }

        (await RenderAsync(app, name)).ShouldBe("Hello Ada.");

        using var missing = await app.Client.PostJsonAsync($"/api/prompts/{name}/publish", new PublishPromptRequest(99));
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Drafts_are_checked_before_they_reach_Langfuse()
    {
        var app = await fixture.GetAppAsync();
        var name = $"e2e-edit-check-{ApiClient.UniqueWord()}";
        app.Langfuse.SetTextPrompt(name, "Hello {{name}}.");

        await ExpectFieldErrorAsync(app, name, new SavePromptDraftRequest("{{#if open}}never closed"), "text");
        await ExpectFieldErrorAsync(app, name, new SavePromptDraftRequest("Hello {{name}}."), "text");
        await ExpectFieldErrorAsync(app, name, new SavePromptDraftRequest(Text: null), "text");
        app.Langfuse.Versions(name).ShouldBe([1], "nothing was saved");

        using (var missing = await app.Client.PostJsonAsync($"/api/prompts/e2e-missing-{Guid.NewGuid():N}/versions", new SavePromptDraftRequest("Hi")))
        {
            await missing.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var missingVersions = await app.Client.GetAsync($"/api/prompts/e2e-missing-{Guid.NewGuid():N}/versions", Ct))
        {
            await missingVersions.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        var chatName = $"e2e-edit-chat-{ApiClient.UniqueWord()}";
        app.Langfuse.SetChatPrompt(chatName, [("system", "You help {{user}}."), ("user", "{{question}}")]);
        await ExpectFieldErrorAsync(app, chatName, new SavePromptDraftRequest(Messages: [new("boss", "Hi"), new("user", "{{question}}")]), "messages[0].role");
        await ExpectFieldErrorAsync(app, chatName, new SavePromptDraftRequest(Messages: [new("system", "{{#each x}}"), new("user", "{{question}}")]), "messages[0].content");

        using var chat = await app.Client.PostJsonAsync(
            $"/api/prompts/{chatName}/versions",
            new SavePromptDraftRequest(Messages: [new("system", "You help {{user}} kindly."), new("user", "{{question}}")]));
        var draft = await chat.ReadJsonAsync<PromptVersionDto>(HttpStatusCode.Created);
        draft.Type.ShouldBe("chat");
        draft.Messages[0].ShouldBe(new PromptMessageDto("system", "You help {{user}} kindly."));
        draft.CommitMessage.ShouldBe("Edited in the portal (by Local admin)");
    }

    private static async Task ExpectFieldErrorAsync(KnowledgeAgentApp app, string name, SavePromptDraftRequest request, string field)
    {
        using var response = await app.Client.PostJsonAsync($"/api/prompts/{name}/versions", request);
        (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey(field);
    }

    private static async Task<string?> RenderAsync(KnowledgeAgentApp app, string name)
    {
        using var response = await app.Client.PostJsonAsync($"/api/prompts/{name}/render", new RenderPromptRequest(PromptScenarios.Variables(new { name = "Ada" })));
        return (await response.ReadJsonAsync<RenderedPromptDto>()).Text;
    }
}

public sealed class InMemoryPromptEditScenarios(InMemoryAppFixture fixture) : PromptEditScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersPromptEditScenarios(ContainersAppFixture fixture) : PromptEditScenarios(fixture), IClassFixture<ContainersAppFixture>;
