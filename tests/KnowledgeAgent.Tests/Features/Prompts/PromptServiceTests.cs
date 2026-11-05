using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Prompts.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Prompts;

public sealed class PromptServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero));
    private readonly List<PromptHarness> _harnesses = [];
    private FakeLangfuseServer _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _server = await FakeLangfuseServer.StartAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var harness in _harnesses)
        {
            harness.Dispose();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Serves_prompts_from_langfuse()
    {
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "Custom {{appName}}");
        var service = Harness().Service;

        var template = await service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct);

        template.Origin.ShouldBe(PromptOrigin.Langfuse);
        template.Version.ShouldBe(1);
        template.Text.ShouldBe("Custom {{appName}}");
    }

    [Fact]
    public async Task Uses_the_configured_default_label_and_honours_selectors()
    {
        _server.SetTextPrompt("greeting", "production text", null, "production");
        _server.SetTextPrompt("greeting", "staging text", null, "staging");
        var service = Harness(prompts => prompts.Label = "staging").Service;

        (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("staging text");
        (await service.GetTemplateAsync("greeting", new PromptSelector(Label: "production"), Ct)).Text.ShouldBe("production text");
        (await service.GetTemplateAsync("greeting", new PromptSelector(Version: 1), Ct)).Text.ShouldBe("production text");
    }

    [Fact]
    public async Task Caches_templates_for_the_configured_ttl()
    {
        _server.SetTextPrompt("greeting", "v1");
        var service = Harness(prompts => prompts.CacheTtlSeconds = 60).Service;

        (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("v1");
        _server.SetTextPrompt("greeting", "v2");
        _time.Advance(TimeSpan.FromSeconds(59));
        (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("v1");
        PromptRequests("greeting").ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(2));
        var refreshed = await service.GetTemplateAsync("greeting", cancellationToken: Ct);

        refreshed.Text.ShouldBe("v2");
        refreshed.Version.ShouldBe(2);
        PromptRequests("greeting").ShouldBe(2);
    }

    [Fact]
    public async Task Caches_each_label_and_version_separately()
    {
        _server.SetTextPrompt("greeting", "v1", null, "production");
        _server.SetTextPrompt("greeting", "v2", null, "staging");
        var service = Harness().Service;

        for (var i = 0; i < 2; i++)
        {
            (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("v1");
            (await service.GetTemplateAsync("greeting", new PromptSelector(Label: "staging"), Ct)).Text.ShouldBe("v2");
            (await service.GetTemplateAsync("greeting", new PromptSelector(Version: 2), Ct)).Text.ShouldBe("v2");
        }

        PromptRequests("greeting").ShouldBe(3);
    }

    [Fact]
    public async Task A_zero_ttl_disables_caching()
    {
        _server.SetTextPrompt("greeting", "v1");
        var service = Harness(prompts => prompts.CacheTtlSeconds = 0).Service;

        await service.GetTemplateAsync("greeting", cancellationToken: Ct);
        await service.GetTemplateAsync("greeting", cancellationToken: Ct);
        _server.SetTextPrompt("greeting", "v2");

        (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("v2");
        PromptRequests("greeting").ShouldBe(3);
    }

    [Fact]
    public async Task Serves_the_last_good_copy_during_an_outage_and_retries_after_a_backoff()
    {
        var logs = new CapturingLoggerProvider();
        _server.SetTextPrompt("greeting", "v1");
        var service = Harness(prompts => prompts.CacheTtlSeconds = 60, loggerFactory: logs.CreateFactory()).Service;
        await service.GetTemplateAsync("greeting", cancellationToken: Ct);
        _server.SetTextPrompt("greeting", "v2");

        _time.Advance(TimeSpan.FromSeconds(61));
        _server.Unavailable = true;
        var stale = await service.GetTemplateAsync("greeting", cancellationToken: Ct);
        var duringBackoff = await service.GetTemplateAsync("greeting", cancellationToken: Ct);

        stale.Text.ShouldBe("v1");
        stale.Origin.ShouldBe(PromptOrigin.Langfuse);
        duringBackoff.Text.ShouldBe("v1");
        PromptRequests("greeting").ShouldBe(2); // the second call during the backoff did not hit Langfuse
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("serving the cached copy"));

        _server.Unavailable = false;
        _time.Advance(TimeSpan.FromSeconds(11));
        (await service.GetTemplateAsync("greeting", cancellationToken: Ct)).Text.ShouldBe("v2");
    }

    [Fact]
    public async Task Falls_back_to_the_local_default_when_the_prompt_is_missing_in_langfuse()
    {
        var harness = Harness();

        var template = await harness.Service.GetTemplateAsync(PromptNames.ResearcherSystem, cancellationToken: Ct);

        harness.Local.TryGet(PromptNames.ResearcherSystem, out var local).ShouldBeTrue();
        template.Origin.ShouldBe(PromptOrigin.Local);
        template.Version.ShouldBe(1);
        template.Text.ShouldBe(local!.Text);
        PromptRequests(PromptNames.ResearcherSystem).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_prompts_without_a_local_default_are_not_found()
    {
        var service = Harness().Service;

        var exception = await Should.ThrowAsync<PromptNotFoundException>(() => service.GetTemplateAsync("unknown", cancellationToken: Ct));

        exception.Name.ShouldBe("unknown");
    }

    [Fact]
    public async Task Missing_prompts_are_not_found_when_the_local_fallback_is_disabled()
    {
        var service = Harness(prompts => prompts.FallbackToLocal = false).Service;

        await Should.ThrowAsync<PromptNotFoundException>(() => service.GetTemplateAsync(PromptNames.ConversationTitle, cancellationToken: Ct));
    }

    [Fact]
    public async Task Falls_back_to_the_local_default_when_langfuse_is_down()
    {
        _server.Unavailable = true;
        var service = Harness().Service;

        var template = await service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct);
        await service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct);

        template.Origin.ShouldBe(PromptOrigin.Local);
        PromptRequests(PromptNames.OrchestratorSystem).ShouldBe(1); // the failure is remembered for a short backoff

        _server.Unavailable = false;
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "From Langfuse");
        _time.Advance(TimeSpan.FromSeconds(11));
        (await service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct)).Origin.ShouldBe(PromptOrigin.Langfuse);
    }

    [Fact]
    public async Task Falls_back_to_the_local_default_when_credentials_are_rejected()
    {
        _server.SetTextPrompt(PromptNames.ConversationTitle, "From Langfuse");
        var logs = new CapturingLoggerProvider();
        var service = Harness(langfuse: options => options.SecretKey = "sk-lf-wrong-secret", loggerFactory: logs.CreateFactory()).Service;

        var template = await service.GetTemplateAsync(PromptNames.ConversationTitle, cancellationToken: Ct);

        template.Origin.ShouldBe(PromptOrigin.Local);
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("401"));
        logs.Entries.ShouldNotContain(e => e.Message.Contains("sk-lf-wrong-secret") || e.Message.Contains(_server.PublicKey + ":"));
    }

    [Fact]
    public async Task Outages_without_cache_or_fallback_raise_prompt_store_unavailable()
    {
        _server.Unavailable = true;
        var noFallback = Harness(prompts => prompts.FallbackToLocal = false).Service;
        var withFallback = Harness().Service;

        var disabled = await Should.ThrowAsync<PromptStoreUnavailableException>(() =>
            noFallback.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct));
        var unknown = await Should.ThrowAsync<PromptStoreUnavailableException>(() =>
            withFallback.GetTemplateAsync("unknown", cancellationToken: Ct));

        disabled.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        disabled.Message.ShouldContain("local fallback is disabled");
        unknown.Message.ShouldContain("no local default");
    }

    [Fact]
    public async Task Local_store_never_calls_langfuse()
    {
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "From Langfuse");
        var harness = Harness(prompts => prompts.Store = "Local");

        var template = await harness.Service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct);
        var list = await harness.Service.ListAsync(Ct);

        harness.Service.UsesLangfuse.ShouldBeFalse();
        template.Origin.ShouldBe(PromptOrigin.Local);
        list.ShouldAllBe(summary => summary.Origin == PromptOrigin.Local && summary.ExternalUrl == null);
        _server.RequestLog.ShouldBeEmpty();
        await Should.ThrowAsync<PromptNotFoundException>(() => harness.Service.GetTemplateAsync("unknown", cancellationToken: Ct));
    }

    [Fact]
    public async Task Unconfigured_langfuse_means_local_prompts()
    {
        var harness = new PromptHarness(server: null, timeProvider: _time);
        _harnesses.Add(harness);

        var template = await harness.Service.GetTemplateAsync(PromptNames.ConversationTitle, cancellationToken: Ct);

        harness.Service.UsesLangfuse.ShouldBeFalse();
        template.Origin.ShouldBe(PromptOrigin.Local);
    }

    [Fact]
    public async Task Renders_text_prompts_and_parses_the_model_config()
    {
        var config = new JsonObject
        {
            ["profile"] = "researcher",
            ["provider"] = "anthropic",
            ["model"] = "claude-opus-5-5",
            ["temperature"] = 0.3,
            ["max_tokens"] = 1024,
            ["reasoningEffort"] = "high",
            ["reasoningOutput"] = "summary",
        };
        _server.SetTextPrompt("summary", "Summarize for {{user.name}}{{#if short}} briefly{{/if}}.", config);
        var service = Harness().Service;

        var rendered = await service.RenderAsync("summary", new Dictionary<string, object?> { ["user"] = new { name = "Ann" }, ["short"] = true }, cancellationToken: Ct);

        rendered.Name.ShouldBe("summary");
        rendered.Type.ShouldBe(PromptType.Text);
        rendered.Origin.ShouldBe(PromptOrigin.Langfuse);
        rendered.Version.ShouldBe(1);
        rendered.Text.ShouldBe("Summarize for Ann briefly.");
        rendered.Profile.ShouldBe("researcher");
        rendered.ModelPatch.ShouldNotBeNull();
        rendered.ModelPatch.Provider.ShouldBe("anthropic");
        rendered.ModelPatch.Model.ShouldBe("claude-opus-5-5");
        rendered.ModelPatch.Temperature.ShouldBe(0.3f);
        rendered.ModelPatch.MaxOutputTokens.ShouldBe(1024);
        rendered.ModelPatch.ReasoningEffort.ShouldBe("high");
        rendered.ModelPatch.ReasoningOutput.ShouldBe("summary");
        rendered.Config!["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
    }

    [Fact]
    public async Task Renders_every_message_of_chat_prompts()
    {
        _server.SetChatPrompt("chat", [("system", "You are {{agent}}."), ("user", "{{upper question}}")], new JsonObject { ["profile"] = "utility" });
        var service = Harness().Service;

        var rendered = await service.RenderAsync("chat", new Dictionary<string, object?> { ["agent"] = "Aria", ["question"] = "why?" }, cancellationToken: Ct);

        rendered.Type.ShouldBe(PromptType.Chat);
        rendered.Text.ShouldBeNull();
        rendered.Messages.ShouldBe([new PromptMessage("system", "You are Aria."), new PromptMessage("user", "WHY?")]);
        rendered.Profile.ShouldBe("utility");
        rendered.ModelPatch.ShouldBeNull();
    }

    [Fact]
    public async Task Local_defaults_render_without_a_model_patch()
    {
        var service = Harness(prompts => prompts.Store = "Local").Service;

        var rendered = await service.RenderAsync(PromptNames.ConversationTitle, new Dictionary<string, object?> { ["message"] = "Hello" }, cancellationToken: Ct);

        rendered.Origin.ShouldBe(PromptOrigin.Local);
        rendered.Version.ShouldBe(1);
        rendered.Text!.ShouldEndWith("Message:\nHello");
        rendered.ModelPatch.ShouldBeNull();
        rendered.Profile.ShouldBeNull();
        rendered.Config.ShouldNotBeNull().Count.ShouldBe(0);
    }

    [Fact]
    public async Task Render_errors_name_the_prompt_and_version()
    {
        _server.SetTextPrompt("broken", "{{#if x}}never closed");
        _server.SetTextPrompt("broken", "{{#if x}}still never closed");
        var service = Harness().Service;

        var exception = await Should.ThrowAsync<PromptRenderException>(() => service.RenderAsync("broken", new Dictionary<string, object?>(), cancellationToken: Ct));

        exception.Message.ShouldContain("'broken'");
        exception.Message.ShouldContain("version 2");
        exception.Message.ShouldContain("syntax error");
    }

    [Fact]
    public async Task Returned_configs_are_independent_copies()
    {
        _server.SetTextPrompt("greeting", "x", new JsonObject { ["model"] = "a" });
        var service = Harness().Service;

        var first = await service.GetTemplateAsync("greeting", cancellationToken: Ct);
        first.Config!["model"] = "changed";
        var second = await service.GetTemplateAsync("greeting", cancellationToken: Ct);

        second.Config!["model"]!.GetValue<string>().ShouldBe("a");
    }

    [Fact]
    public void Renders_ad_hoc_templates()
    {
        var service = Harness(prompts => prompts.Store = "Local").Service;

        service.RenderTemplate("{{#each items}}{{upper this}}{{#unless @last}}, {{/unless}}{{/each}}", new Dictionary<string, object?> { ["items"] = new[] { "a", "b" } })
            .ShouldBe("A, B");
        Should.Throw<PromptRenderException>(() => service.RenderTemplate("{{#if}}", new Dictionary<string, object?>()));
        Should.Throw<PromptRenderException>(() => service.RenderTemplate("{{#each items}}", new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task Lists_local_defaults_and_langfuse_prompts_with_links_and_variables()
    {
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "old");
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "Hello {{user}} {{#each items}}{{title}}{{/each}}");
        _server.SetChatPrompt("custom-chat", [("system", "{{a}}"), ("user", "{{b}} {{a}}")]);
        _server.SetTextPrompt("staging-only", "{{draft}}", null, "staging");
        var service = Harness().Service;

        var list = await service.ListAsync(Ct);

        list.Select(s => s.Name).ShouldBe(PromptNames.All.Concat(["custom-chat", "staging-only"]).Order(StringComparer.Ordinal).ToArray());

        var orchestrator = list.Single(s => s.Name == PromptNames.OrchestratorSystem);
        orchestrator.Origin.ShouldBe(PromptOrigin.Langfuse);
        orchestrator.Version.ShouldBe(2);
        orchestrator.Labels.ShouldBe(["production", "latest"], ignoreOrder: true);
        orchestrator.Variables.ShouldBe(["items", "user"]);
        orchestrator.ExternalUrl.ShouldBe($"{_server.BaseAddress.ToString().TrimEnd('/')}/project/{FakeLangfuseServer.ProjectId}/prompts/orchestrator-system");

        var chat = list.Single(s => s.Name == "custom-chat");
        chat.Type.ShouldBe(PromptType.Chat);
        chat.Variables.ShouldBe(["a", "b"]);

        var stagingOnly = list.Single(s => s.Name == "staging-only");
        stagingOnly.Origin.ShouldBe(PromptOrigin.Langfuse);
        stagingOnly.Labels.ShouldBe(["staging", "latest"], ignoreOrder: true);
        stagingOnly.Variables.ShouldBe(["draft"]);

        var researcher = list.Single(s => s.Name == PromptNames.ResearcherSystem);
        researcher.Origin.ShouldBe(PromptOrigin.Local);
        researcher.Version.ShouldBe(1);
        researcher.Labels.ShouldBe(["production"]);
        researcher.Variables.ShouldBe(["appName", "focusSources", "maxSearches", "today"]);
        researcher.ExternalUrl.ShouldBeNull();

        _server.RequestLog.Count(r => r.StartsWith("GET /api/public/projects", StringComparison.Ordinal)).ShouldBe(1);
        await service.ListAsync(Ct);
        _server.RequestLog.Count(r => r.StartsWith("GET /api/public/projects", StringComparison.Ordinal)).ShouldBe(1); // project id is cached
    }

    [Fact]
    public async Task Deep_links_prefer_the_public_url_and_configured_project_id()
    {
        _server.SetTextPrompt("team/summary", "x");
        var service = Harness(langfuse: options =>
        {
            options.PublicUrl = "https://langfuse.example.com/";
            options.ProjectId = "proj-42";
        }).Service;

        var summary = (await service.ListAsync(Ct)).Single(s => s.Name == "team/summary");

        summary.ExternalUrl.ShouldBe("https://langfuse.example.com/project/proj-42/prompts/team%2Fsummary");
        _server.RequestLog.ShouldNotContain(r => r.StartsWith("GET /api/public/projects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Listing_tolerates_langfuse_outages()
    {
        _server.Unavailable = true;
        var service = Harness().Service;

        var list = await service.ListAsync(Ct);

        list.Select(s => s.Name).ShouldBe(PromptNames.All.Order(StringComparer.Ordinal).ToArray());
        list.ShouldAllBe(s => s.Origin == PromptOrigin.Local);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_langfuse_call()
    {
        using var handler = new GatedJsonHandler("""{"name":"greeting","version":3,"type":"text","prompt":"Hi","config":{},"labels":["production"],"tags":[]}""");
        var harness = new PromptHarness(
            server: null,
            configureLangfuse: options =>
            {
                options.BaseUrl = "http://langfuse.test";
                options.PublicKey = "pk";
                options.SecretKey = "sk";
            },
            timeProvider: _time,
            handler: handler);
        _harnesses.Add(harness);

        var requests = Enumerable.Range(0, 10).Select(_ => harness.Service.GetTemplateAsync("greeting", cancellationToken: Ct)).ToArray();
        (await Eventually.TrueAsync(() => handler.Calls > 0, TimeSpan.FromSeconds(5), Ct)).ShouldBeTrue();
        handler.Release();
        var templates = await Task.WhenAll(requests);

        templates.ShouldAllBe(t => t.Version == 3 && t.Text == "Hi");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_treated_as_an_outage()
    {
        using var handler = new GatedJsonHandler("{}");
        var harness = new PromptHarness(
            server: null,
            configureLangfuse: options =>
            {
                options.BaseUrl = "http://langfuse.test";
                options.PublicKey = "pk";
                options.SecretKey = "sk";
            },
            timeProvider: _time,
            handler: handler);
        _harnesses.Add(harness);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            harness.Service.GetTemplateAsync(PromptNames.OrchestratorSystem, cancellationToken: cancellation.Token));
    }

    private PromptHarness Harness(
        Action<PromptOptions>? prompts = null,
        Action<LangfuseOptions>? langfuse = null,
        ILoggerFactory? loggerFactory = null)
    {
        var harness = new PromptHarness(_server, prompts, langfuse, _time, loggerFactory: loggerFactory);
        _harnesses.Add(harness);
        return harness;
    }

    private int PromptRequests(string name) =>
        _server.RequestLog.Count(r => r.StartsWith($"GET /api/public/v2/prompts/{name}?", StringComparison.Ordinal));
}
