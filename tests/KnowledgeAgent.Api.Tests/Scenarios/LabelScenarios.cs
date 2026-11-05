using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class LabelScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Conversations_are_labeled_automatically_then_renamed_pinned_relabeled_and_filtered()
    {
        await using var app = await fixture.CreateAppAsync();
        var defaults = await app.Client.GetJsonAsync<LabelDto[]>("/api/labels");
        defaults.Select(label => label.Name).ShouldBe(["Feedback", "Problem", "Question"]);
        defaults.ShouldAllBe(label => label.AutoApply && label.Description.Length > 0);

        var word = ApiClient.UniqueWord();
        using var created = await app.Client.PostJsonAsync("/api/labels", new CreateLabelRequest($"Refunds {word}", "purple", "The person wants money back.", AutoApply: true));
        var refunds = await created.ReadJsonAsync<LabelDto>(HttpStatusCode.Created);
        (refunds.Color, refunds.ConversationCount).ShouldBe(("purple", 0));

        var utility = app.ChatModels.Use("utility", new ScriptedChatClient())
            .ThenText("Refund question")
            .ThenText($$"""{"labels": ["{{refunds.Id}}", "{{Guid.NewGuid()}}"]}""");
        var stream = await app.ChatAsync(new ChatRequestDto(null, $"Can I get my money back for {word}?"));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        var id = stream.ConversationId;

        ConversationSummaryDto? labeled = null;
        (await ApiClient.EventuallyAsync(
            () =>
            {
                labeled = app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?labelId={refunds.Id}").GetAwaiter().GetResult().Items.SingleOrDefault();
                return labeled is not null;
            },
            TimeSpan.FromSeconds(10))).ShouldBeTrue("the job labels the chat after its first answer");
        labeled!.Labels.ShouldBe([new ConversationLabelDto(refunds.Id, refunds.Name, "purple", "auto")]);
        utility.Requests.Count.ShouldBe(2);
        utility.FallbackText = """{"labels": []}""";

        using (var patched = await app.Client.PatchJsonAsync($"/api/conversations/{id}", new UpdateConversationRequest(" Money back ", Pinned: true)))
        {
            var summary = await patched.ReadJsonAsync<ConversationSummaryDto>();
            (summary.Id, summary.Title, summary.Pinned).ShouldBe((id, "Money back", true));
        }

        (await app.ChatAsync(new ChatRequestDto(null, "Another question"))).Errors.ShouldBeEmpty();
        var list = await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?channel=portal&limit=1");
        list.Items.Single().Id.ShouldBe(id, "pinned conversations come first");
        var next = await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?channel=portal&limit=1&cursor={list.NextCursor}");
        next.Items.Single().Id.ShouldNotBe(id);
        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?q={word}&pinned=true")).Items.Single().Id.ShouldBe(id);
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?labelId={refunds.Id}&channel=portal")).Items.Single().Labels.Single().Source.ShouldBe("auto");

        using (var cleared = await app.Client.PutJsonAsync($"/api/conversations/{id}/labels", new SetConversationLabelsRequest([])))
        {
            (await cleared.ReadJsonAsync<ConversationLabelDto[]>()).ShouldBeEmpty();
        }

        // Both queued chats get the same answer, so the result does not depend on which job runs first.
        utility.FallbackText = $$"""{"labels": ["{{refunds.Id}}"]}""";
        var asked = utility.Requests.Count;
        int queued;
        using (var relabel = await app.Client.PostAsync("/api/labels/relabel?days=1", null, Ct))
        {
            queued = (await relabel.ReadJsonAsync<RelabelResponse>(HttpStatusCode.Accepted)).Queued;
        }

        queued.ShouldBe(2);
        (await ApiClient.EventuallyAsync(() => utility.Requests.Count >= asked + queued, TimeSpan.FromSeconds(10))).ShouldBeTrue("each queued chat is labeled again");
        (await app.Client.GetJsonAsync<ConversationHistoryDto>($"/api/conversations/{id}/history")).Labels.ShouldBeEmpty();

        using (var manual = await app.Client.PutJsonAsync($"/api/conversations/{id}/labels", new SetConversationLabelsRequest([refunds.Id])))
        {
            (await manual.ReadJsonAsync<ConversationLabelDto[]>()).Single().Source.ShouldBe("manual");
        }

        (await app.Client.GetJsonAsync<LabelDto[]>("/api/labels")).Single(label => label.Id == refunds.Id).ConversationCount.ShouldBe(2);
        using (var deleted = await app.Client.DeleteAsync($"/api/labels/{refunds.Id}", Ct))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?q={word}")).Items.Single().Labels.ShouldBeEmpty();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_labels_and_lists_are_rejected()
    {
        var app = await fixture.GetAppAsync();
        var word = ApiClient.UniqueWord();
        using var created = await app.Client.PostJsonAsync("/api/labels", new CreateLabelRequest($"Billing {word}"));
        var billing = await created.ReadJsonAsync<LabelDto>(HttpStatusCode.Created);
        billing.Color.ShouldBe("gray");

        foreach (var (body, field) in new (CreateLabelRequest Body, string Field)[]
                 {
                     (new CreateLabelRequest($"BILLING {word}"), "name"),
                     (new CreateLabelRequest(" "), "name"),
                     (new CreateLabelRequest(new string('x', 41)), "name"),
                     (new CreateLabelRequest($"Other {word}", "orange"), "color"),
                 })
        {
            using var invalid = await app.Client.PostJsonAsync("/api/labels", body);
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey(field);
        }

        using (var missing = await app.Client.PatchJsonAsync($"/api/labels/{Guid.NewGuid()}", new UpdateLabelRequest("x")))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var stream = await app.ChatAsync(new ChatRequestDto(null, $"Where is my invoice {word}?"));
        using (var unknown = await app.Client.PutJsonAsync($"/api/conversations/{stream.ConversationId}/labels", new SetConversationLabelsRequest([Guid.NewGuid()])))
        {
            (await unknown.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("labelIds");
        }

        using (var title = await app.Client.PatchJsonAsync($"/api/conversations/{stream.ConversationId}", new UpdateConversationRequest(" ")))
        {
            (await title.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("title");
        }

        var searchCursor = new ConversationCursor(DateTimeOffset.UtcNow, Guid.NewGuid()).Encode();
        foreach (var (query, field) in new[] { ("limit=101", "limit"), ("cursor=nope", "cursor"), ($"cursor={searchCursor}", "cursor"), ("channel=fax", "channel") })
        {
            using var invalid = await app.Client.GetAsync($"/api/conversations?{query}", Ct);
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey(field);
        }

        using var relabel = await app.Client.PostAsync("/api/labels/relabel?days=91", null, Ct);
        (await relabel.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("days");
    }
}

public sealed class InMemoryLabelScenarios(InMemoryAppFixture fixture) : LabelScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersLabelScenarios(ContainersAppFixture fixture) : LabelScenarios(fixture), IClassFixture<ContainersAppFixture>;
