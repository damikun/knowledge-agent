using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class SourceFolderScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Folders_group_sources_without_changing_what_search_finds()
    {
        await using var app = await fixture.CreateAppAsync();
        var marker = ApiClient.UniqueWord();

        var legal = await CreateFolderAsync(app, "Legal");
        var contracts = await CreateFolderAsync(app, "Contracts", legal.Id);
        var lease = await AddTextAsync(app, $"{marker} lease", $"The {marker} lease renews every March.", contracts.Id);
        var memo = await AddTextAsync(app, $"{marker} memo", $"The {marker} memo explains each renewal step.");
        lease.FolderId.ShouldBe(contracts.Id);
        (await app.WaitForSourceAsync(lease.Id)).FolderId.ShouldBe(contracts.Id);
        (await app.WaitForSourceAsync(memo.Id)).FolderId.ShouldBeNull();

        var tree = await app.Client.GetJsonAsync<SourceFolderListDto>("/api/sources/folders");
        tree.Folders.Select(folder => (folder.Name, folder.ParentId, folder.SourceCount))
            .ShouldBe([("Contracts", legal.Id, 1), ("Internal", null, 0), ("Legal", null, 0)], ignoreOrder: true);
        tree.Folders.Where(folder => folder.IsInternal).ShouldHaveSingleItem().Id.ShouldBe(SourceFolder.InternalId);
        tree.UnfiledCount.ShouldBe(1);
        tree.TotalCount.ShouldBe(2);

        (await ListAsync(app, $"folder={contracts.Id}")).Items.ShouldHaveSingleItem().Id.ShouldBe(lease.Id);
        (await ListAsync(app, "folder=UNFILED")).Items.ShouldHaveSingleItem().Id.ShouldBe(memo.Id);
        (await ListAsync(app, $"folder={legal.Id}")).Total.ShouldBe(0, "a folder lists only the sources directly in it");
        (await ListAsync(app, $"folder={contracts.Id}&query={marker}&type=text&status=ready")).Total.ShouldBe(1);
        (await ListAsync(app, $"folder={contracts.Id}&type=pdf")).Total.ShouldBe(0);
        (await app.SearchAsync($"{marker} renewal")).Select(hit => hit.SourceId).Distinct().ShouldBe([lease.Id, memo.Id], ignoreOrder: true);

        (await MoveSourcesAsync(app, [memo.Id, Guid.NewGuid()], legal.Id)).ShouldBe(1);
        (await app.Client.GetJsonAsync<SourceDto>($"/api/sources/{memo.Id}")).FolderId.ShouldBe(legal.Id);

        using (var rename = await app.Client.PatchJsonAsync($"/api/sources/folders/{legal.Id}", new RenameSourceFolderRequest("  Legal   and compliance ")))
        {
            var renamed = await rename.ReadJsonAsync<SourceFolderDto>();
            renamed.Name.ShouldBe("Legal and compliance");
            renamed.SourceCount.ShouldBe(1);
        }

        using (var moveUp = await app.Client.PostJsonAsync($"/api/sources/folders/{contracts.Id}/move", new MoveSourceFolderRequest(null)))
        {
            (await moveUp.ReadJsonAsync<SourceFolderDto>()).ParentId.ShouldBeNull();
        }

        var drafts = await CreateFolderAsync(app, "Drafts", legal.Id);
        (await MoveSourcesAsync(app, [lease.Id], drafts.Id)).ShouldBe(1);
        using (var delete = await app.Client.DeleteAsync($"/api/sources/folders/{legal.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var after = await app.Client.GetJsonAsync<SourceFolderListDto>("/api/sources/folders");
        after.Folders.Select(folder => folder.Id).ShouldBe([contracts.Id, SourceFolder.InternalId], ignoreOrder: true);
        after.UnfiledCount.ShouldBe(2);
        after.TotalCount.ShouldBe(2);
        (await app.Client.GetJsonAsync<SourceDto>($"/api/sources/{lease.Id}")).FolderId.ShouldBeNull();
        (await app.SearchAsync($"{marker} renewal")).Select(hit => hit.SourceId).Distinct().Count().ShouldBe(2, "deleting folders keeps their sources");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_source_moved_while_it_is_ingested_stays_in_its_new_folder()
    {
        var embeddings = new HeldEmbeddings();
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = embeddings.Install);
        var folder = await CreateFolderAsync(app, "Inbox");
        var source = await AddTextAsync(app, "Quarterly report", "Revenue grew in every region this quarter.");

        await embeddings.Started.Task.WaitAsync(ApiClient.IngestionTimeout, Ct);
        (await app.Client.GetJsonAsync<SourceDto>($"/api/sources/{source.Id}")).Status.ShouldBe("processing");
        (await MoveSourcesAsync(app, [source.Id], folder.Id)).ShouldBe(1);
        embeddings.Release.SetResult();

        (await app.WaitForSourceAsync(source.Id)).FolderId.ShouldBe(folder.Id);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Pages_and_uploads_can_be_filed_when_they_are_added()
    {
        var app = await fixture.GetAppAsync();
        var web = await fixture.GetWebServerAsync();
        var folder = await CreateFolderAsync(app, $"Web {ApiClient.UniqueWord()}");
        var path = $"/folders/{ApiClient.UniqueWord()}.html";
        web.AddHtml(path, TestDocuments.HtmlArticle("Filed page", ["A page that is filed in a folder as soon as it is added."]));

        using var page = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest(web.Url(path).ToString(), FolderId: folder.Id));
        var added = await page.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        using var upload = await UploadAsync(app, "notes.md", "# Notes\n\nFiled on upload.", folder.Id.ToString());
        var uploaded = await upload.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);

        added.FolderId.ShouldBe(folder.Id);
        uploaded.FolderId.ShouldBe(folder.Id);
        (await app.WaitForSourceAsync(added.Id)).FolderId.ShouldBe(folder.Id);
        (await app.WaitForSourceAsync(uploaded.Id)).FolderId.ShouldBe(folder.Id);
        (await ListAsync(app, $"folder={folder.Id}")).Items.Select(source => source.Id).ShouldBe([uploaded.Id, added.Id]);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Pages_of_a_site_are_counted_filtered_and_moved_together()
    {
        var app = await fixture.GetAppAsync();
        var web = await fixture.GetWebServerAsync();
        var inbox = await CreateFolderAsync(app, $"Inbox {ApiClient.UniqueWord()}");
        var docs = await CreateFolderAsync(app, $"Docs {ApiClient.UniqueWord()}");
        var marker = ApiClient.UniqueWord();
        var paths = new[] { "intro", "install", "faq" }.Select(name => $"/sites/{marker}-{name}.html").ToList();
        foreach (var path in paths)
        {
            web.AddHtml(path, TestDocuments.HtmlArticle($"{marker} {path}", [$"The {marker} page {path} belongs to a documentation site."]));
        }

        // The test server listens on 127.0.0.1, so a localhost URL is a second site with the same pages.
        var intro = await AddUrlAsync(app, web.Url(paths[0]), inbox.Id);
        var install = await AddUrlAsync(app, web.Url(paths[1]), inbox.Id);
        var faq = await AddUrlAsync(app, new UriBuilder(web.Url(paths[2])) { Host = "localhost" }.Uri, inbox.Id);
        var note = await AddTextAsync(app, $"{marker} note", "Pasted text has no site.", inbox.Id);

        intro.Site.ShouldBe("127.0.0.1");
        faq.Site.ShouldBe("localhost");
        note.Site.ShouldBeNull();
        (await app.Client.GetJsonAsync<SourceSiteDto[]>($"/api/sources/sites?folder={inbox.Id}"))
            .ShouldBe([new SourceSiteDto("127.0.0.1", 2), new SourceSiteDto("localhost", 1)]);
        (await app.Client.GetJsonAsync<SourceSiteDto[]>($"/api/sources/sites?folder={inbox.Id}&type=text")).ShouldBeEmpty();
        (await ListAsync(app, $"folder={inbox.Id}&site=127.0.0.1")).Items.Select(source => source.Id).ShouldBe([install.Id, intro.Id]);
        (await ListAsync(app, $"folder={inbox.Id}&site=WWW.LocalHost")).Items.ShouldHaveSingleItem().Id.ShouldBe(faq.Id);

        using (var move = await app.Client.PostJsonAsync(
            "/api/sources/move",
            new MoveSourcesRequest(null, docs.Id, new SourceMatchRequest(Folder: inbox.Id.ToString(), Site: "127.0.0.1"))))
        {
            (await move.ReadJsonAsync<MoveSourcesResponse>()).Moved.ShouldBe(2);
        }

        (await ListAsync(app, $"folder={docs.Id}")).Items.Select(source => source.Id).ShouldBe([install.Id, intro.Id]);
        (await ListAsync(app, $"folder={inbox.Id}")).Items.Select(source => source.Id).ShouldBe([note.Id, faq.Id]);

        using (var unfile = await app.Client.PostJsonAsync(
            "/api/sources/move",
            new MoveSourcesRequest([], null, new SourceMatchRequest(Query: marker, Folder: docs.Id.ToString()))))
        {
            (await unfile.ReadJsonAsync<MoveSourcesResponse>()).Moved.ShouldBe(2);
        }

        (await ListAsync(app, $"folder=unfiled&query={marker}")).Total.ShouldBe(2);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_folder_requests_return_problem_details()
    {
        var app = await fixture.GetAppAsync();
        var parent = await CreateFolderAsync(app, $"Parent {ApiClient.UniqueWord()}");
        var child = await CreateFolderAsync(app, "Child", parent.Id);
        var grandchild = await CreateFolderAsync(app, "Grandchild", child.Id);
        var unknown = Guid.NewGuid();

        (await CreateProblemAsync(app, new CreateSourceFolderRequest("   "))).ShouldBe(("name", "A folder name is required."));
        (await CreateProblemAsync(app, new CreateSourceFolderRequest(new string('x', 101)))).ShouldBe(("name", "The name must be at most 100 characters long."));
        (await CreateProblemAsync(app, new CreateSourceFolderRequest("CHILD", parent.Id)))
            .ShouldBe(("name", $"A folder named 'CHILD' already exists in '{parent.Name}'."));
        (await CreateProblemAsync(app, new CreateSourceFolderRequest("Lost", unknown))).ShouldBe(("parentId", "The parent folder does not exist."));
        (await CreateProblemAsync(app, new CreateSourceFolderRequest("Too deep", grandchild.Id)))
            .ShouldBe(("parentId", "Folders can be nested at most 3 levels deep."));

        using (var cycle = await app.Client.PostJsonAsync($"/api/sources/folders/{parent.Id}/move", new MoveSourceFolderRequest(grandchild.Id)))
        {
            (await cycle.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["parentId"]
                .ShouldBe(["A folder cannot be moved into itself or one of its subfolders."]);
        }

        foreach (var request in new Func<Task<HttpResponseMessage>>[]
        {
            () => app.Client.GetAsync($"/api/sources/folders/{unknown}", Ct),
            () => app.Client.PatchJsonAsync($"/api/sources/folders/{unknown}", new RenameSourceFolderRequest("Name")),
            () => app.Client.PostJsonAsync($"/api/sources/folders/{unknown}/move", new MoveSourceFolderRequest(null)),
            () => app.Client.DeleteAsync($"/api/sources/folders/{unknown}", Ct),
        })
        {
            using var response = await request();
            (await response.ReadProblemAsync(HttpStatusCode.NotFound)).Detail.ShouldBe($"Folder '{unknown}' was not found.");
        }

        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest([], parent.Id))).ShouldBe(("sourceIds", "Choose at least one source."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest(null, parent.Id))).ShouldBe(("sourceIds", "Choose at least one source."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest([.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())], null)))
            .ShouldBe(("sourceIds", "At most 100 sources can be moved at once."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest([Guid.NewGuid()], unknown))).ShouldBe(("folderId", "The folder does not exist."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest([Guid.NewGuid()], parent.Id, new SourceMatchRequest(Site: "example.com"))))
            .ShouldBe(("matching", "Send either sourceIds or matching, not both."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest(null, parent.Id, new SourceMatchRequest(Type: "video"))))
            .ShouldBe(("matching.type", "type must be one of: url, pdf, text, markdown, website."));
        (await MoveSourcesProblemAsync(app, new MoveSourcesRequest(null, unknown, new SourceMatchRequest(Site: "example.com"))))
            .ShouldBe(("folderId", "The folder does not exist."));

        using (var longSite = await app.Client.GetAsync($"/api/sources?site={new string('a', 256)}", Ct))
        {
            (await longSite.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["site"].ShouldBe(["site must be at most 255 characters."]);
        }

        using (var badSites = await app.Client.GetAsync("/api/sources/sites?status=done&folder=nowhere", Ct))
        {
            (await badSites.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys.ShouldBe(["status", "folder"], ignoreOrder: true);
        }

        using (var badFilter = await app.Client.GetAsync("/api/sources?folder=inbox", Ct))
        {
            (await badFilter.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["folder"].ShouldBe(["folder must be a folder id or 'unfiled'."]);
        }

        using (var text = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest("Note", "Text.", FolderId: unknown)))
        {
            (await text.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["folderId"].ShouldBe(["The folder does not exist."]);
        }

        using (var url = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest("https://example.com/", FolderId: unknown)))
        {
            (await url.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["folderId"].ShouldBe(["The folder does not exist."]);
        }

        using (var malformed = await UploadAsync(app, "a.txt", "Text.", "not-a-folder"))
        {
            (await malformed.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["folderId"].ShouldBe(["folderId must be a folder id."]);
        }

        using (var missing = await UploadAsync(app, "a.txt", "Text.", unknown.ToString()))
        {
            (await missing.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["folderId"].ShouldBe(["The folder does not exist."]);
        }

        using var badGuid = await app.Client.PostAsync(
            "/api/sources/folders", new StringContent("""{"name":"Bad","parentId":"not-a-guid"}""", Encoding.UTF8, "application/json"), Ct);
        await badGuid.ReadProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Folder_endpoints_are_described_in_the_OpenAPI_document()
    {
        var app = await fixture.GetAppAsync();

        using var document = JsonDocument.Parse(await app.Client.GetStringAsync("/openapi/v1.json", Ct));

        var paths = document.RootElement.GetProperty("paths");
        var operations = new Dictionary<string, string>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(operation => operation.Value.TryGetProperty("operationId", out _)))
            {
                operations[operation.Value.GetProperty("operationId").GetString()!] = $"{operation.Name.ToUpperInvariant()} {path.Name}";
            }
        }

        operations.ShouldContainKeyAndValue("ListSourceFolders", "GET /api/sources/folders");
        operations.ShouldContainKeyAndValue("CreateSourceFolder", "POST /api/sources/folders");
        operations.ShouldContainKeyAndValue("GetSourceFolder", "GET /api/sources/folders/{id}");
        operations.ShouldContainKeyAndValue("RenameSourceFolder", "PATCH /api/sources/folders/{id}");
        operations.ShouldContainKeyAndValue("MoveSourceFolder", "POST /api/sources/folders/{id}/move");
        operations.ShouldContainKeyAndValue("DeleteSourceFolder", "DELETE /api/sources/folders/{id}");
        operations.ShouldContainKeyAndValue("MoveSources", "POST /api/sources/move");
        operations.ShouldContainKeyAndValue("ListSourceSites", "GET /api/sources/sites");
        paths.GetProperty("/api/sources/folders").GetProperty("post").GetProperty("responses").TryGetProperty("400", out _).ShouldBeTrue();
    }

    private static async Task<SourceFolderDto> CreateFolderAsync(KnowledgeAgentApp app, string name, Guid? parentId = null)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/folders", new CreateSourceFolderRequest(name, parentId));
        var folder = await response.ReadJsonAsync<SourceFolderDto>(HttpStatusCode.Created);
        response.Headers.Location?.ToString().ShouldBe($"/api/sources/folders/{folder.Id}");
        folder.Name.ShouldBe(name);
        folder.ParentId.ShouldBe(parentId);
        folder.SourceCount.ShouldBe(0);
        return folder;
    }

    private static async Task<SourceDto> AddUrlAsync(KnowledgeAgentApp app, Uri url, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest(url.ToString(), FolderId: folderId));
        return await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
    }

    private static async Task<SourceDto> AddTextAsync(KnowledgeAgentApp app, string title, string content, Guid? folderId = null)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content, FolderId: folderId));
        return await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
    }

    private static async Task<int> MoveSourcesAsync(KnowledgeAgentApp app, IReadOnlyList<Guid> sourceIds, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/move", new MoveSourcesRequest(sourceIds, folderId));
        return (await response.ReadJsonAsync<MoveSourcesResponse>()).Moved;
    }

    private static Task<SourcePageDto> ListAsync(KnowledgeAgentApp app, string query) =>
        app.Client.GetJsonAsync<SourcePageDto>($"/api/sources?{query}");

    private static async Task<(string Field, string Message)> CreateProblemAsync(KnowledgeAgentApp app, CreateSourceFolderRequest request)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/folders", request);
        var error = (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldHaveSingleItem();
        return (error.Key, error.Value.ShouldHaveSingleItem());
    }

    private static async Task<(string Field, string Message)> MoveSourcesProblemAsync(KnowledgeAgentApp app, MoveSourcesRequest request)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/move", request);
        var error = (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldHaveSingleItem();
        return (error.Key, error.Value.ShouldHaveSingleItem());
    }

    private static async Task<HttpResponseMessage> UploadAsync(KnowledgeAgentApp app, string fileName, string text, string folderId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(folderId), "folderId");
        return await app.Client.PostAsync("/api/sources/upload", form, Ct);
    }

    // Holds document embeddings, which keeps a source in processing until the test releases it.
    private sealed class HeldEmbeddings
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Install(IServiceCollection services)
        {
            services.RemoveAll<IEmbeddingService>();
            services.AddSingleton<EmbeddingService>();
            services.AddSingleton<IEmbeddingService>(provider => new Gate(provider.GetRequiredService<EmbeddingService>(), this));
        }

        private sealed class Gate(IEmbeddingService inner, HeldEmbeddings held) : IEmbeddingService
        {
            public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => inner.IsAvailableAsync(cancellationToken);

            public async Task<EmbeddingResult> EmbedAsync(
                IReadOnlyList<string> inputs,
                EmbeddingPurpose purpose = EmbeddingPurpose.Document,
                CancellationToken cancellationToken = default)
            {
                if (purpose == EmbeddingPurpose.Document)
                {
                    held.Started.TrySetResult();
                    await held.Release.Task.WaitAsync(cancellationToken);
                }

                return await inner.EmbedAsync(inputs, purpose, cancellationToken);
            }
        }
    }
}

public sealed class InMemorySourceFolderScenarios(InMemoryAppFixture fixture) : SourceFolderScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersSourceFolderScenarios(ContainersAppFixture fixture) : SourceFolderScenarios(fixture), IClassFixture<ContainersAppFixture>;
