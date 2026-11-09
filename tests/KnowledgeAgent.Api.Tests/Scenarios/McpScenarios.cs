using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Agents.Access;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Connections.Mcp;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class McpScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Admins_connect_an_mcp_server_whose_tools_an_agent_then_uses()
    {
        var app = await fixture.GetAppAsync();
        await using var shop = await TestMcpServer.StartAsync();
        shop.RequiredToken = "shop-token-1234567";

        McpServerDto server;
        using (var create = await app.Client.PostJsonAsync(
            "/api/connections/mcp",
            new SaveMcpServerRequest("Shop", shop.Url.ToString(), McpAuthType.Bearer, Secret: "shop-token-1234567")))
        {
            (await create.Content.ReadAsStringAsync(Ct)).ShouldNotContain("shop-token-1234567", Case.Sensitive, "secrets are write-only");
            server = await create.ReadJsonAsync<McpServerDto>(HttpStatusCode.Created);
        }

        server.HasSecret.ShouldBeTrue();
        server.LastCheck.ShouldNotBeNull().Ok.ShouldBeTrue(server.LastCheck.Error);
        server.ServerName.ShouldBe("test-shop");
        var lookup = server.Tools.Single(tool => tool.Name == "lookup_order");
        lookup.ReadOnly.ShouldBe(true);
        lookup.DefaultPolicy.ShouldBe(McpToolPolicy.Auto);
        server.Tools.Single(tool => tool.Name == "cancel_order").DefaultPolicy.ShouldBe(McpToolPolicy.Confirm);

        using (var test = await app.Client.PostAsync($"/api/connections/mcp/{server.Id}/test", null, Ct))
        {
            (await test.ReadJsonAsync<McpTestResultDto>()).Ok.ShouldBeTrue();
        }

        using (var draft = await app.Client.PostJsonAsync("/api/connections/mcp/test", new SaveMcpServerRequest("Shop", shop.Url.ToString())))
        {
            var result = await draft.ReadJsonAsync<McpTestResultDto>();
            result.Ok.ShouldBeFalse("the draft has no token");
            result.Message.ShouldContain("401");
        }

        var key = ApiClient.UniqueWord("shop-");
        AgentDto agent;
        using (var created = await app.Client.PostJsonAsync(
            "/api/agents",
            new SaveAgentRequest("Shop helper", key, Tools: [ToolNames.SearchKnowledgeBase], McpApps: [new AgentMcpAppDto(server.Id)])))
        {
            agent = await created.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        }

        var enabled = agent.McpApps.ShouldHaveSingleItem();
        (enabled.Enabled, enabled.Mode, enabled.ChangesPolicy).ShouldBe((true, McpAppMode.All, McpToolPolicy.Confirm), "an app is on with all its tools by default");
        (await app.Client.GetJsonAsync<McpServerDto>($"/api/connections/mcp/{server.Id}")).UsedBy.ShouldHaveSingleItem().ToolCount.ShouldBe(server.Tools.Count);

        using (var chosen = await app.Client.PutJsonAsync(
            $"/api/agents/{agent.Id}",
            new SaveAgentRequest("Shop helper", Tools: [ToolNames.SearchKnowledgeBase], McpApps: [new AgentMcpAppDto(server.Id, Mode: McpAppMode.Chosen, ChangesPolicy: McpToolPolicy.VerifiedOnly, Tools: [new AgentMcpToolDto("lookup_order", McpToolPolicy.Auto), new AgentMcpToolDto("cancel_order")])])))
        {
            agent = await chosen.ReadJsonAsync<AgentDto>();
        }

        agent.McpApps.ShouldHaveSingleItem().Tools.ShouldNotBeNull().Select(tool => (tool.Name, tool.Policy)).ShouldBe(
            [("lookup_order", McpToolPolicy.Auto), ("cancel_order", McpToolPolicy.VerifiedOnly)], "Auto is chosen; a policy left out never runs on its own");
        using (var unknown = await app.Client.PutJsonAsync($"/api/agents/{agent.Id}", new SaveAgentRequest("Shop helper", McpApps: [new AgentMcpAppDto(server.Id, Mode: McpAppMode.Chosen, Tools: [new AgentMcpToolDto("drop_tables")])])))
        {
            (await unknown.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("mcpApps");
        }

        using (var auto = await app.Client.PutJsonAsync($"/api/agents/{agent.Id}", new SaveAgentRequest("Shop helper", McpApps: [new AgentMcpAppDto(server.Id, ChangesPolicy: McpToolPolicy.Auto)])))
        {
            (await auto.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("mcpApps");
        }

        using (var kept = await app.Client.PutJsonAsync($"/api/agents/{agent.Id}", new SaveAgentRequest("Shop helper", Tools: [ToolNames.SearchKnowledgeBase])))
        {
            (await kept.ReadJsonAsync<AgentDto>()).McpApps.ShouldHaveSingleItem("no MCP apps in the request keeps the agent's own");
        }

        (await app.Client.GetJsonAsync<McpServerDto>($"/api/connections/mcp/{server.Id}")).UsedBy.ShouldHaveSingleItem().AgentId.ShouldBe(agent.Id);

        var toolName = McpToolNames.For(server.Key, "lookup_order");
        var scripted = app.ChatModels.Use("orchestrator", new ScriptedChatClient()
            .ThenToolCall(toolName, new { orderId = "A-17" })
            .ThenText("It shipped on 1 November."));
        var stream = await app.ChatAsync(new ChatRequestDto(null, "Where is A-17?", Agent: key));

        stream.Done.FinishReason.ShouldBe(ChatFinishReasons.Stop);
        shop.Calls.ShouldContain(("lookup_order", "A-17"));
        scripted.Requests[0].Options!.Tools!.Select(tool => tool.Name).ShouldContain(toolName);
        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{stream.ConversationId}");
        conversation.Turns.Single().ToolCalls.Single(call => call.ToolName == toolName).Result.ShouldBe("Order A-17: shipped on 2026-11-01.");

        using (var delete = await app.Client.DeleteAsync($"/api/connections/mcp/{server.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await app.Client.GetJsonAsync<AgentDto>($"/api/agents/{agent.Id}")).McpApps.ShouldBeEmpty("deleting a connection takes it from the agents");
        app.ChatModels.Reset();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_agent_answers_as_an_mcp_server_with_its_own_api_keys()
    {
        var app = await fixture.GetAppAsync();
        var agent = await CreateAgentAsync(app);
        var other = await CreateAgentAsync(app);
        await app.AddReadyTextSourceAsync(ApiClient.UniqueWord("Returns "), "Returns are accepted within 30 days of delivery.");
        var created = await CreateKeyAsync(app, agent.Id, "Claude Desktop");
        created.Secret.ShouldStartWith(AgentApiKeyService.KeyPrefix);
        created.Key.Prefix.ShouldBe(created.Secret[..9]);

        (await ConnectFailureAsync(app, agent.Key, created.Secret)).ShouldBe(HttpStatusCode.Forbidden);
        await SetAccessAsync(app, agent.Id, mcp: true);

        app.ChatModels.Use("orchestrator", new ScriptedChatClient().ThenText("Returns are accepted for 30 days.").ThenText("Yes, also for sale items."));
        await using (var client = await ConnectAsync(app, agent.Key, created.Secret))
        {
            (await client.ListToolsAsync(cancellationToken: Ct)).Select(tool => tool.Name).ShouldBe([AgentMcpServer.AskTool, AgentMcpServer.SearchTool]);

            var answer = await client.CallToolAsync(AgentMcpServer.AskTool, new Dictionary<string, object?> { ["question"] = "How long can I return things?" }, cancellationToken: Ct);
            answer.IsError.ShouldNotBe(true);
            var text = Text(answer);
            text.ShouldStartWith("Returns are accepted for 30 days.");
            var conversationId = Guid.Parse(text[(text.IndexOf("conversation_id: ", StringComparison.Ordinal) + 17)..].Trim());

            var followUp = await client.CallToolAsync(
                AgentMcpServer.AskTool,
                new Dictionary<string, object?> { ["question"] = "Sale items too?", ["conversation_id"] = conversationId.ToString() },
                cancellationToken: Ct);
            Text(followUp).ShouldContain(conversationId.ToString());

            var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}");
            conversation.Channel.ShouldBe("mcp");
            conversation.AgentId.ShouldBe(agent.Id);
            conversation.Turns.Count.ShouldBe(2);
            var listed = await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?channel=mcp");
            listed.Items.Select(item => item.Id).ShouldContain(conversationId);

            var search = await client.CallToolAsync(AgentMcpServer.SearchTool, new Dictionary<string, object?> { ["query"] = "returns within 30 days" }, cancellationToken: Ct);
            Text(search).ShouldContain("Returns are accepted within 30 days");

            var bad = await client.CallToolAsync(AgentMcpServer.AskTool, new Dictionary<string, object?> { ["question"] = "Again", ["conversation_id"] = Guid.NewGuid().ToString() }, cancellationToken: Ct);
            bad.IsError.ShouldBe(true);
        }

        var keys = await app.Client.GetJsonAsync<AgentApiKeyDto[]>($"/api/agents/{agent.Id}/api-keys");
        keys.ShouldHaveSingleItem().LastUsedAt.ShouldNotBeNull();
        (await ConnectFailureAsync(app, agent.Key, "ka_not-a-real-key-at-all-000000")).ShouldBe(HttpStatusCode.Unauthorized);
        (await ConnectFailureAsync(app, other.Key, created.Secret)).ShouldBe(HttpStatusCode.Unauthorized, "a key works only for its own agent");
        (await ConnectFailureAsync(app, agent.Key, null)).ShouldBe(HttpStatusCode.Unauthorized);

        using (var revoke = await app.Client.DeleteAsync($"/api/agents/{agent.Id}/api-keys/{created.Key.Id}", Ct))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await ConnectFailureAsync(app, agent.Key, created.Secret)).ShouldBe(HttpStatusCode.Unauthorized, "a revoked key stops working at once");
        (await app.Client.GetJsonAsync<AgentApiKeyDto[]>($"/api/agents/{agent.Id}/api-keys")).ShouldHaveSingleItem().RevokedAt.ShouldNotBeNull();
        app.ChatModels.Reset();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task The_http_api_answers_with_a_key_within_its_rate_limit()
    {
        var app = await fixture.GetAppAsync();
        var agent = await CreateAgentAsync(app);
        var created = await CreateKeyAsync(app, agent.Id, "Backend", requestsPerMinute: 1);
        app.ChatModels.Use("orchestrator", new ScriptedChatClient().ThenText("Thirty days."));

        (await AskAsync(app, agent.Key, created.Secret)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the HTTP API is off");
        var access = await SetAccessAsync(app, agent.Id, api: true);
        access.ApiPath.ShouldBe($"/api/public/agents/{agent.Key}");
        access.ActiveKeys.ShouldBe(1);

        using (var ok = await AskAsync(app, agent.Key, created.Secret))
        {
            var answer = await ok.ReadJsonAsync<AgentAnswerDto>();
            answer.Answer.ShouldBe("Thirty days.");
            (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{answer.ConversationId}")).Channel.ShouldBe("api");
        }

        using (var limited = await AskAsync(app, agent.Key, created.Secret))
        {
            (await limited.ReadProblemAsync(HttpStatusCode.TooManyRequests)).Code.ShouldBe("rate_limited");
            limited.Headers.RetryAfter.ShouldNotBeNull();
        }

        app.ChatModels.Reset();
    }

    private static async Task<AgentDto> CreateAgentAsync(KnowledgeAgentApp app)
    {
        using var response = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Help", ApiClient.UniqueWord("help-"), Tools: [ToolNames.SearchKnowledgeBase]));
        return await response.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
    }

    private static async Task<CreatedAgentApiKeyDto> CreateKeyAsync(KnowledgeAgentApp app, Guid agentId, string name, int? requestsPerMinute = null)
    {
        using var response = await app.Client.PostJsonAsync($"/api/agents/{agentId}/api-keys", new CreateAgentApiKeyRequest(name, requestsPerMinute));
        return await response.ReadJsonAsync<CreatedAgentApiKeyDto>(HttpStatusCode.Created);
    }

    private static async Task<AgentAccessDto> SetAccessAsync(KnowledgeAgentApp app, Guid agentId, bool? mcp = null, bool? api = null)
    {
        using var response = await app.Client.PutJsonAsync($"/api/agents/{agentId}/access", new SaveAgentAccessRequest(mcp, api));
        return await response.ReadJsonAsync<AgentAccessDto>();
    }

    private static async Task<HttpResponseMessage> AskAsync(KnowledgeAgentApp app, string agentKey, string secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/agents/{agentKey}/ask")
        {
            Content = JsonContent.Create(new AgentAskRequest("How long can I return things?"), options: TestJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return await app.Client.SendAsync(request, Ct);
    }

    private static Task<McpClient> ConnectAsync(KnowledgeAgentApp app, string agentKey, string? secret)
    {
        var headers = new Dictionary<string, string>();
        if (secret is not null)
        {
            headers["Authorization"] = "Bearer " + secret;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(app.Client.BaseAddress!, AgentApiKeyService.McpPath(agentKey)),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = headers,
            },
            app.Factory.CreateClient(),
            NullLoggerFactory.Instance,
            ownsHttpClient: true);
        return McpClient.CreateAsync(transport, loggerFactory: NullLoggerFactory.Instance, cancellationToken: Ct);
    }

    private static async Task<HttpStatusCode?> ConnectFailureAsync(KnowledgeAgentApp app, string agentKey, string? secret)
    {
        try
        {
            await using var client = await ConnectAsync(app, agentKey, secret);
            return null;
        }
        catch (HttpRequestException ex)
        {
            return ex.StatusCode;
        }
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}

public sealed class InMemoryMcpScenarios(InMemoryAppFixture fixture) : McpScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersMcpScenarios(ContainersAppFixture fixture) : McpScenarios(fixture), IClassFixture<ContainersAppFixture>;
