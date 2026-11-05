using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class EvalDatasetScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_upload_adds_new_questions_in_one_go_and_skips_the_ones_the_dataset_has()
    {
        var app = await fixture.GetAppAsync();
        var dataset = await CreateDatasetAsync(app);
        using (var added = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items", new SaveEvalItemRequest("Can I return a kettle?")))
        {
            (await added.ReadJsonAsync<EvalDatasetItemDto>(HttpStatusCode.Created)).Source.ShouldBe(CustomDatasetItemSources.Manual);
        }

        var rows = new[]
        {
            new SaveEvalItemRequest("  can I   RETURN a kettle? "),
            new SaveEvalItemRequest("How long is the warranty?", "Two years.", ["two years"], Tags: ["warranty"]),
            new SaveEvalItemRequest("how long is the warranty?"),
            new SaveEvalItemRequest(
                "And for refurbished ones?",
                "One year.",
                History: [new EvalTurn("user", "How long is the warranty?"), new EvalTurn("assistant", "Two years.")]),
        };
        ImportEvalItemsResultDto result;
        using (var imported = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items/batch", new ImportEvalItemsRequest(rows)))
        {
            result = await imported.ReadJsonAsync<ImportEvalItemsResultDto>();
        }

        (result.Created, result.Skipped).ShouldBe((2, 2));
        result.SkippedRows.ShouldBe([0, 2]);
        result.Items.ShouldAllBe(item => item.Source == CustomDatasetItemSources.Upload);

        var items = await app.Client.GetJsonAsync<EvalDatasetItemDto[]>($"/api/evals/datasets/{dataset.Id}/items");
        items.Select(item => item.Question).ShouldBe(["Can I return a kettle?", "How long is the warranty?", "And for refurbished ones?"]);
        items[2].History.ShouldBe([new EvalTurn("user", "How long is the warranty?"), new EvalTurn("assistant", "Two years.")]);
        items[0].History.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets")).Datasets.Single(entry => entry.Id == dataset.Id).ItemCount.ShouldBe(3);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_upload_with_a_bad_row_saves_nothing_and_names_the_row()
    {
        var app = await fixture.GetAppAsync();
        var dataset = await CreateDatasetAsync(app);

        using (var invalid = await app.Client.PostJsonAsync(
            $"/api/evals/datasets/{dataset.Id}/items/batch",
            new ImportEvalItemsRequest([
                new SaveEvalItemRequest("Fine question?"),
                new SaveEvalItemRequest(" "),
                new SaveEvalItemRequest("Odd turn?", History: [new EvalTurn("system", "Be brief.")]),
            ])))
        {
            var problem = await invalid.ReadProblemAsync(HttpStatusCode.BadRequest);
            problem.FieldErrors.Keys.ShouldBe(["items[1].question", "items[2].history"], ignoreOrder: true);
        }

        (await app.Client.GetJsonAsync<EvalDatasetItemDto[]>($"/api/evals/datasets/{dataset.Id}/items")).ShouldBeEmpty();

        var tooMany = Enumerable.Range(0, CustomDatasetService.MaxBatchItems + 1).Select(index => new SaveEvalItemRequest($"Question {index}?")).ToList();
        using (var invalid = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items/batch", new ImportEvalItemsRequest(tooMany)))
        {
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("items");
        }

        using (var empty = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items/batch", new ImportEvalItemsRequest([])))
        {
            (await empty.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("items");
        }

        using var missing = await app.Client.PostJsonAsync($"/api/evals/datasets/{Guid.NewGuid()}/items/batch", new ImportEvalItemsRequest([new SaveEvalItemRequest("Hi?")]));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Several_questions_are_removed_at_once_and_a_question_keeps_where_it_came_from()
    {
        var app = await fixture.GetAppAsync();
        var dataset = await CreateDatasetAsync(app);
        var conversationId = Guid.NewGuid();
        var ids = new List<Guid>();
        foreach (var request in new[]
        {
            new SaveEvalItemRequest("From a chat?", ConversationId: conversationId),
            new SaveEvalItemRequest("Typed in?"),
            new SaveEvalItemRequest("Also typed in?"),
        })
        {
            using var added = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items", request);
            ids.Add((await added.ReadJsonAsync<EvalDatasetItemDto>(HttpStatusCode.Created)).Id);
        }

        using (var edited = await app.Client.PutJsonAsync($"/api/evals/datasets/{dataset.Id}/items/{ids[0]}", new SaveEvalItemRequest("From a chat, edited?", Source: "upload")))
        {
            (await edited.ReadJsonAsync<EvalDatasetItemDto>()).Source.ShouldBe(CustomDatasetItemSources.Conversation);
        }

        using (var deleted = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items/delete", new DeleteEvalItemsRequest([ids[1], ids[2], Guid.NewGuid()])))
        {
            (await deleted.ReadJsonAsync<DeleteEvalItemsResultDto>()).Deleted.ShouldBe(2);
        }

        (await app.Client.GetJsonAsync<EvalDatasetItemDto[]>($"/api/evals/datasets/{dataset.Id}/items")).Single().Id.ShouldBe(ids[0]);
        using (var none = await app.Client.PostJsonAsync($"/api/evals/datasets/{dataset.Id}/items/delete", new DeleteEvalItemsRequest([])))
        {
            (await none.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("ids");
        }

        (await app.Client.DeleteAsync($"/api/evals/datasets/{dataset.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<EvalDatasetDto> CreateDatasetAsync(KnowledgeAgentApp app)
    {
        using var created = await app.Client.PostJsonAsync("/api/evals/datasets", new CreateEvalDatasetRequest("Upload " + ApiClient.UniqueWord()));
        return await created.ReadJsonAsync<EvalDatasetDto>(HttpStatusCode.Created);
    }
}

public sealed class InMemoryEvalDatasetScenarios(InMemoryAppFixture fixture) : EvalDatasetScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersEvalDatasetScenarios(ContainersAppFixture fixture) : EvalDatasetScenarios(fixture), IClassFixture<ContainersAppFixture>;
