using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class SourceScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Text_source_is_ingested_chunked_and_searchable()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        const string title = "Zephyr reactor notes";
        var content =
            $"The {marker} reactor uses molten salt cooling and produces three hundred megawatts. "
            + $"Engineers inspect the {marker} reactor turbines every spring.";

        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content));
        var created = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        response.Headers.Location?.ToString().ShouldBe($"/api/sources/{created.Id}");
        created.Type.ShouldBe("text");
        created.Title.ShouldBe(title);
        created.Status.ShouldBeOneOf("pending", "processing", "ready");

        var ready = await app.WaitForSourceAsync(created.Id);
        ready.ChunkCount.ShouldBeGreaterThan(0);
        ready.TokenEstimate.ShouldBeGreaterThan(0);
        ready.EmbeddingModel.ShouldNotBeNullOrWhiteSpace();
        ready.EmbeddingModel.ShouldContain(FakeModels.EmbeddingModel);
        ready.ProcessedAt.ShouldNotBeNull();
        ready.Error.ShouldBeNull();

        var listed = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?ids={created.Id}");
        listed.Items.ShouldHaveSingleItem().Status.ShouldBe("ready");

        var chunks = await app.Client.GetJsonAsync<ChunkDto[]>($"/api/sources/{created.Id}/chunks");
        chunks.Length.ShouldBe(ready.ChunkCount);
        chunks.Select(c => c.ChunkIndex).ShouldBe(Enumerable.Range(0, chunks.Length));
        string.Join(' ', chunks.Select(c => c.Content)).ShouldContain(marker);

        var firstPage = await app.Client.GetJsonAsync<ChunkDto[]>($"/api/sources/{created.Id}/chunks?skip=0&take=1");
        firstPage.ShouldHaveSingleItem().ChunkIndex.ShouldBe(0);

        var hits = await app.SearchAsync($"{marker} reactor cooling");
        hits.ShouldNotBeEmpty();
        hits[0].SourceId.ShouldBe(created.Id);
        hits[0].SourceTitle.ShouldBe(title);
        hits[0].Content.ShouldContain(marker);
        hits[0].Score.ShouldBeGreaterThan(0);
        hits.Select(h => h.Score).ShouldBeInOrder(SortDirection.Descending);

        var scoped = await app.SearchAsync($"{marker} turbines", topK: 3, sourceIds: [created.Id]);
        scoped.ShouldNotBeEmpty();
        scoped.Length.ShouldBeLessThanOrEqualTo(3);
        scoped.ShouldAllBe(h => h.SourceId == created.Id);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Search_modes_report_what_each_retriever_found()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync($"{marker} fault codes", $"Fault {marker}-214 means the encoder on joint 2 lost its index.");
        var query = $"what does {marker}-214 mean";

        var hybrid = (await SearchAsync(new SearchRequest(query, SourceIds: [source.Id]))).ShouldHaveSingleItem();
        hybrid.SourceId.ShouldBe(source.Id);
        hybrid.VectorRank.ShouldBe(1);
        hybrid.KeywordRank.ShouldBe(1);
        hybrid.VectorScore.ShouldNotBeNull();
        hybrid.KeywordScore.ShouldNotBeNull().ShouldBeGreaterThan(0);
        hybrid.Score.ShouldBe(1, 1e-9, "ranked first by both retrievers");
        hybrid.RerankScore.ShouldBeNull("no reranker is configured");

        var keyword = (await SearchAsync(new SearchRequest(query, SourceIds: [source.Id], Mode: "KEYWORD", Rerank: true))).ShouldHaveSingleItem();
        keyword.KeywordRank.ShouldBe(1);
        keyword.VectorRank.ShouldBeNull();
        keyword.VectorScore.ShouldBeNull();
        keyword.Score.ShouldBe(1, 1e-9);

        var vector = (await SearchAsync(new SearchRequest(query, SourceIds: [source.Id], Mode: "vector"))).ShouldHaveSingleItem();
        vector.VectorRank.ShouldBe(1);
        vector.KeywordRank.ShouldBeNull();
        vector.Score.ShouldBe(vector.VectorScore.ShouldNotBeNull());

        var unrelated = await SearchAsync(new SearchRequest($"{ApiClient.UniqueWord()} {ApiClient.UniqueWord()}", Mode: "keyword"));
        unrelated.ShouldBeEmpty("keyword search finds nothing without a matching term");

        using var badMode = await app.Client.PostJsonAsync("/api/search", new SearchRequest("anything", Mode: "fuzzy"));
        (await badMode.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("mode");

        async Task<SearchHitDto[]> SearchAsync(SearchRequest request)
        {
            using var response = await app.Client.PostJsonAsync("/api/search", request);
            return await response.ReadJsonAsync<SearchHitDto[]>();
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Url_source_is_fetched_from_the_web_and_titled_from_the_page()
    {
        var app = await fixture.GetAppAsync();
        var web = await fixture.GetWebServerAsync();
        var marker = ApiClient.UniqueWord();
        var path = $"/articles/{marker}.html";
        const string pageTitle = "Aurora Borealis Field Guide";
        web.AddHtml(path, TestDocuments.HtmlArticle(pageTitle,
        [
            $"The {marker} aurora appears when charged solar particles collide with gases in the upper atmosphere.",
            $"Observers in northern Norway photograph the {marker} aurora on clear winter nights between September and March.",
            "Green light comes from oxygen about one hundred kilometres up, while rare red light comes from oxygen far higher.",
            "Strong geomagnetic storms push the display towards the equator, so it can occasionally be seen in central Europe.",
        ]));
        var url = web.Url(path).ToString();

        using var response = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest(url));
        var created = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        response.Headers.Location?.ToString().ShouldBe($"/api/sources/{created.Id}");
        created.Type.ShouldBe("url");
        created.Url.ShouldNotBeNull().ShouldContain(path);

        var ready = await app.WaitForSourceAsync(created.Id);
        ready.Title.ShouldBe(pageTitle);
        ready.ChunkCount.ShouldBeGreaterThan(0);
        web.Hits(path).ShouldBeGreaterThan(0);

        var hits = await app.SearchAsync($"{marker} aurora atmosphere", sourceIds: [created.Id]);
        hits.ShouldNotBeEmpty();
        hits[0].SourceTitle.ShouldBe(pageTitle);
        hits[0].SourceUrl.ShouldNotBeNull().ShouldContain(path);
        string.Join(' ', hits.Select(h => h.Content)).ShouldContain(marker);
        string.Join(' ', hits.Select(h => h.Content)).ShouldNotContain("Copyright Test Site");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Pdf_upload_is_ingested_with_its_page_count()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        var pdf = TestDocuments.Pdf(
            $"Glacier handbook page one. The {marker} glacier advances slowly down the valley and carves deep fjords. "
            + "Ice cores record centuries of snowfall and volcanic ash layers.",
            $"Glacier handbook page two. Meltwater from the {marker} glacier feeds a turquoise lake every summer. "
            + "Rangers measure the retreat of the glacier tongue each autumn.");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "glaciers.pdf");
        form.Add(new StringContent("Glacier handbook"), "title");

        using var response = await app.Client.PostAsync("/api/sources/upload", form, Ct);
        var created = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        response.Headers.Location?.ToString().ShouldBe($"/api/sources/{created.Id}");
        created.Type.ShouldBe("pdf");
        created.Title.ShouldBe("Glacier handbook");
        created.FileName.ShouldBe("glaciers.pdf");
        created.SizeBytes.ShouldBe(pdf.Length);

        var ready = await app.WaitForSourceAsync(created.Id);
        ready.PageCount.ShouldBe(2);
        ready.ChunkCount.ShouldBeGreaterThan(0);

        var chunks = await app.Client.GetJsonAsync<ChunkDto[]>($"/api/sources/{created.Id}/chunks");
        string.Join(' ', chunks.Select(c => c.Content)).ShouldContain("turquoise lake");
        chunks.ShouldAllBe(c => c.Page == null || c.Page == 1 || c.Page == 2);

        var hits = await app.SearchAsync($"{marker} glacier meltwater turquoise lake", sourceIds: [created.Id]);
        hits.ShouldNotBeEmpty();
        hits[0].SourceTitle.ShouldBe("Glacier handbook");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_sources_are_rejected_with_validation_problems()
    {
        var app = await fixture.GetAppAsync();
        var before = await CountSourcesAsync(app);

        using (var badUrl = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest("not a url")))
        {
            (await badUrl.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldNotBeEmpty();
        }

        using (var ftpUrl = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest("ftp://example.com/file.txt")))
        {
            (await ftpUrl.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldNotBeEmpty();
        }

        using (var missingUrl = await app.Client.PostJsonAsync("/api/sources/url", new { title = "No URL" }))
        {
            (await missingUrl.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("url");
        }

        using (var unsupported = await UploadAsync(app, "tool.exe", "application/octet-stream", [0x4D, 0x5A, 0x90, 0x00, 0x03]))
        {
            (await unsupported.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldNotBeEmpty();
        }

        using (var noFile = new MultipartFormDataContent { { new StringContent("Only a title"), "title" } })
        using (var missingFile = await app.Client.PostAsync("/api/sources/upload", noFile, Ct))
        {
            (await missingFile.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("file");
        }

        using (var emptyText = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest("Empty", "   ")))
        {
            (await emptyText.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("content");
        }

        using (var badFormat = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest("Html", "<p>x</p>", "html")))
        {
            (await badFormat.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("format");
        }

        using (var malformed = await app.Client.PostAsync(
                   "/api/sources/text",
                   new StringContent("{\"title\": ", Encoding.UTF8, "application/json"),
                   Ct))
        {
            await malformed.ReadProblemAsync(HttpStatusCode.BadRequest);
        }

        (await CountSourcesAsync(app)).ShouldBe(before, "rejected requests must not create sources");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Markdown_upload_and_markdown_text_are_typed_as_markdown()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();

        using var upload = await UploadAsync(
            app,
            "notes.md",
            "text/markdown",
            Encoding.UTF8.GetBytes($"# Beekeeping\n\nThe {marker} hive produces heather honey in August.\n"));
        var uploaded = await upload.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        uploaded.Type.ShouldBe("markdown");
        uploaded.FileName.ShouldBe("notes.md");

        var pasted = await app.AddTextSourceAsync("Pasted markdown", $"## Hives\n\n* {marker} queen bee", "markdown");
        pasted.Type.ShouldBe("markdown");

        await app.WaitForSourceAsync(uploaded.Id);
        await app.WaitForSourceAsync(pasted.Id);
        var hits = await app.SearchAsync($"{marker} heather honey", sourceIds: [uploaded.Id]);
        hits.ShouldNotBeEmpty();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Deleted_source_disappears_from_lookups_and_search()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync(
            "Volcano log",
            $"The {marker} volcano erupted in nineteen twelve and covered the valley in ash.");
        (await app.SearchAsync($"{marker} volcano ash")).ShouldContain(h => h.SourceId == source.Id);

        using (var delete = await app.Client.DeleteAsync($"/api/sources/{source.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var get = await app.Client.GetAsync($"/api/sources/{source.Id}", Ct))
        {
            await get.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var chunks = await app.Client.GetAsync($"/api/sources/{source.Id}/chunks", Ct))
        {
            await chunks.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        (await app.SearchAsync($"{marker} volcano ash", topK: 20)).ShouldNotContain(h => h.SourceId == source.Id);
        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?ids={source.Id}")).Total.ShouldBe(0);

        using var again = await app.Client.DeleteAsync($"/api/sources/{source.Id}", Ct);
        await again.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Reingest_reprocesses_a_source_and_reindex_queues_every_source()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        var source = await app.AddReadyTextSourceAsync("Orchard diary", $"The {marker} orchard grows forty apple varieties.");

        using (var reingest = await app.Client.PostAsync($"/api/sources/{source.Id}/reingest", null, Ct))
        {
            var accepted = await reingest.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
            reingest.Headers.Location?.ToString().ShouldBe($"/api/sources/{source.Id}");
            accepted.Id.ShouldBe(source.Id);
            accepted.Status.ShouldBeOneOf("pending", "processing", "ready");
        }

        var reprocessed = await app.WaitForSourceAsync(
            source.Id,
            s => s.Status == "ready" && s.ProcessedAt > source.ProcessedAt);
        reprocessed.ChunkCount.ShouldBe(source.ChunkCount);
        (await app.SearchAsync($"{marker} orchard apple", sourceIds: [source.Id])).ShouldNotBeEmpty();

        var total = await CountSourcesAsync(app);
        using (var reindex = await app.Client.PostAsync("/api/sources/reindex", null, Ct))
        {
            var queued = await reindex.ReadJsonAsync<ReindexResponse>();
            queued.Queued.ShouldBeGreaterThanOrEqualTo(1);
            queued.Queued.ShouldBeLessThanOrEqualTo(total);
        }

        var reindexed = await app.WaitForSourceAsync(
            source.Id,
            s => s.Status == "ready" && s.ProcessedAt > reprocessed.ProcessedAt);
        reindexed.ChunkCount.ShouldBe(source.ChunkCount);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Unknown_sources_and_bad_paging_return_problem_details()
    {
        var app = await fixture.GetAppAsync();
        var unknown = Guid.NewGuid();

        using (var get = await app.Client.GetAsync($"/api/sources/{unknown}", Ct))
        {
            (await get.ReadProblemAsync(HttpStatusCode.NotFound)).Detail.ShouldNotBeNull().ShouldContain(unknown.ToString());
        }

        using (var reingest = await app.Client.PostAsync($"/api/sources/{unknown}/reingest", null, Ct))
        {
            await reingest.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var chunks = await app.Client.GetAsync($"/api/sources/{unknown}/chunks", Ct))
        {
            await chunks.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var badPaging = await app.Client.GetAsync($"/api/sources/{unknown}/chunks?skip=-1&take=0", Ct))
        {
            var problem = await badPaging.ReadProblemAsync(HttpStatusCode.BadRequest);
            problem.FieldErrors.ShouldContainKey("skip");
            problem.FieldErrors.ShouldContainKey("take");
        }

        using (var emptyQuery = await app.Client.PostJsonAsync("/api/search", new SearchRequest("  ")))
        {
            (await emptyQuery.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("query");
        }

        using var badTopK = await app.Client.PostJsonAsync("/api/search", new SearchRequest("anything", TopK: 0));
        (await badTopK.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("topK");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Sources_are_found_page_by_page_with_search_and_filters()
    {
        var app = await fixture.GetAppAsync();
        var marker = ApiClient.UniqueWord();
        var alpha = await app.AddReadyTextSourceAsync($"{marker} alpha handbook", "Alpha notes about apples.");
        var beta = await app.WaitForSourceAsync(
            (await app.AddTextSourceAsync($"Beta {marker} guide", "# Beta\n\nNotes about bees.", "markdown")).Id);
        var gamma = await app.AddReadyTextSourceAsync($"Gamma {marker} handbook", "Gamma notes about grapes.");

        var first = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&limit=2");
        first.Items.Select(s => s.Id).ShouldBe([alpha.Id, beta.Id]);
        first.Total.ShouldBe(3);
        first.Offset.ShouldBe(0);
        first.Limit.ShouldBe(2);
        first.NextOffset.ShouldBe(2);

        var second = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&limit=2&offset={first.NextOffset}");
        second.Items.Select(s => s.Id).ShouldBe([gamma.Id]);
        second.Total.ShouldBe(3);
        second.Offset.ShouldBe(2);
        second.NextOffset.ShouldBeNull();

        var beyond = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&offset=50");
        beyond.Items.ShouldBeEmpty();
        beyond.Total.ShouldBe(3);
        beyond.NextOffset.ShouldBeNull();

        var words = await app.Client.GetJsonAsync<SourcePageDto>(
            $"/api/sources?query={Uri.EscapeDataString($"  {marker.ToUpperInvariant()}   HANDBOOK ")}");
        words.Items.Select(s => s.Id).ShouldBe([gamma.Id, alpha.Id]);
        words.Total.ShouldBe(2);

        var markdown = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&type=markdown");
        markdown.Items.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);

        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&status=READY")).Total.ShouldBe(3);
        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&status=pending,failed")).Total.ShouldBe(0);
        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&status=processing&status=ready")).Total.ShouldBe(3);
        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={marker}&status=any")).Total.ShouldBe(3);

        var byIds = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?ids={alpha.Id},{gamma.Id}");
        byIds.Items.Select(s => s.Id).ShouldBe([gamma.Id, alpha.Id]);
        byIds.Total.ShouldBe(2);

        var repeated = await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?ids={alpha.Id}&ids={beta.Id}&limit=1");
        repeated.Items.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);
        repeated.Total.ShouldBe(2);
        repeated.NextOffset.ShouldBe(1);

        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?ids={Guid.NewGuid()}")).Total.ShouldBe(0);
        (await app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?query={ApiClient.UniqueWord()}")).Total.ShouldBe(0);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_source_queries_return_field_errors()
    {
        var app = await fixture.GetAppAsync();

        using (var bad = await app.Client.GetAsync("/api/sources?type=video&status=ready,done&offset=-1&limit=0&ids=not-a-guid", Ct))
        {
            (await bad.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys
                .ShouldBe(["type", "status", "offset", "limit", "ids"], ignoreOrder: true);
        }

        string[][] cases =
        [
            ["limit=101", "limit"],
            ["limit=ten", "limit"],
            ["offset=1.5", "offset"],
            ["offset=99999999999", "offset"],
            ["type=1", "type"],
            ["status=0", "status"],
            [$"query={new string('a', 501)}", "query"],
            [$"ids={string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()))}", "ids"],
        ];
        foreach (var (query, field) in cases.Select(c => (c[0], c[1])))
        {
            using var response = await app.Client.GetAsync($"/api/sources?{query}", Ct);
            (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys.ShouldBe([field], ignoreOrder: false, customMessage: query[..Math.Min(query.Length, 40)]);
        }

        var widest = await app.Client.GetJsonAsync<SourcePageDto>(
            $"/api/sources?query={new string('a', 500)}&type=&status=&offset=0&limit=100&ids=");
        widest.Limit.ShouldBe(100);
        widest.Total.ShouldBe(0);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Stats_and_the_unfiltered_list_cover_every_source()
    {
        await using var app = await fixture.CreateAppAsync();

        var empty = await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats");
        empty.Total.ShouldBe(0);
        empty.Ready.ShouldBe(0);
        empty.ReadyChunks.ShouldBe(0);
        empty.ReadyByType.ShouldBeEmpty();
        empty.ReadyByEmbeddingModel.ShouldBeEmpty();

        var none = await app.Client.GetJsonAsync<SourcePageDto>("/api/sources");
        none.Items.ShouldBeEmpty();
        none.Total.ShouldBe(0);
        none.Offset.ShouldBe(0);
        none.Limit.ShouldBe(25);
        none.NextOffset.ShouldBeNull();

        var first = await app.AddReadyTextSourceAsync("First note", "The first note is about rivers and their deltas.");
        var second = await app.WaitForSourceAsync(
            (await app.AddTextSourceAsync("Second note", "# Lakes\n\nThe second note is about mountain lakes.", "markdown")).Id);
        var third = await app.AddReadyTextSourceAsync("Third note", "The third note is about seas and tides.");

        var stats = await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats");
        stats.Total.ShouldBe(3);
        stats.Ready.ShouldBe(3);
        stats.Pending.ShouldBe(0);
        stats.Processing.ShouldBe(0);
        stats.Failed.ShouldBe(0);
        stats.ReadyChunks.ShouldBe(first.ChunkCount + second.ChunkCount + third.ChunkCount);
        stats.ReadyByType.ShouldBe(new Dictionary<string, int> { ["text"] = 2, ["markdown"] = 1 }, ignoreOrder: true);
        stats.ReadyByEmbeddingModel.ShouldBe(new Dictionary<string, int> { [first.EmbeddingModel!] = 3 });

        var all = await app.Client.GetJsonAsync<SourcePageDto>("/api/sources");
        all.Items.Select(s => s.Id).ShouldBe([third.Id, second.Id, first.Id]);
        all.Total.ShouldBe(3);
        all.NextOffset.ShouldBeNull();

        // The portal reads these exact property names.
        using (var page = JsonDocument.Parse(await app.Client.GetStringAsync("/api/sources?limit=1", Ct)))
        {
            page.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["items", "total", "offset", "limit", "nextOffset"]);
            page.RootElement.GetProperty("nextOffset").GetInt32().ShouldBe(1);
        }

        using (var json = JsonDocument.Parse(await app.Client.GetStringAsync("/api/sources/stats", Ct)))
        {
            json.RootElement.EnumerateObject().Select(p => p.Name)
                .ShouldBe(["total", "ready", "pending", "processing", "failed", "readyChunks", "readyByType", "readyByEmbeddingModel", "waiting"]);
            json.RootElement.GetProperty("readyByType").EnumerateObject().Select(p => p.Name).ShouldBe(["markdown", "text"], ignoreOrder: true);
        }

        using (var delete = await app.Client.DeleteAsync($"/api/sources/{third.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var after = await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats");
        after.Total.ShouldBe(2);
        after.ReadyChunks.ShouldBe(first.ChunkCount + second.ChunkCount);
        after.ReadyByType.ShouldBe(new Dictionary<string, int> { ["text"] = 1, ["markdown"] = 1 }, ignoreOrder: true);
        after.ReadyByEmbeddingModel.ShouldBe(new Dictionary<string, int> { [first.EmbeddingModel!] = 2 });
    }

    private static async Task<int> CountSourcesAsync(KnowledgeAgentApp app) =>
        (await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats")).Total;

    private static async Task<HttpResponseMessage> UploadAsync(KnowledgeAgentApp app, string fileName, string contentType, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await app.Client.PostAsync("/api/sources/upload", form, Ct);
    }
}

public sealed class InMemorySourceScenarios(InMemoryAppFixture fixture) : SourceScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersSourceScenarios(ContainersAppFixture fixture) : SourceScenarios(fixture), IClassFixture<ContainersAppFixture>;
