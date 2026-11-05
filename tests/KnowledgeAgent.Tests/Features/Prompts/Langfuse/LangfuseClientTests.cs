using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Prompts.Support;

namespace KnowledgeAgent.Tests.Features.Prompts.Langfuse;

public sealed class LangfuseClientTests : IAsyncLifetime
{
    private FakeLangfuseServer _server = null!;
    private HttpClient _http = null!;
    private LangfuseClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = await FakeLangfuseServer.StartAsync();
        _client = PromptHarness.CreateClient(PromptHarness.LangfuseOptionsFor(_server), out _http);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Gets_a_text_prompt_by_the_production_label_by_default()
    {
        _server.SetTextPrompt("greeting", "Hello {{name}}", new JsonObject { ["model"] = "claude-opus-5-5", ["temperature"] = 0.2 });

        var prompt = await _client.GetPromptAsync("greeting", cancellationToken: Ct);

        prompt.ShouldNotBeNull();
        prompt.Name.ShouldBe("greeting");
        prompt.Version.ShouldBe(1);
        prompt.Type.ShouldBe(PromptType.Text);
        prompt.Text.ShouldBe("Hello {{name}}");
        prompt.Messages.ShouldBeEmpty();
        prompt.Origin.ShouldBe(PromptOrigin.Langfuse);
        prompt.Labels.ShouldBe(["production", "latest"], ignoreOrder: true);
        prompt.Config!["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        prompt.Config["temperature"]!.GetValue<double>().ShouldBe(0.2);
        _server.RequestLog.ShouldContain("GET /api/public/v2/prompts/greeting?label=production");
    }

    [Fact]
    public async Task Gets_a_prompt_by_label_or_version()
    {
        _server.SetTextPrompt("greeting", "v1", null, "production");
        _server.SetTextPrompt("greeting", "v2", null, "staging");

        (await _client.GetPromptAsync("greeting", cancellationToken: Ct))!.Text.ShouldBe("v1");
        (await _client.GetPromptAsync("greeting", label: "staging", cancellationToken: Ct))!.Text.ShouldBe("v2");
        (await _client.GetPromptAsync("greeting", label: "latest", cancellationToken: Ct))!.Version.ShouldBe(2);
        (await _client.GetPromptAsync("greeting", version: 1, cancellationToken: Ct))!.Text.ShouldBe("v1");
        (await _client.GetPromptAsync("greeting", label: "staging", version: 1, cancellationToken: Ct))!.Text.ShouldBe("v1"); // version wins
    }

    [Fact]
    public async Task Returns_null_when_the_prompt_label_or_version_does_not_exist()
    {
        _server.SetTextPrompt("greeting", "v1");

        (await _client.GetPromptAsync("missing", cancellationToken: Ct)).ShouldBeNull();
        (await _client.GetPromptAsync("greeting", label: "staging", cancellationToken: Ct)).ShouldBeNull();
        (await _client.GetPromptAsync("greeting", version: 9, cancellationToken: Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Url_encodes_prompt_names_and_labels()
    {
        _server.SetTextPrompt("team summary", "text", null, "prod & test");

        var prompt = await _client.GetPromptAsync("team summary", label: "prod & test", cancellationToken: Ct);

        prompt!.Name.ShouldBe("team summary");
        _server.RequestLog.ShouldContain("GET /api/public/v2/prompts/team%20summary?label=prod%20%26%20test");
    }

    [Fact]
    public async Task Parses_chat_prompts()
    {
        _server.SetChatPrompt("chat", [("system", "You are {{agent}}."), ("user", "{{question}}")]);

        var prompt = await _client.GetPromptAsync("chat", cancellationToken: Ct);

        prompt!.Type.ShouldBe(PromptType.Chat);
        prompt.Text.ShouldBeNull();
        prompt.Messages.ShouldBe([new PromptMessage("system", "You are {{agent}}."), new PromptMessage("user", "{{question}}")]);
    }

    [Fact]
    public async Task Tolerates_chatmessage_entries_and_skips_placeholders()
    {
        // Langfuse v3 stores typed chat entries, so create one through the raw API like a newer SDK would.
        const string body = """
            {"name":"typed-chat","type":"chat","labels":["production"],"prompt":[
              {"type":"chatmessage","role":"system","content":"Be {{tone}}."},
              {"type":"placeholder","name":"history"},
              {"role":"user","content":"{{question}}"}
            ]}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/public/v2/prompts") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        (await _http.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var prompt = await _client.GetPromptAsync("typed-chat", cancellationToken: Ct);

        prompt!.Messages.ShouldBe([new PromptMessage("system", "Be {{tone}}."), new PromptMessage("user", "{{question}}")]);
    }

    [Fact]
    public async Task Creates_prompt_versions()
    {
        var request = new LangfuseCreatePromptRequest
        {
            Name = "new-prompt",
            Text = "Hi {{name}}",
            Config = new JsonObject { ["profile"] = "utility" },
            Labels = ["production"],
            Tags = ["knowledge-agent"],
            CommitMessage = "first",
        };

        var created = await _client.CreatePromptAsync(request, Ct);
        var second = await _client.CreatePromptAsync(request with { Text = "Hello {{name}}", Labels = ["staging"] }, Ct);

        created.Version.ShouldBe(1);
        created.Origin.ShouldBe(PromptOrigin.Langfuse);
        created.Tags.ShouldBe(["knowledge-agent"]);
        second.Version.ShouldBe(2);
        _server.Versions("new-prompt").ShouldBe([1, 2]);

        var production = await _client.GetPromptAsync("new-prompt", cancellationToken: Ct);
        production!.Text.ShouldBe("Hi {{name}}");
        production.Config!["profile"]!.GetValue<string>().ShouldBe("utility");
        (await _client.GetPromptAsync("new-prompt", label: "staging", cancellationToken: Ct))!.Text.ShouldBe("Hello {{name}}");
    }

    [Fact]
    public async Task Creates_chat_prompts()
    {
        var created = await _client.CreatePromptAsync(
            new LangfuseCreatePromptRequest
            {
                Name = "chat-new",
                Type = PromptType.Chat,
                Messages = [new PromptMessage("system", "S"), new PromptMessage("user", "U")],
                Labels = ["production"],
            },
            Ct);

        created.Type.ShouldBe(PromptType.Chat);
        created.Messages.Select(m => m.Role).ShouldBe(["system", "user"]);
    }

    [Fact]
    public void Builds_the_create_body_with_labels_tags_config_and_commit_message()
    {
        var body = LangfuseJson.BuildCreateBody(new LangfuseCreatePromptRequest
        {
            Name = "p",
            Text = "T",
            Config = new JsonObject { ["model"] = "m" },
            Labels = ["production"],
            Tags = ["knowledge-agent"],
            CommitMessage = "Seeded by Knowledge Agent",
        });

        body.ToJsonString().ShouldBe(
            """{"name":"p","type":"text","prompt":"T","config":{"model":"m"},"labels":["production"],"tags":["knowledge-agent"],"commitMessage":"Seeded by Knowledge Agent"}""");
    }

    [Fact]
    public async Task Lists_prompts_page_by_page()
    {
        for (var i = 0; i < 7; i++)
        {
            _server.SetTextPrompt($"prompt-{i}", "text");
        }

        _server.SetTextPrompt("prompt-0", "second version", new JsonObject { ["model"] = "x" }, "staging");

        var first = await _client.ListPromptsAsync(page: 1, limit: 3, Ct);
        var last = await _client.ListPromptsAsync(page: 3, limit: 3, Ct);

        first.Items.Select(i => i.Name).ShouldBe(["prompt-0", "prompt-1", "prompt-2"]);
        first.TotalItems.ShouldBe(7);
        first.TotalPages.ShouldBe(3);
        first.Page.ShouldBe(1);
        first.Limit.ShouldBe(3);
        first.Items[0].Versions.ShouldBe([1, 2]);
        first.Items[0].Labels.ShouldBe(["production", "staging", "latest"], ignoreOrder: true);
        first.Items[0].LastConfig!["model"]!.GetValue<string>().ShouldBe("x");
        first.Items[0].LastUpdatedAt.ShouldNotBeNull();
        last.Items.Select(i => i.Name).ShouldBe(["prompt-6"]);
    }

    [Fact]
    public async Task Lists_all_prompts_across_pages()
    {
        for (var i = 0; i < 120; i++)
        {
            _server.SetTextPrompt($"p-{i:000}", "text");
        }

        var all = await _client.ListAllPromptsAsync(Ct);

        all.Count.ShouldBe(120);
        all.Select(p => p.Name).Distinct().Count().ShouldBe(120);
        _server.RequestLog.Count(r => r.StartsWith("GET /api/public/v2/prompts?", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public async Task Reads_health_and_projects()
    {
        var health = await _client.GetHealthAsync(Ct);
        var projects = await _client.GetProjectsAsync(Ct);

        health.Status.ShouldBe("OK");
        health.Version.ShouldBe("fake");
        projects.ShouldBe([new LangfuseProject(FakeLangfuseServer.ProjectId, "Fake project")]);
    }

    [Fact]
    public async Task Wrong_credentials_raise_an_unauthorized_error()
    {
        var options = PromptHarness.LangfuseOptionsFor(_server);
        options.SecretKey = "sk-lf-wrong";
        var client = PromptHarness.CreateClient(options, out var http);
        using var _ = http;
        _server.SetTextPrompt("greeting", "text");

        var get = await Should.ThrowAsync<LangfuseApiException>(() => client.GetPromptAsync("greeting", cancellationToken: Ct));
        var list = await Should.ThrowAsync<LangfuseApiException>(() => client.ListPromptsAsync(cancellationToken: Ct));
        var create = await Should.ThrowAsync<LangfuseApiException>(() => client.CreatePromptAsync(new LangfuseCreatePromptRequest { Name = "x", Text = "y" }, Ct));

        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        get.Message.ShouldContain("401");
        get.Message.ShouldContain("Invalid credentials");
        get.Message.ShouldNotContain("sk-lf-wrong");
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        create.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetHealthAsync(Ct)).Status.ShouldBe("OK"); // health needs no credentials
    }

    [Fact]
    public async Task Outages_raise_service_unavailable()
    {
        _server.Unavailable = true;

        var exception = await Should.ThrowAsync<LangfuseApiException>(() => _client.GetPromptAsync("greeting", cancellationToken: Ct));

        exception.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await Should.ThrowAsync<LangfuseApiException>(() => _client.GetHealthAsync(Ct));
    }

    [Fact]
    public async Task An_unconfigured_client_explains_what_is_missing()
    {
        var client = PromptHarness.CreateClient(new LangfuseOptions(), out var http);
        using var _ = http;

        var exception = await Should.ThrowAsync<LangfuseApiException>(() => client.GetPromptAsync("x", cancellationToken: Ct));

        exception.Message.ShouldContain("Langfuse:BaseUrl");
    }

    [Fact]
    public void Configures_base_address_with_path_prefix_and_basic_auth()
    {
        using var http = new HttpClient();

        LangfuseClient.ConfigureHttpClient(http, new LangfuseOptions { BaseUrl = "https://example.com/langfuse", PublicKey = "pk", SecretKey = "sk" });

        http.BaseAddress.ShouldBe(new Uri("https://example.com/langfuse/"));
        http.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Basic");
        http.DefaultRequestHeaders.Authorization.Parameter.ShouldBe(Convert.ToBase64String(Encoding.UTF8.GetBytes("pk:sk")));
    }

    [Theory]
    [InlineData("langfuse:3000")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    public async Task Invalid_base_urls_leave_the_client_unconfigured(string baseUrl)
    {
        var client = PromptHarness.CreateClient(new LangfuseOptions { BaseUrl = baseUrl, PublicKey = "pk", SecretKey = "sk" }, out var http);
        using var _ = http;

        http.BaseAddress.ShouldBeNull();
        (await Should.ThrowAsync<LangfuseApiException>(() => client.GetHealthAsync(Ct))).Message.ShouldContain("absolute URL");
    }

    [Fact]
    public void Parses_prompt_payload_variants()
    {
        var textWithoutConfig = LangfuseJson.ParsePrompt(JsonNode.Parse("""{"name":"t","version":4,"type":"text","prompt":"x","config":null,"labels":["a"],"tags":[]}"""));
        var chatAsString = LangfuseJson.ParsePrompt(JsonNode.Parse("""{"name":"c","type":"chat","prompt":"only system"}"""));

        textWithoutConfig.Version.ShouldBe(4);
        textWithoutConfig.Config.ShouldBeNull();
        textWithoutConfig.Labels.ShouldBe(["a"]);
        chatAsString.Messages.ShouldBe([new PromptMessage("system", "only system")]);
        Should.Throw<LangfuseApiException>(() => LangfuseJson.ParsePrompt(JsonNode.Parse("[]")));
        Should.Throw<LangfuseApiException>(() => LangfuseJson.ParsePrompt(JsonNode.Parse("""{"version":1}""")));
    }
}
