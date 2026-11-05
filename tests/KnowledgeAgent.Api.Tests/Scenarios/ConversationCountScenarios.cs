using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ConversationCountScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Each_inbox_view_counts_its_conversations_without_the_playground_tests()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var widget = await CreateWidgetAsync(app);
        var waiting = await StartVisitorConversationAsync(app, widget.PublicKey, "Can I change my order?");
        await PostAsVisitorAsync(app, widget.PublicKey, waiting, $"conversations/{waiting.ConversationId}/handover", new VisitorHandoverRequest(Name: "Ana"));
        var answered = await StartVisitorConversationAsync(app, widget.PublicKey, "Do you ship abroad?");
        var closed = await StartVisitorConversationAsync(app, widget.PublicKey, "Thanks, that is all.");
        using (var close = await app.Client.PostAsync($"/api/conversations/{closed.ConversationId}/close", null, Ct))
        {
            close.IsSuccessStatusCode.ShouldBeTrue(await close.Content.ReadAsStringAsync(Ct));
        }

        (await app.ChatAsync(new ChatRequestDto(null, "A playground question"))).Errors.ShouldBeEmpty();

        LabelDto label;
        using (var created = await app.Client.PostJsonAsync("/api/labels", new CreateLabelRequest(ApiClient.UniqueWord("Shipping "))))
        {
            label = await created.ReadJsonAsync<LabelDto>(HttpStatusCode.Created);
        }

        foreach (var id in new[] { waiting.ConversationId, answered.ConversationId })
        {
            using var labeled = await app.Client.PutJsonAsync($"/api/conversations/{id}/labels", new SetConversationLabelsRequest([label.Id]));
            labeled.IsSuccessStatusCode.ShouldBeTrue();
        }

        using (var pinned = await app.Client.PatchJsonAsync($"/api/conversations/{answered.ConversationId}", new UpdateConversationRequest(Pinned: true)))
        {
            pinned.IsSuccessStatusCode.ShouldBeTrue();
        }

        var counts = await app.Client.GetJsonAsync<ConversationCountsDto>("/api/conversations/counts");
        (counts.HandoverPending, counts.Human, counts.Ai, counts.Closed, counts.All, counts.Pinned).ShouldBe((1, 0, 1, 1, 3, 1));
        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{answered.ConversationId}")).Pinned.ShouldBeTrue();
        counts.Labels.ShouldContain(new LabelCountDto(label.Id, 2));

        (await app.Client.GetJsonAsync<ConversationCountsDto>("/api/conversations/counts?includeTests=true")).All.ShouldBe(4);
        (await app.Client.GetJsonAsync<ConversationCountsDto>("/api/conversations/counts?channel=portal")).All.ShouldBe(1);
        (await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?widgetId={Guid.NewGuid()}")).All.ShouldBe(0);
        (await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?widgetId={widget.Id}")).HandoverPending.ShouldBe(1);

        using var invalid = await app.Client.GetAsync("/api/conversations/counts?channel=fax", Ct);
        (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("channel");
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

    // A new visitor, so every call starts a conversation of its own.
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

public sealed class InMemoryConversationCountScenarios(InMemoryAppFixture fixture) : ConversationCountScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersConversationCountScenarios(ContainersAppFixture fixture) : ConversationCountScenarios(fixture), IClassFixture<ContainersAppFixture>;
