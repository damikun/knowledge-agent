using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ConversationInsightsScenarios(AppFixture fixture)
{
    private const string Golden = "orbitwell-golden";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Conversations_are_found_by_their_words_and_filters_and_show_their_history()
    {
        var app = await fixture.GetAppAsync();
        var word = ApiClient.UniqueWord();
        var widget = await CreateWidgetAsync(app);
        var asked = await StartVisitorConversationAsync(app, widget.PublicKey, $"Can I return the {word} kettle?");
        await PostAsVisitorAsync(app, widget.PublicKey, asked, $"conversations/{asked.ConversationId}/handover", new VisitorHandoverRequest(Name: "Ana", Email: "ana@visitor.test"));
        await PostAsVisitorAsync(app, widget.PublicKey, asked, $"conversations/{asked.ConversationId}/feedback", new SubmitFeedbackRequest(2));
        (await app.ChatAsync(new ChatRequestDto(null, $"What is the {word} policy?"))).Errors.ShouldBeEmpty();

        // The portal conversation is a playground test, which search leaves out unless asked for.
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}")).Items.ShouldHaveSingleItem().Test.ShouldBeFalse();
        var found = await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}&includeTests=true");
        found.Items.Count.ShouldBe(2);
        found.NextCursor.ShouldBeNull();
        var website = found.Items.Single(item => item.Channel == "widget");
        (website.Id, website.WidgetId, website.HandoverStatus, website.Rating).ShouldBe((asked.ConversationId!.Value, (Guid?)widget.Id, "pending", (int?)2));
        website.Match.ShouldNotBeNull().ShouldContain(word);

        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}&channel=portal")).Items.Single().Channel.ShouldBe("portal");
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}&handover=true&maxRating=2")).Items.Single().Id
            .ShouldBe(asked.ConversationId.Value);
        var first = await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}&includeTests=true&limit=1&sort=created&order=asc");
        var second = await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?q={word}&includeTests=true&limit=1&sort=created&order=asc&cursor={first.NextCursor}");
        first.Items.Concat(second.Items).Select(item => item.Id).ShouldBe(found.Items.Select(item => item.Id), ignoreOrder: true);

        var history = await app.Client.GetJsonAsync<ConversationHistoryDto>($"/api/conversations/{asked.ConversationId}/history");
        history.Handovers.Single().Status.ShouldBe("pending");
        history.Feedback.Single().Rating.ShouldBe(2);
        using (var missing = await app.Client.GetAsync($"/api/conversations/{Guid.NewGuid()}/history", Ct))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        foreach (var (query, field) in new[] { ("cursor=nope", "cursor"), ("minRating=4&maxRating=2", "minRating"), ("status=busy", "status"), ("limit=500", "limit"), ("sort=title", "sort") })
        {
            using var invalid = await app.Client.GetAsync($"/api/conversations/search?{query}", Ct);
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey(field);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Analytics_list_the_questions_that_needed_a_person()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var widget = await CreateWidgetAsync(app);
        var word = ApiClient.UniqueWord();
        var asked = await StartVisitorConversationAsync(app, widget.PublicKey, $"Do you deliver {word} on Sundays?");
        await PostAsVisitorAsync(app, widget.PublicKey, asked, $"conversations/{asked.ConversationId}/handover", new VisitorHandoverRequest(Email: "ana@visitor.test"));
        await StartVisitorConversationAsync(app, widget.PublicKey, "Hello there");

        var analytics = await app.Client.GetJsonAsync<AnalyticsDto>("/api/analytics?days=7");

        analytics.Days.ShouldBe(7);
        analytics.Daily.Count.ShouldBe(7);
        (analytics.Totals.Conversations, analytics.Totals.HandedToPerson, analytics.Totals.AnsweredWithoutPerson).ShouldBe((2, 1, 1));
        analytics.Daily[^1].Conversations.ShouldBe(2);
        analytics.Widgets.Single().ShouldSatisfyAllConditions(item => item.WidgetId.ShouldBe(widget.Id), item => item.HandedToPerson.ShouldBe(1));
        var question = analytics.Unanswered.Single(item => item.Reasons.Contains(UnansweredReasons.Handover));
        (question.Question, question.ConversationId).ShouldBe(($"Do you deliver {word} on Sundays?", asked.ConversationId!.Value));

        (await app.Client.GetJsonAsync<AnalyticsDto>("/api/analytics")).Days.ShouldBe(AnalyticsEndpoints.DefaultDays);
        using var invalid = await app.Client.GetAsync($"/api/analytics?days={AnalyticsEndpoints.MaxDays + 1}", Ct);
        (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("days");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Teams_build_datasets_of_their_own_from_a_copy_and_from_conversations()
    {
        var app = await fixture.GetAppAsync();
        var name = "Returns " + ApiClient.UniqueWord();

        EvalDatasetDto copy;
        using (var created = await app.Client.PostJsonAsync("/api/evals/datasets", new CreateEvalDatasetRequest(name + " copy", CopyFrom: Golden)))
        {
            copy = await created.ReadJsonAsync<EvalDatasetDto>(HttpStatusCode.Created);
        }

        var golden = (await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets")).Datasets.Single(dataset => dataset.Name == Golden);
        (copy.Source, copy.ItemCount, copy.CopiedFrom).ShouldBe((EvalDatasetSources.Custom, golden.ItemCount, $"builtin:{Golden}"));

        EvalDatasetDto dataset;
        using (var created = await app.Client.PostJsonAsync("/api/evals/datasets", new CreateEvalDatasetRequest(name, "Questions about returns")))
        {
            dataset = await created.ReadJsonAsync<EvalDatasetDto>(HttpStatusCode.Created);
        }

        foreach (var (request, field) in new[]
        {
            (new CreateEvalDatasetRequest(name.ToUpperInvariant()), "name"),
            (new CreateEvalDatasetRequest(Golden), "name"),
            (new CreateEvalDatasetRequest("bad/name"), "name"),
            (new CreateEvalDatasetRequest(name + " 2", CopyFrom: "missing"), "copyFrom"),
        })
        {
            using var invalid = await app.Client.PostJsonAsync("/api/evals/datasets", request);
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey(field);
        }

        var conversationId = Guid.NewGuid();
        EvalDatasetItemDto item;
        using (var added = await app.Client.PostJsonAsync(
            $"/api/evals/datasets/{dataset.Id}/items",
            new SaveEvalItemRequest("Can I return a kettle?", "Yes, within 30 days.", ["30 days"], Tags: ["returns"], ConversationId: conversationId)))
        {
            item = await added.ReadJsonAsync<EvalDatasetItemDto>(HttpStatusCode.Created);
        }

        item.ConversationId.ShouldBe(conversationId);
        using (var invalid = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items", new SaveEvalItemRequest("  ")))
        {
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("question");
        }

        using (var updated = await app.Client.PutJsonAsync($"/api/evals/datasets/{dataset.Id}/items/{item.Id}", new SaveEvalItemRequest("Can I return a used kettle?", ExpectedFacts: ["30 days", "unused"])))
        {
            var changed = await updated.ReadJsonAsync<EvalDatasetItemDto>();
            (changed.Question, changed.ConversationId).ShouldBe(("Can I return a used kettle?", (Guid?)conversationId));
            changed.ExpectedFacts.ShouldBe(["30 days", "unused"]);
        }

        using (var renamed = await app.Client.PatchJsonAsync($"/api/evals/datasets/{dataset.Id}", new UpdateEvalDatasetRequest(name + " v2")))
        {
            (await renamed.ReadJsonAsync<EvalDatasetDto>()).Name.ShouldBe(name + " v2");
        }

        var listed = (await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets")).Datasets.Single(entry => entry.Id == dataset.Id);
        (listed.Name, listed.ItemCount, listed.Source).ShouldBe((name + " v2", 1, EvalDatasetSources.Custom));
        (await app.Client.GetJsonAsync<EvalDatasetItemDto[]>($"/api/evals/datasets/{dataset.Id}/items")).Single().Id.ShouldBe(item.Id);

        (await app.Client.DeleteAsync($"/api/evals/datasets/{dataset.Id}/items/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await app.Client.DeleteAsync($"/api/evals/datasets/{dataset.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await app.Client.DeleteAsync($"/api/evals/datasets/{dataset.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Client.DeleteAsync($"/api/evals/datasets/{copy.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_dataset_of_their_own_runs_against_the_live_knowledge_base()
    {
        var app = await fixture.GetAppAsync();
        var word = ApiClient.UniqueWord();
        var title = $"Return rules {word}";
        await app.AddReadyTextSourceAsync(title, $"The {word} return window is 30 days for every order.");
        var name = "live " + word;
        EvalDatasetDto dataset;
        using (var created = await app.Client.PostJsonAsync("/api/evals/datasets", new CreateEvalDatasetRequest(name)))
        {
            dataset = await created.ReadJsonAsync<EvalDatasetDto>(HttpStatusCode.Created);
        }

        using (var added = await app.Client.PostJsonAsync(
            $"/api/evals/datasets/{dataset.Id}/items",
            new SaveEvalItemRequest($"What is the {word} return window?", "30 days.", ["30 days"], [title], Tags: ["returns"])))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var start = await app.Client.PostJsonAsync("/api/evals/runs", new StartEvalRunRequest(name, Source: EvalDatasetSources.Custom));
        var queued = await start.ReadJsonAsync<EvalRunSummaryDto>(HttpStatusCode.Accepted);
        var run = await WaitForRunAsync(app, queued.Id);

        run.Summary.Status.ShouldBe(EvalRunStatuses.Completed, run.Summary.Error);
        (run.Summary.DatasetSource, run.Config.Target).ShouldBe((EvalDatasetSources.Custom, "live"));
        var result = run.Items.Single();
        result.Tags.ShouldBe(["returns"]);
        result.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
    }

    private static async Task<EvalRunDto> WaitForRunAsync(KnowledgeAgentApp app, Guid id)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(90))
        {
            var run = await app.Client.GetJsonAsync<EvalRunDto>($"/api/evals/runs/{id}");
            if (EvalRunStatuses.IsFinished(run.Summary.Status))
            {
                return run;
            }

            await Task.Delay(200, Ct);
        }

        throw new ShouldAssertException($"Eval run {id} did not finish.{Environment.NewLine}{app.Logs.Dump()}");
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

public sealed class InMemoryConversationInsightsScenarios(InMemoryAppFixture fixture) : ConversationInsightsScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersConversationInsightsScenarios(ContainersAppFixture fixture) : ConversationInsightsScenarios(fixture), IClassFixture<ContainersAppFixture>;
