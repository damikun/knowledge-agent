using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Prompts;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class PromptScenarios(AppFixture fixture)
{
    private static readonly TimeSpan SeedTimeout = TimeSpan.FromSeconds(20);

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Default_prompts_are_listed_and_seeded_into_Langfuse()
    {
        var app = await fixture.GetAppAsync();

        foreach (var name in PromptNames.All)
        {
            (await ApiClient.EventuallyAsync(() => app.Langfuse.HasPrompt(name), SeedTimeout))
                .ShouldBeTrue($"'{name}' was not seeded into Langfuse.{Environment.NewLine}{app.Logs.Dump()}");
        }

        var prompts = await app.Client.GetJsonAsync<PromptSummaryDto[]>("/api/prompts");
        PromptNames.All.ShouldBeSubsetOf(prompts.Select(p => p.Name));

        var orchestrator = prompts.Single(p => p.Name == PromptNames.OrchestratorSystem);
        orchestrator.Origin.ShouldBe("langfuse");
        orchestrator.Version.ShouldBeGreaterThanOrEqualTo(1);
        orchestrator.Type.ShouldBeOneOf("text", "chat");
        orchestrator.Labels.ShouldContain("production");
        orchestrator.Variables.ShouldNotBeEmpty();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Prompt_detail_returns_the_raw_template_with_variables_and_config()
    {
        var app = await fixture.GetAppAsync();
        var name = $"e2e-detail-{ApiClient.UniqueWord()}";
        const string template = "Hello {{name}}, you have {{count}} new {{#if urgent}}URGENT {{/if}}messages.";
        app.Langfuse.SetTextPrompt(name, template, new JsonObject { ["model"] = "detail-model" });

        var detail = await app.Client.GetJsonAsync<PromptDetailDto>($"/api/prompts/{name}");

        detail.Name.ShouldBe(name);
        detail.Origin.ShouldBe("langfuse");
        detail.Version.ShouldBe(1);
        detail.Type.ShouldBe("text");
        detail.Text.ShouldBe(template);
        detail.Messages.ShouldBeEmpty();
        detail.Labels.ShouldContain("production");
        detail.Variables.ShouldBe(new[] { "count", "name", "urgent" });
        detail.Config.ShouldNotBeNull()["model"]!.GetValue<string>().ShouldBe("detail-model");

        var seeded = await app.Client.GetJsonAsync<PromptDetailDto>($"/api/prompts/{PromptNames.OrchestratorSystem}");
        (seeded.Text ?? string.Join('\n', seeded.Messages.Select(m => m.Content))).ShouldNotBeNullOrWhiteSpace();

        using var missing = await app.Client.GetAsync($"/api/prompts/e2e-missing-{Guid.NewGuid():N}", TestContext.Current.CancellationToken);
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Prompt_render_applies_Handlebars_variables_and_reports_the_model_config()
    {
        var app = await fixture.GetAppAsync();
        var name = $"e2e-greeting-{ApiClient.UniqueWord()}";
        app.Langfuse.SetTextPrompt(
            name,
            "Hello {{name}}! {{#if formal}}Kind regards.{{else}}Cheers!{{/if}} {{#each topics}}[{{@index}}:{{this}}]{{/each}}",
            new JsonObject { ["model"] = "render-model", ["temperature"] = 0.2 });

        using var response = await app.Client.PostJsonAsync(
            $"/api/prompts/{name}/render",
            new RenderPromptRequest(Variables(new { name = "Ada", formal = true, topics = new[] { "sql", "ai" } })));
        var rendered = await response.ReadJsonAsync<RenderedPromptDto>();

        rendered.Name.ShouldBe(name);
        rendered.Origin.ShouldBe("langfuse");
        rendered.Version.ShouldBe(1);
        rendered.Type.ShouldBe("text");
        rendered.Text.ShouldBe("Hello Ada! Kind regards. [0:sql][1:ai]");
        var modelOverride = rendered.ModelOverride.ShouldNotBeNull();
        modelOverride.Model.ShouldBe("render-model");
        modelOverride.Temperature.ShouldNotBeNull().ShouldBe(0.2f, 0.0001f);

        app.Langfuse.SetTextPrompt(name, "Staging greeting for {{name}}", config: null, "staging");
        using var staging = await app.Client.PostJsonAsync(
            $"/api/prompts/{name}/render",
            new RenderPromptRequest(Variables(new { name = "Ada" }), Label: "staging"));
        var stagingRendered = await staging.ReadJsonAsync<RenderedPromptDto>();
        stagingRendered.Text.ShouldBe("Staging greeting for Ada");
        stagingRendered.Version.ShouldBe(2);

        using var production = await app.Client.PostJsonAsync($"/api/prompts/{name}/render", new RenderPromptRequest(Variables(new { name = "Bo", formal = false })));
        (await production.ReadJsonAsync<RenderedPromptDto>()).Text.ShouldBe("Hello Bo! Cheers! ");

        var chatName = $"e2e-chat-{ApiClient.UniqueWord()}";
        app.Langfuse.SetChatPrompt(chatName, [("system", "You help {{user}}."), ("user", "{{question}}")]);
        using var chat = await app.Client.PostJsonAsync(
            $"/api/prompts/{chatName}/render",
            new RenderPromptRequest(Variables(new { user = "Ada", question = "Why is the sky blue?" })));
        var chatRendered = await chat.ReadJsonAsync<RenderedPromptDto>();
        chatRendered.Type.ShouldBe("chat");
        chatRendered.Messages.ShouldBe(new[]
        {
            new PromptMessageDto("system", "You help Ada."),
            new PromptMessageDto("user", "Why is the sky blue?"),
        });

        using var missing = await app.Client.PostJsonAsync($"/api/prompts/e2e-missing-{Guid.NewGuid():N}/render", new RenderPromptRequest());
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Adhoc_templates_render_with_helpers_and_template_errors_are_bad_requests()
    {
        var app = await fixture.GetAppAsync();

        using var response = await app.Client.PostJsonAsync(
            "/api/prompts/render",
            new RenderTemplateRequest(
                "{{upper name}}: {{#each items}}{{@index}}={{this}};{{/each}}{{#if (eq tone \"formal\")}} Sincerely.{{/if}}",
                Variables(new { name = "ada", items = new[] { "a", "b" }, tone = "formal" })));
        var rendered = await response.ReadJsonAsync<RenderTemplateResponse>();
        rendered.Output.ShouldBe("ADA: 0=a;1=b; Sincerely.");
        rendered.Variables.ShouldContain("name");
        rendered.Variables.ShouldContain("items");
        rendered.Variables.ShouldContain("tone");
        rendered.Variables.ShouldNotContain("upper");
        rendered.Variables.ShouldNotContain("eq");

        using (var broken = await app.Client.PostJsonAsync("/api/prompts/render", new RenderTemplateRequest("{{#if open}}never closed")))
        {
            await broken.ReadProblemAsync(HttpStatusCode.BadRequest);
        }

        using var missing = await app.Client.PostJsonAsync("/api/prompts/render", new { variables = new { } });
        (await missing.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("template");
    }

    internal static IReadOnlyDictionary<string, JsonElement> Variables(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values, TestJson.Options), TestJson.Options)!;
}

public sealed class InMemoryPromptScenarios(InMemoryAppFixture fixture) : PromptScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersPromptScenarios(ContainersAppFixture fixture) : PromptScenarios(fixture), IClassFixture<ContainersAppFixture>;
