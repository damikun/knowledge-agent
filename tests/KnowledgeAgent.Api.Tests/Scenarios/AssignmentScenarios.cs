using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Runs as the local admin of Auth:Mode=None (subject local-admin).
public abstract class AssignmentScenarios(AppFixture fixture)
{
    private const string Me = "local-admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_staff_member_assigns_a_conversation_finds_it_in_Mine_and_reads_it()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();

        // Signing in records the staff member, so conversations can be assigned to them.
        (await app.Client.GetJsonAsync<CurrentUserDto>("/api/auth/me")).Id.ShouldBe(Me);
        (await app.Client.GetJsonAsync<List<StaffMemberDto>>("/api/staff")).ShouldContain(member => member.Id == Me && member.Name == "Local admin");

        var widget = await CreateWidgetAsync(app);
        var waiting = await StartVisitorConversationAsync(app, widget.PublicKey, "Can I change my order?");
        await PostAsVisitorAsync(app, widget.PublicKey, waiting, $"conversations/{waiting.ConversationId}/handover", new VisitorHandoverRequest(Name: "Ana"));
        var id = waiting.ConversationId!.Value;
        var mineUrl = $"/api/conversations?widgetId={widget.Id}&assignee=me&status=ai,handover_pending,human";

        (await app.Client.GetJsonAsync<ConversationListPageDto>(mineUrl)).Items.ShouldBeEmpty();
        var unassigned = await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?widgetId={widget.Id}&assignee=none&status=handover_pending,human");
        unassigned.Items.Single().ShouldSatisfyAllConditions(item => item.Id.ShouldBe(id), item => item.Unread.ShouldBeTrue(), item => item.Assignee.ShouldBeNull());

        using (var assigned = await app.Client.PostJsonAsync($"/api/conversations/{id}/assign", new AssignRequest(Me)))
        {
            (await assigned.ReadJsonAsync<AssignmentDto>()).Assignee.ShouldBe(new ConversationAssigneeDto(Me, "Local admin"));
        }

        var mine = await app.Client.GetJsonAsync<ConversationListPageDto>(mineUrl);
        mine.Items.Single().ShouldSatisfyAllConditions(item => item.Id.ShouldBe(id), item => item.Assignee.ShouldBe(new ConversationAssigneeDto(Me, "Local admin")));
        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{id}")).Assignee.ShouldBe(new ConversationAssigneeDto(Me, "Local admin"));
        var searched = await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?widgetId={widget.Id}&assignee=me&unread=true&status=handover_pending,human");
        searched.Items.Single().ShouldSatisfyAllConditions(
            item => item.Id.ShouldBe(id),
            item => item.Unread.ShouldBeTrue(),
            item => item.Assignee.ShouldBe(new ConversationAssigneeDto(Me, "Local admin")));
        var counts = await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?widgetId={widget.Id}");
        (counts.Mine, counts.Unassigned, counts.Unread.Mine, counts.Unread.HandoverPending).ShouldBe((1, 0, 1, 1));

        var contactId = (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{id}")).ContactId.ShouldNotBeNull();
        (await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?contactId={contactId}")).ShouldSatisfyAllConditions(
            personCounts => personCounts.All.ShouldBe(1),
            personCounts => personCounts.Mine.ShouldBe(1),
            personCounts => personCounts.Unread.All.ShouldBe(1));
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?contactId={contactId}&assignee=me")).Items.Select(item => item.Id).ShouldBe([id]);
        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?contactId={contactId}")).Items.Select(item => item.Id).ShouldBe([id]);

        using (var read = await app.Client.PostJsonAsync($"/api/conversations/{id}/read", new MarkReadRequest()))
        {
            (await read.ReadJsonAsync<ConversationReadDto>()).Unread.ShouldBeFalse();
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?widgetId={widget.Id}&unread=true")).Items.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?widgetId={widget.Id}&unread=true")).Items.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?widgetId={widget.Id}")).Unread.All.ShouldBe(0);

