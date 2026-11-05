using System.Net;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Agents.Access;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class AgentScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Agents_can_be_created_listed_updated_and_deleted()
    {
        var app = await fixture.GetAppAsync();
        var key = ApiClient.UniqueWord("agent-");
        var folder = await CreateFolderAsync(app, ApiClient.UniqueWord("folder"));

        var all = await app.Client.GetJsonAsync<AgentDto[]>("/api/agents");
        var builtin = all.Where(agent => agent.IsBuiltIn).ShouldHaveSingleItem();
        builtin.Key.ShouldBe(AgentConfig.InternalKey);
        var options = await app.Client.GetJsonAsync<AgentOptionsDto>("/api/agents/options");
        options.Tools.ShouldContain(ToolNames.SearchKnowledgeBase);
        options.Profiles.ShouldContain(AgentDefaults.ProfileName);
        options.Tools.ShouldNotContain(ToolNames.RequestHumanHandover, "when the agent offers a person is its handover policy");
        builtin.HandoverPolicy.ShouldBe(HandoverPolicy.Never);

        AgentDto created;
        using (var create = await app.Client.PostJsonAsync(
            "/api/agents",
            new SaveAgentRequest("Website helper", key, Instructions: "Be friendly.", FolderIds: [folder.Id], Tools: [ToolNames.SearchKnowledgeBase], HandoverPolicy: HandoverPolicy.WhenUnsure)))
        {
            created = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
            create.Headers.Location?.ToString().ShouldBe($"/api/agents/{created.Id}");
        }

        created.Key.ShouldBe(key);
        created.FolderIds.ShouldBe([folder.Id]);
        created.Tools.ShouldBe([ToolNames.SearchKnowledgeBase]);
        (await app.Client.GetJsonAsync<AgentDto>($"/api/agents/{created.Id}")).Name.ShouldBe("Website helper");

        using (var duplicate = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Copy", key)))
        {
            (await duplicate.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("key");
        }

        using (var update = await app.Client.PutJsonAsync($"/api/agents/{created.Id}", new SaveAgentRequest("Website guide", Language: "French")))
        {
            var updated = await update.ReadJsonAsync<AgentDto>();
            updated.Name.ShouldBe("Website guide");
            updated.Language.ShouldBe("French");
            updated.FolderIds.ShouldBeEmpty("an update replaces the whole agent");
            updated.HandoverPolicy.ShouldBe(HandoverPolicy.WhenUnsure, "but keeps its handover policy when none is sent");
            (await update.Content.ReadAsStringAsync(Ct)).ShouldContain("\"handoverPolicy\":\"whenUnsure\"");
        }

        using (var saveBuiltin = await app.Client.PutJsonAsync($"/api/agents/{builtin.Id}", new SaveAgentRequest("Company brain")))
        {
            (await saveBuiltin.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("id");
        }

        using (var deleteBuiltin = await app.Client.DeleteAsync($"/api/agents/{builtin.Id}", Ct))
        {
            (await deleteBuiltin.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("id");
        }

        using (var delete = await app.Client.DeleteAsync($"/api/agents/{created.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var gone = await app.Client.GetAsync($"/api/agents/{created.Id}", Ct))
        {
            await gone.ReadProblemAsync(HttpStatusCode.NotFound);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Specialists_are_part_of_the_agent_and_an_agent_in_a_team_is_only_deleted_on_request()
    {
        var app = await fixture.GetAppAsync();
        AgentDto billing;
        using (var create = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Billing", ApiClient.UniqueWord("billing-"))))
        {
            billing = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        }

        AgentDto front;
        using (var create = await app.Client.PostJsonAsync(
            "/api/agents",
            new SaveAgentRequest("Front desk", ApiClient.UniqueWord("front-")) { Specialists = [new(billing.Id, "Invoices and refunds")], ShowSpecialistName = true }))
        {
            front = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
            (await create.Content.ReadAsStringAsync(Ct)).ShouldContain($"\"specialists\":[{{\"agentId\":\"{billing.Id}\",\"whenToUse\":\"Invoices and refunds\"}}]");
        }

        front.ShowSpecialistName.ShouldBeTrue();

        using (var loop = await app.Client.PutJsonAsync($"/api/agents/{billing.Id}", new SaveAgentRequest("Billing") { Specialists = [new(front.Id, "Anything else")] }))
        {
            (await loop.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("specialists");
        }

        using (var inUse = await app.Client.DeleteAsync($"/api/agents/{billing.Id}", Ct))
        {
            var problem = await inUse.ReadProblemAsync(HttpStatusCode.Conflict);
            problem.Code.ShouldBe("agent_is_specialist");
            (await inUse.Content.ReadAsStringAsync(Ct)).ShouldContain(front.Id.ToString());
        }

        using (var delete = await app.Client.DeleteAsync($"/api/agents/{billing.Id}?removeFromTeams=true", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await app.Client.GetJsonAsync<AgentDto>($"/api/agents/{front.Id}")).Specialists.ShouldBeEmpty();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Only_admins_can_change_agents()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);

        using (var list = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/agents").As(AuthRoles.Support), Ct))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var create = new HttpRequestMessage(HttpMethod.Post, "/api/agents")
        {
            Content = JsonContent.Create(new SaveAgentRequest("Nope", "nope"), options: TestJson.Options),
        }.As(AuthRoles.Support);
        using var forbidden = await app.Client.SendAsync(create, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var removeFromTeams = await app.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{Guid.NewGuid()}?removeFromTeams=true").As(AuthRoles.Support), Ct);
        removeFromTeams.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_chat_with_an_agent_stays_within_its_folders()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var folder = await CreateFolderAsync(app, ApiClient.UniqueWord("folder"));
        var inside = await AddReadyTextAsync(app, "Inside notes", $"The {marker} ferry leaves at nine.", folder.Id);
        var outside = await AddReadyTextAsync(app, "Outside notes", $"The {marker} ferry is painted red.", folderId: null);
        AgentDto agent;
        using (var create = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Ferry desk", ApiClient.UniqueWord("ferry-"), FolderIds: [folder.Id])))
        {
            agent = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        }

        var stream = await app.ChatAsync(new ChatRequestDto(null, $"Tell me about the {marker} ferry.", Agent: agent.Key));

        stream.Errors.ShouldBeEmpty(stream.Describe());
        var citations = stream.All<ToolResultEvent>(ChatEventTypes.ToolResult).SelectMany(result => result.Citations).ToList();
        citations.ShouldNotBeEmpty(stream.Describe());
        citations.ShouldAllBe(citation => citation.SourceId == inside.Id);
        citations.ShouldNotContain(citation => citation.SourceId == outside.Id);
        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{stream.ConversationId}");
        conversation.AgentId.ShouldBe(agent.Id);
        conversation.Channel.ShouldBe("portal");
        conversation.Status.ShouldBe("ai");

        var missing = await app.ChatAsync(new ChatRequestDto(null, "Hello?", Agent: "no-such-agent"));
        missing.Errors.ShouldHaveSingleItem().Code.ShouldBe(ChatErrorCodes.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task No_agent_retrieves_Internal_sources_through_chat_or_the_http_api()
    {
        await using var app = await fixture.CreateAppAsync();
        var marker = ApiClient.UniqueWord();
        var internalFolder = (await app.Client.GetJsonAsync<SourceFolderListDto>("/api/sources/folders")).Folders.Single(folder => folder.IsInternal);
        internalFolder.Name.ShouldBe("Internal");
        var team = await CreateFolderAsync(app, "HR", internalFolder.Id);
        var secret = await AddReadyTextAsync(app, "Salary bands", $"The {marker} salary band tops out at ninety.", team.Id);
        var open = await AddReadyTextAsync(app, "Careers", $"The {marker} salary is agreed in the interview.", folderId: null);
        AgentDto website;
        using (var create = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Website", "website", Tools: [ToolNames.SearchKnowledgeBase])))
        {
            website = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        }

        var stream = await app.ChatAsync(new ChatRequestDto(null, $"What is the {marker} salary?", Agent: website.Key));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        var cited = stream.All<ToolResultEvent>(ChatEventTypes.ToolResult).SelectMany(result => result.Citations).Select(citation => citation.SourceId).ToList();
        cited.ShouldContain(open.Id, stream.Describe());
        cited.ShouldNotContain(secret.Id);

        using (var key = await app.Client.PostJsonAsync($"/api/agents/{website.Id}/api-keys", new CreateAgentApiKeyRequest("Backend")))
        using (var access = await app.Client.PutJsonAsync($"/api/agents/{website.Id}/access", new SaveAgentAccessRequest(HttpApiEnabled: true)))
        {
            access.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var search = new HttpRequestMessage(HttpMethod.Post, $"/api/public/agents/{website.Key}/search")
            {
                Content = JsonContent.Create(new AgentSearchRequest($"{marker} salary", TopK: 10), options: TestJson.Options),
            };
            search.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await key.ReadJsonAsync<CreatedAgentApiKeyDto>(HttpStatusCode.Created)).Secret);
            using var found = await app.Client.SendAsync(search, Ct);
            var titles = (await found.ReadJsonAsync<AgentSearchResultDto>()).Passages.Select(passage => passage.SourceTitle).ToList();
            titles.ShouldContain("Careers");
            titles.ShouldNotContain("Salary bands");
        }

        using (var limited = await app.Client.PutJsonAsync($"/api/agents/{website.Id}", new SaveAgentRequest("Website", FolderIds: [team.Id])))
        {
            (await limited.ReadProblemAsync(HttpStatusCode.BadRequest)).Errors.ShouldNotBeNull().ShouldContainKey("folderIds");
        }

        var portal = await app.ChatAsync(new ChatRequestDto(null, $"What is the {marker} salary?"));
        portal.Errors.ShouldBeEmpty(portal.Describe());
        var portalCited = portal.All<ToolResultEvent>(ChatEventTypes.ToolResult).SelectMany(result => result.Citations).Select(citation => citation.SourceId).ToList();
        portalCited.ShouldContain(open.Id, portal.Describe());
        portalCited.ShouldNotContain(secret.Id);
        var builtIn = (await app.Client.GetJsonAsync<AgentDto[]>("/api/agents")).Single(agent => agent.IsBuiltIn);
        using (var expose = await app.Client.PutJsonAsync($"/api/agents/{builtIn.Id}/access", new SaveAgentAccessRequest(McpServerEnabled: true)))
        {
            (await expose.ReadProblemAsync(HttpStatusCode.BadRequest)).Errors.ShouldNotBeNull().ShouldContainKey("id");
        }

        using (var builtInKey = await app.Client.PostJsonAsync($"/api/agents/{builtIn.Id}/api-keys", new CreateAgentApiKeyRequest("Backend")))
        {
            builtInKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var rename = await app.Client.PatchJsonAsync($"/api/sources/folders/{internalFolder.Id}", new RenameSourceFolderRequest("Team")))
        {
            rename.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var move = await app.Client.PostJsonAsync($"/api/sources/folders/{internalFolder.Id}/move", new MoveSourceFolderRequest(team.Id)))
        {
            move.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var delete = await app.Client.DeleteAsync($"/api/sources/folders/{internalFolder.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    private static async Task<SourceFolderDto> CreateFolderAsync(KnowledgeAgentApp app, string name, Guid? parentId = null)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/folders", new CreateSourceFolderRequest(name, parentId));
        return await response.ReadJsonAsync<SourceFolderDto>(HttpStatusCode.Created);
    }

    private static async Task<SourceDto> AddReadyTextAsync(KnowledgeAgentApp app, string title, string content, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content, FolderId: folderId));
        var source = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        return await app.WaitForSourceAsync(source.Id);
    }
}

public sealed class InMemoryAgentScenarios(InMemoryAppFixture fixture) : AgentScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersAgentScenarios(ContainersAppFixture fixture) : AgentScenarios(fixture), IClassFixture<ContainersAppFixture>;
