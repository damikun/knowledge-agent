using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Help;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.Overview;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class HelpScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Help_reads_sections_from_its_contents_and_answers_with_links_into_the_portal_and_stores_nothing()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var model = app.ChatModels.Use("orchestrator", new ScriptedChatClient()
            .ThenToolCall(ReadHelpSectionTool.ToolName, new { id = "guide/made-up" })
            .ThenToolCall(ReadHelpSectionTool.ToolName, new { id = "guide/connect-a-model#add-an-api-key" })
            .ThenText("Open [AI models](/models#providers) and paste the key under Providers & API keys [S1]."));
        var before = await SnapshotAsync(app);

        var stream = await HelpChatAsync(app, new HelpChatRequest(
            "How do I add an API key?",
            Page: "/sources",
            History: [new HelpHistoryMessage("user", "Hi"), new HelpHistoryMessage("assistant", "Hello, ask me about the portal [S3].")]));

        stream.Types[0].ShouldBe(ChatEventTypes.Conversation, stream.Describe());
        stream.Types[^1].ShouldBe(ChatEventTypes.Done, stream.Describe());
        stream.Errors.ShouldBeEmpty(stream.Describe());
        string.Concat(stream.All<TextDeltaEvent>(ChatEventTypes.TextDelta).Select(delta => delta.Text)).ShouldContain("[S1]");
        var results = stream.All<ToolResultEvent>(ChatEventTypes.ToolResult).ToList();
        results.Select(result => (result.ToolName, result.IsError)).ShouldBe([(ReadHelpSectionTool.ToolName, true), (ReadHelpSectionTool.ToolName, false)]);
        var citation = results[1].Citations.ShouldHaveSingleItem();
        citation.Ref.ShouldBe("S1");
        citation.SourceTitle.ShouldBe("Connect a model > Add an API key");
        citation.SourceUrl.ShouldBe("/models#providers");

        // Two help tools and one Internal search tool, nothing else; an unknown id comes back as a recoverable error.
        var first = model.Requests[0];
        first.Options!.Tools!.Select(tool => tool.Name).ShouldBe([ReadHelpSectionTool.ToolName, SearchHelpTool.ToolName, SearchInternalTool.ToolName], ignoreOrder: true);
        var system = first.Messages[0];
        system.Role.ShouldBe(ChatRole.System);
        system.Text.ShouldContain("They are on Sources (/sources)");
        system.Text.ShouldContain("- `guide/connect-a-model#add-an-api-key` Add an API key: ");
        model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single().Result!.ToString()!
            .ShouldContain("No help section has the id \"guide/made-up\"");
        var read = model.Requests[2].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Last().Result!.ToString()!;
        read.ShouldStartWith("[S1] Connect a model > Add an API key");
        read.ShouldContain("](/models");
        first.Messages.Select(message => message.Text).ShouldContain("Hello, ask me about the portal.");
        first.Messages[^1].Text.ShouldBe("How do I add an API key?");

        (await SnapshotAsync(app)).ShouldBe(before, "a help chat must not show up in any customer data or number");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Help_searches_the_Internal_sources_and_only_them()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var model = app.ChatModels.Use("orchestrator", new ScriptedChatClient()
            .ThenToolCall(SearchInternalTool.ToolName, new { query = $"{marker} escalation" })
            .ThenText("Your Internal sources say: page the second line [S1]."));
        var empty = await HelpChatAsync(app, new HelpChatRequest("How do we escalate?"));
        empty.All<ToolResultEvent>(ChatEventTypes.ToolResult).ShouldHaveSingleItem().Summary.ShouldBe("No Internal sources", empty.Describe());

        var internalFolder = (await app.Client.GetJsonAsync<SourceFolderListDto>("/api/sources/folders")).Folders.Single(folder => folder.IsInternal);
        var runbook = await AddReadyTextAsync(app, "On-call runbook", $"The {marker} escalation pages the second line after ten minutes.", internalFolder.Id);
        // Matches the query as well, but it is no Internal source.
        await AddReadyTextAsync(app, "Public FAQ", $"The {marker} escalation is explained to customers in the FAQ.", folderId: null);
        model = app.ChatModels.Use("orchestrator", new ScriptedChatClient()
            .ThenToolCall(SearchInternalTool.ToolName, new { query = $"{marker} escalation" })
            .ThenText("Your Internal sources say: page the second line [S1]."));

        var stream = await HelpChatAsync(app, new HelpChatRequest("How do we escalate?"));

        stream.Errors.ShouldBeEmpty(stream.Describe());
        var cited = stream.All<ToolResultEvent>(ChatEventTypes.ToolResult).ShouldHaveSingleItem().Citations;
        cited.Select(citation => citation.SourceId).Distinct().ShouldBe([runbook.Id], stream.Describe());
        cited.ShouldAllBe(citation => citation.SourceTitle == "Internal: On-call runbook" && citation.SourceUrl == $"/sources?source={runbook.Id:D}");
        model.Requests[0].Messages[0].Text.ShouldContain("Internal sources");
        app.ChatModels.Reset();
    }

    private static async Task<SourceDto> AddReadyTextAsync(KnowledgeAgentApp app, string title, string content, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content, FolderId: folderId));
        var source = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        return await app.WaitForSourceAsync(source.Id);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Without_a_chat_model_the_help_is_disabled_and_the_chat_says_a_model_is_missing()
    {
        await using var app = await fixture.CreateAppAsync();
        using (var put = await app.Client.PutJsonAsync("/api/models/profiles/orchestrator", new UpdateModelProfileRequest("anthropic")))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var status = await app.Client.GetJsonAsync<HelpStatusDto>("/api/help");
        (status.Enabled, status.ModelReady, status.CanManage).ShouldBe((true, false, true));
        status.Setup.ShouldNotBeNull().ChatModel.ShouldBeFalse();
        status.Setup.Agent.ShouldBeFalse("only the built-in agent exists");

        var stream = await HelpChatAsync(app, new HelpChatRequest("How do I put the widget on my site?", Page: "/widgets"));
        stream.Errors.ShouldHaveSingleItem(stream.Describe()).Code.ShouldBe(ChatErrorCodes.ModelNotConfigured);
        stream.Types[^1].ShouldBe(ChatEventTypes.Done);

        using var tooLong = await app.Client.PostJsonAsync("/api/help/chat", new HelpChatRequest(new string('a', HelpChatService.MaxMessageLength + 1)));
        (await tooLong.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("message");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Turning_the_help_off_hides_it_for_everyone_until_an_admin_turns_it_on()
    {
        await using var app = await fixture.CreateAppAsync();

        using (var off = await app.Client.PutJsonAsync("/api/help/settings", new SaveHelpSettingsRequest(false)))
        {
            (await off.ReadJsonAsync<HelpStatusDto>()).Enabled.ShouldBeFalse();
        }

        (await app.Client.GetJsonAsync<HelpStatusDto>("/api/help")).ShouldBe(new HelpStatusDto(false, true, false, null));
        using (var chat = await app.Client.PostJsonAsync("/api/help/chat", new HelpChatRequest("Hello")))
        {
            (await chat.ReadProblemAsync(HttpStatusCode.NotFound)).Detail.ShouldBe(HelpEndpoints.TurnedOffMessage);
        }

        using var on = await app.Client.PutJsonAsync("/api/help/settings", new SaveHelpSettingsRequest(true));
        (await on.ReadJsonAsync<HelpStatusDto>()).Enabled.ShouldBeTrue();
    }

    internal static async Task<ChatStream> HelpChatAsync(KnowledgeAgentApp app, HelpChatRequest request, params string[] roles)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/help/chat") { Content = JsonContent.Create(request, options: TestJson.Options) };
        message.Headers.Accept.Add(SseReader.AcceptHeader);
        if (roles.Length > 0)
        {
            message.As(roles);
        }

        using var response = await app.Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, Ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ShouldAssertException($"POST /api/help/chat returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return new ChatStream(response.Content.Headers.ContentType?.MediaType, null, null, await SseReader.ReadAllAsync(response, Ct));
    }

    // Rows of every table a conversation could touch, plus what the portal's lists and counts show.
    private static async Task<string> SnapshotAsync(KnowledgeAgentApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var rows = new Dictionary<string, int>
        {
            ["conversations"] = await db.Conversations.CountAsync(Ct),
            ["turns"] = await db.ConversationTurns.CountAsync(Ct),
            ["messages"] = await db.ConversationMessages.CountAsync(Ct),
            ["labels"] = await db.ConversationLabels.CountAsync(Ct),
            ["contacts"] = await db.Contacts.CountAsync(Ct),
            ["visitors"] = await db.WidgetVisitors.CountAsync(Ct),
            ["feedback"] = await db.Feedback.CountAsync(Ct),
            ["handovers"] = await db.Handovers.CountAsync(Ct),
            ["datasets"] = await db.EvalDatasets.CountAsync(Ct),
            ["datasetItems"] = await db.EvalDatasetItems.CountAsync(Ct),
            ["alertStates"] = await db.AlertStates.CountAsync(Ct),
        };

        var list = await app.Client.GetJsonAsync<JsonElement>("/api/conversations");
        var search = await app.Client.GetJsonAsync<JsonElement>("/api/conversations/search?q=API");
        var counts = await app.Client.GetJsonAsync<ConversationCountsDto>("/api/conversations/counts");
        var overview = await app.Client.GetJsonAsync<OverviewDto>("/api/overview");
        var analytics = await app.Client.GetJsonAsync<AnalyticsDto>("/api/analytics");
        var people = await app.Client.GetJsonAsync<PeopleCountDto>("/api/people/count");
        var feedback = await app.Client.GetJsonAsync<JsonElement>("/api/feedback");
        var alerts = await app.Client.GetJsonAsync<AlertListDto>("/api/alerts");
        var views = new Dictionary<string, object>
        {
            ["inbox"] = list.GetProperty("items").GetArrayLength(),
            ["search"] = search.GetProperty("items").GetArrayLength(),
            ["counts"] = (counts.All, counts.Ai, counts.HandoverPending, counts.Human, counts.Closed, counts.Labels.Sum(label => label.Count)),
            ["home"] = (overview.Conversations, overview.WebsiteConversations, overview.AnsweredByAi, overview.HandedToPerson, overview.Ratings),
            ["analytics"] = analytics.Totals,
            ["analyticsLabels"] = analytics.Labels.Count,
            ["unanswered"] = analytics.Unanswered.Count,
            ["contacts"] = people.Count,
            ["feedback"] = feedback.GetArrayLength(),
            ["alerts"] = string.Join(",", alerts.Alerts.Select(alert => $"{alert.Key}:{alert.Count}")),
        };

        return JsonSerializer.Serialize(new { rows, views = views.ToDictionary(view => view.Key, view => view.Value.ToString()) });
    }
}

public sealed class InMemoryHelpScenarios(InMemoryAppFixture fixture) : HelpScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersHelpScenarios(ContainersAppFixture fixture) : HelpScenarios(fixture), IClassFixture<ContainersAppFixture>;

public sealed class HelpAuthScenarios(InMemoryAppFixture fixture) : IClassFixture<InMemoryAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Support_reads_and_asks_the_help_but_only_admins_switch_it()
    {
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = TestUsers.Install);
        app.ChatModels.Reset();
        app.ChatModels.Use("orchestrator", new ScriptedChatClient().ThenText("Ask an admin to change it."));

        (await SendAsync(app, HttpMethod.Get, "/api/help")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using (var status = await SendAsync(app, HttpMethod.Get, "/api/help", roles: AuthRoles.Support))
        {
            var dto = await status.ReadJsonAsync<HelpStatusDto>();
            (dto.Enabled, dto.CanManage).ShouldBe((true, false));
        }

        var stream = await HelpScenarios.HelpChatAsync(app, new HelpChatRequest("Can I change the widget?", Page: "/inbox"), AuthRoles.Support);
        stream.Errors.ShouldBeEmpty(stream.Describe());
        (await SendAsync(app, HttpMethod.Put, "/api/help/settings", new SaveHelpSettingsRequest(false), AuthRoles.Support)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var saved = await SendAsync(app, HttpMethod.Put, "/api/help/settings", new SaveHelpSettingsRequest(false), AuthRoles.Admin);
        (await saved.ReadJsonAsync<HelpStatusDto>()).ShouldBe(new HelpStatusDto(false, true, false, null));
        using var hidden = await SendAsync(app, HttpMethod.Get, "/api/help", roles: AuthRoles.Support);
        (await hidden.ReadJsonAsync<HelpStatusDto>()).Enabled.ShouldBeFalse("off hides the help for everyone");
    }

    private static async Task<HttpResponseMessage> SendAsync(KnowledgeAgentApp app, HttpMethod method, string url, object? body = null, params string[] roles)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: TestJson.Options);
        }

        if (roles.Length > 0)
        {
            request.As(roles);
        }

        return await app.Client.SendAsync(request, Ct);
    }
}