        using (var unread = await app.Client.DeleteAsync($"/api/conversations/{id}/read", Ct))
        {
            (await unread.ReadJsonAsync<ConversationReadDto>()).Unread.ShouldBeTrue();
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>(mineUrl)).Items.Single().Unread.ShouldBeTrue();

        using (var stranger = await app.Client.PostJsonAsync($"/api/conversations/{id}/assign", new AssignRequest("nobody-signed-in")))
        {
            (await stranger.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("userId");
        }

        using (var cleared = await app.Client.PostJsonAsync($"/api/conversations/{id}/assign", new AssignRequest(null)))
        {
            (await cleared.ReadJsonAsync<AssignmentDto>()).Assignee.ShouldBeNull();
        }

        using (var joined = await app.Client.PostAsync($"/api/conversations/{id}/join", null, Ct))
        {
            (await joined.ReadJsonAsync<HandoverDto>()).Status.ShouldBe("active");
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>(mineUrl)).Items.Single().Id.ShouldBe(id);

        using (var returned = await app.Client.PostAsync($"/api/conversations/{id}/return", null, Ct))
        {
            returned.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>(mineUrl)).Items.ShouldBeEmpty();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Assigning_and_reading_check_their_input()
    {
        await using var app = await fixture.CreateAppAsync();
        var missing = Guid.NewGuid();

        using (var assign = await app.Client.PostJsonAsync($"/api/conversations/{missing}/assign", new AssignRequest(null)))
        {
            assign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var read = await app.Client.PostJsonAsync($"/api/conversations/{missing}/read", new MarkReadRequest()))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var negative = await app.Client.PostJsonAsync($"/api/conversations/{missing}/read", new MarkReadRequest(-1)))
        {
            (await negative.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("sequence");
        }

        using (var unread = await app.Client.DeleteAsync($"/api/conversations/{missing}/read", Ct))
        {
            unread.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var status = await app.Client.GetAsync("/api/conversations?status=ai,fax", Ct);
        (await status.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("status");
    }

    private static async Task<WidgetDto> CreateWidgetAsync(KnowledgeAgentApp app)
    {
        AgentDto agent;
        using (var create = await app.Client.PostJsonAsync("/api/agents", new SaveAgentRequest("Shop assistant", ApiClient.UniqueWord("shop-"))))
        {
            agent = await create.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        }

        var settings = new WidgetSettings { Behavior = new WidgetBehavior { HandoverEnabled = true } };
        using var response = await app.Client.PostJsonAsync("/api/widgets", new SaveWidgetRequest(ApiClient.UniqueWord("Shop "), agent.Id, Settings: settings));
        return await response.ReadJsonAsync<WidgetDto>(HttpStatusCode.Created);
    }

    private static async Task<VisitorSessionDto> StartVisitorConversationAsync(KnowledgeAgentApp app, string key, string message)
    {
        VisitorSessionDto session;
        using (var started = await app.Client.PostJsonAsync($"/api/public/widgets/{key}/sessions", new VisitorSessionRequest()))
        {
            session = await started.ReadJsonAsync<VisitorSessionDto>();
        }

        using var request = VisitorRequest(key, session, "messages", new VisitorMessageRequest(message));
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        using var response = await app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stream = new ChatStream(null, null, null, await SseReader.ReadAllAsync(response, Ct));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        return session with { ConversationId = stream.ConversationId };
    }

    private static async Task PostAsVisitorAsync(KnowledgeAgentApp app, string key, VisitorSessionDto session, string path, object body)
    {
        using var request = VisitorRequest(key, session, path, body);
        using var response = await app.Client.SendAsync(request, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
    }

    private static HttpRequestMessage VisitorRequest(string key, VisitorSessionDto session, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/widgets/{key}/{path}")
        {
            Content = JsonContent.Create(body, body.GetType(), options: TestJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return request;
    }
}

public sealed class InMemoryAssignmentScenarios(InMemoryAppFixture fixture) : AssignmentScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersAssignmentScenarios(ContainersAppFixture fixture) : AssignmentScenarios(fixture), IClassFixture<ContainersAppFixture>;
