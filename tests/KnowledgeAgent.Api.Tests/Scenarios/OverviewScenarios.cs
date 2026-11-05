using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Overview;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class OverviewScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Overview_counts_the_weeks_conversations_requests_for_a_person_ratings_and_widget_activity()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var widget = await CreateWidgetAsync(app);
        var unused = await CreateWidgetAsync(app);

        // Portal chats are playground tests, also when they test a widget, and count nowhere.
        (await app.ChatAsync(new ChatRequestDto(null, "What is our travel policy?"))).Errors.ShouldBeEmpty();
        (await app.ChatAsync(new ChatRequestDto(null, "Testing the shop widget", WidgetId: widget.Id))).Errors.ShouldBeEmpty();
        var asked = await StartVisitorConversationAsync(app, widget.PublicKey, "Can I change my order?");
        await PostAsVisitorAsync(app, widget.PublicKey, asked, $"conversations/{asked.ConversationId}/handover", new VisitorHandoverRequest(Name: "Ana"));
        await PostAsVisitorAsync(app, widget.PublicKey, asked, $"conversations/{asked.ConversationId}/feedback", new SubmitFeedbackRequest(4));
        await StartVisitorConversationAsync(app, widget.PublicKey, "Do you ship abroad?");

        var overview = await app.Client.GetJsonAsync<OverviewDto>("/api/overview");

        overview.Days.ShouldBe(OverviewEndpoints.DefaultDays);
        (overview.Conversations, overview.WebsiteConversations, overview.PreviousConversations).ShouldBe((2, 2, 0));
        (overview.AnsweredByAi, overview.HandedToPerson).ShouldBe((1, 1));
        (overview.Ratings, overview.AverageRating).ShouldBe((1, 4.0));
        var activity = overview.Widgets.Single(item => item.WidgetId == widget.Id);
        activity.Conversations.ShouldBe(2);
        activity.LastConversationAt.ShouldNotBeNull().ShouldBeGreaterThan(overview.From);
        overview.Widgets.Single(item => item.WidgetId == unused.Id).ShouldBe(new WidgetActivityDto(unused.Id, 0, null));

        (await app.Client.GetJsonAsync<OverviewDto>("/api/overview?days=30")).From.ShouldBeLessThan(overview.From);
        foreach (var days in new[] { 0, OverviewEndpoints.MaxDays + 1 })
        {
            using var invalid = await app.Client.GetAsync($"/api/overview?days={days}", Ct);
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("days");
        }
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

public sealed class InMemoryOverviewScenarios(InMemoryAppFixture fixture) : OverviewScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersOverviewScenarios(ContainersAppFixture fixture) : OverviewScenarios(fixture), IClassFixture<ContainersAppFixture>;
