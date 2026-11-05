using System.Text;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Sources;

public sealed class SourceServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Url_source_is_stored_pending_with_a_default_title_and_queued()
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddUrlAsync("  https://Example.com/docs/getting-started?ref=nav  ", cancellationToken: Ct));

        source.Type.ShouldBe(SourceType.Url);
        source.Status.ShouldBe(SourceStatus.Pending);
        source.Url.ShouldBe("https://example.com/docs/getting-started?ref=nav");
        source.Title.ShouldBe("example.com/docs/getting-started");
        source.Metadata[SourceMetadataKeys.AutoTitle].ShouldBe("true");
        source.CreatedAt.ShouldBe(KnowledgeTestContext.Start);
        context.Repository.Find(source.Id).ShouldNotBeNull();
        context.Repository.FindContent(source.Id).ShouldBeNull();
        context.DrainQueue().ShouldBe([source.Id]);
    }

    [Fact]
    public async Task New_sources_are_filed_in_the_requested_folder()
    {
        await using var context = new KnowledgeTestContext();
        var folderId = Guid.NewGuid();

        var page = await context.WithSourceServiceAsync(s => s.AddUrlAsync("https://example.com/a", folderId: folderId, cancellationToken: Ct));
        var file = await context.WithSourceServiceAsync(s =>
            s.AddFileAsync("a.txt", "text/plain", "hello"u8.ToArray(), folderId: folderId, cancellationToken: Ct));
        var text = await context.WithSourceServiceAsync(s => s.AddTextAsync("Notes", "Some text.", folderId: folderId, cancellationToken: Ct));
        var unfiled = await context.WithSourceServiceAsync(s => s.AddTextAsync("Loose", "More text.", cancellationToken: Ct));

        foreach (var source in new[] { page, file, text })
        {
            context.Repository.Find(source.Id).ShouldNotBeNull().FolderId.ShouldBe(folderId);
        }

        context.Repository.Find(unfiled.Id).ShouldNotBeNull().FolderId.ShouldBeNull();
        context.Repository.Find(text.Id).ShouldNotBeNull().Site.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://www.Example.com/docs/page", "example.com")]
    [InlineData("http://docs.python.org:8080/3/library/?q=www.x", "docs.python.org")]
    [InlineData("https://WWW.www.example.org./path", "www.example.org")]
    [InlineData("https://bücher.example/", "xn--bcher-kva.example")]
    [InlineData("https://www./", "www")]
    public async Task Url_sources_record_their_site(string url, string site)
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddUrlAsync(url, cancellationToken: Ct));

        source.Site.ShouldBe(site);
        context.Repository.Find(source.Id).ShouldNotBeNull().Site.ShouldBe(site);
    }

    [Fact]
    public async Task Url_source_keeps_a_user_title()
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddUrlAsync("https://example.com/", "  Example   home  ", cancellationToken: Ct));

        source.Title.ShouldBe("Example home");
        source.Metadata.ShouldNotContainKey(SourceMetadataKeys.AutoTitle);
    }

    [Fact]
    public async Task Root_url_title_is_the_host()
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddUrlAsync("https://example.com", cancellationToken: Ct));

        source.Title.ShouldBe("example.com");
        source.Url.ShouldBe("https://example.com/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("example.com/page")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("https://user:secret@example.com/")]
    public async Task Invalid_urls_are_rejected(string url)
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<ValidationFailedException>(() => context.WithSourceServiceAsync(s => s.AddUrlAsync(url, cancellationToken: Ct)));

        ex.Errors.Keys.ShouldBe(["url"]);
        context.Repository.Count.ShouldBe(0);
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Urls_longer_than_2048_characters_are_rejected()
    {
        await using var context = new KnowledgeTestContext();
        var url = "https://example.com/" + new string('a', 2049 - "https://example.com/".Length);

        var ex = await Should.ThrowAsync<ValidationFailedException>(() => context.WithSourceServiceAsync(s => s.AddUrlAsync(url, cancellationToken: Ct)));

        ex.Errors["url"].ShouldHaveSingleItem().ShouldContain("2048");
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/admin")]
    [InlineData("http://localhost:9/")]
    [InlineData("http://[::1]:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/intranet")]
    [InlineData("http://192.168.1.1/router")]
    public async Task Private_urls_are_accepted_and_then_blocked_when_fetched(string url)
    {
        await using var context = new KnowledgeTestContext(o => o.AllowPrivateNetworks = false);

        var source = await context.WithSourceServiceAsync(s => s.AddUrlAsync(url, cancellationToken: Ct));
        source.Status.ShouldBe(SourceStatus.Pending);
        context.DrainQueue().ShouldBe([source.Id]);
        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Error!.ShouldContain("not allowed");
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Overlong_title_is_rejected()
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<ValidationFailedException>(() =>
            context.WithSourceServiceAsync(s => s.AddUrlAsync("https://example.com/", new string('t', 301), cancellationToken: Ct)));

        ex.Errors.Keys.ShouldBe(["title"]);
    }

    [Fact]
    public async Task Pdf_upload_stores_raw_bytes_and_is_queued()
    {
        await using var context = new KnowledgeTestContext();
        var bytes = TestDocuments.QuarterlyReportPdf();

        var source = await context.WithSourceServiceAsync(s => s.AddFileAsync("Q3 report.pdf", "application/pdf", bytes, cancellationToken: Ct));

        source.Type.ShouldBe(SourceType.Pdf);
        source.Status.ShouldBe(SourceStatus.Pending);
        source.FileName.ShouldBe("Q3 report.pdf");
        source.ContentType.ShouldBe("application/pdf");
        source.SizeBytes.ShouldBe(bytes.Length);
        source.Title.ShouldBe("Q3 report");
        source.Metadata[SourceMetadataKeys.AutoTitle].ShouldBe("true");
        var content = context.Repository.FindContent(source.Id)!;
        content.RawBytes.ShouldBe(bytes);
        content.ExtractedText.ShouldBeNull();
        context.DrainQueue().ShouldBe([source.Id]);
    }

    [Theory]
    [InlineData("notes.md", "application/octet-stream", "Markdown", "text/markdown")]
    [InlineData("NOTES.MARKDOWN", null, "Markdown", "text/markdown")]
    [InlineData("readme.txt", "text/plain; charset=utf-8", "Text", "text/plain")]
    [InlineData("notes", "text/markdown", "Markdown", "text/markdown")]
    [InlineData("log.data", "text/plain", "Text", "text/plain")]
    public async Task Text_uploads_get_their_type_from_the_extension_or_content_type(string fileName, string? contentType, string expectedType, string expectedContentType)
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddFileAsync(fileName, contentType, Encoding.UTF8.GetBytes("# Hello\n\nWorld"), cancellationToken: Ct));

        source.Type.ToString().ShouldBe(expectedType);
        source.ContentType.ShouldBe(expectedContentType);
    }

    [Fact]
    public async Task Upload_file_names_lose_client_paths()
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddFileAsync(@"C:\fakepath\folder/report.pdf", null, TestDocuments.QuarterlyReportPdf(), cancellationToken: Ct));

        source.FileName.ShouldBe("report.pdf");
        source.Title.ShouldBe("report");
    }

    [Fact]
    public async Task Upload_with_user_title_keeps_it()
    {
        await using var context = new KnowledgeTestContext();

        var source = await context.WithSourceServiceAsync(s => s.AddFileAsync("a.txt", "text/plain", "hello"u8.ToArray(), "Greeting", cancellationToken: Ct));

        source.Title.ShouldBe("Greeting");
        source.Metadata.ShouldNotContainKey(SourceMetadataKeys.AutoTitle);
    }

    [Fact]
    public async Task Invalid_uploads_are_rejected_with_the_file_field()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxUploadBytes = 1024);
        var pdf = TestDocuments.QuarterlyReportPdf();

        async Task<ValidationFailedException> Reject(string fileName, string? contentType, byte[] content) =>
            await Should.ThrowAsync<ValidationFailedException>(() => context.WithSourceServiceAsync(s => s.AddFileAsync(fileName, contentType, content, cancellationToken: Ct)));

        (await Reject("empty.txt", "text/plain", [])).Errors["file"].ShouldHaveSingleItem().ShouldContain("empty");
        (await Reject("big.txt", "text/plain", new byte[1025])).Errors["file"].ShouldHaveSingleItem().ShouldContain("too large");
        (await Reject("report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "x"u8.ToArray())).Errors["file"].ShouldHaveSingleItem().ShouldContain("Unsupported");
        (await Reject("image.png", "image/png", "x"u8.ToArray())).Errors.Keys.ShouldBe(["file"]);
        (await Reject("fake.pdf", "application/pdf", "not really a pdf"u8.ToArray())).Errors["file"].ShouldHaveSingleItem().ShouldContain("not a valid PDF");
        (await Reject("binary.txt", "text/plain", [0x01, 0x00, 0x02])).Errors["file"].ShouldHaveSingleItem().ShouldContain("readable text");
        pdf.Length.ShouldBeGreaterThan(1024);
        (await Reject("large.pdf", "application/pdf", pdf)).Errors.Keys.ShouldBe(["file"]);
        context.Repository.Count.ShouldBe(0);
        context.Queue.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("Text", "text/plain")]
    [InlineData("Markdown", "text/markdown")]
    public async Task Text_source_stores_the_text_directly(string type, string expectedContentType)
    {
        await using var context = new KnowledgeTestContext();
        var sourceType = Enum.Parse<SourceType>(type);

        var source = await context.WithSourceServiceAsync(s => s.AddTextAsync("  Meeting notes ", "We agreed to ship on Friday.", sourceType, cancellationToken: Ct));

        source.Type.ShouldBe(sourceType);
        source.Title.ShouldBe("Meeting notes");
        source.ContentType.ShouldBe(expectedContentType);
        source.SizeBytes.ShouldBe(28);
        source.Metadata.ShouldNotContainKey(SourceMetadataKeys.AutoTitle);
        var content = context.Repository.FindContent(source.Id)!;
        content.ExtractedText.ShouldBe("We agreed to ship on Friday.");
        content.RawBytes.ShouldBeNull();
        context.DrainQueue().ShouldBe([source.Id]);
    }

    [Theory]
    [InlineData("", "content", new[] { "title" })]
    [InlineData("Title", " \n ", new[] { "content" })]
    [InlineData(" ", "", new[] { "title", "content" })]
    public async Task Text_source_requires_title_and_content(string title, string content, string[] fields)
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<ValidationFailedException>(() => context.WithSourceServiceAsync(s => s.AddTextAsync(title, content, cancellationToken: Ct)));

        ex.Errors.Keys.ShouldBe(fields, ignoreOrder: true);
    }

    [Theory]
    [InlineData("Url")]
    [InlineData("Pdf")]
    public async Task Text_source_must_be_text_or_markdown(string type)
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<ValidationFailedException>(() =>
            context.WithSourceServiceAsync(s => s.AddTextAsync("Title", "Content", Enum.Parse<SourceType>(type), cancellationToken: Ct)));

        ex.Errors.Keys.ShouldBe(["format"]);
    }

    [Fact]
    public async Task Text_larger_than_the_upload_limit_is_rejected()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxUploadBytes = 10);

        var ex = await Should.ThrowAsync<ValidationFailedException>(() => context.WithSourceServiceAsync(s => s.AddTextAsync("Title", "more than ten bytes", cancellationToken: Ct)));

        ex.Errors.Keys.ShouldBe(["content"]);
    }

    [Fact]
    public async Task Delete_removes_vectors_content_and_source()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("To delete", TestDocuments.Prose(2));
        var other = await context.SeedTextAsync("To keep", TestDocuments.Prose(2));
        await context.ProcessAsync(source.Id, Ct);
        await context.ProcessAsync(other.Id, Ct);

        var deleted = await context.WithSourceServiceAsync(s => s.DeleteAsync(source.Id, Ct));

        deleted.ShouldBeTrue();
        context.Repository.Find(source.Id).ShouldBeNull();
        context.Repository.FindContent(source.Id).ShouldBeNull();
        context.VectorStore.ForSource(source.Id).ShouldBeEmpty();
        context.VectorStore.ForSource(other.Id).ShouldNotBeEmpty();
        (await context.WithSourceServiceAsync(s => s.DeleteAsync(source.Id, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Reingest_resets_the_source_and_queues_it()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxAttempts = 1);
        context.Embeddings.AlwaysFail = true;
        var source = await context.SeedTextAsync("Flaky", TestDocuments.Prose(1));
        await context.ProcessAsync(source.Id, Ct);
        context.Repository.Find(source.Id)!.Status.ShouldBe(SourceStatus.Failed);
        context.Time.Advance(TimeSpan.FromMinutes(5));

        var reset = await context.WithSourceServiceAsync(s => s.ReingestAsync(source.Id, Ct));

        reset.ShouldNotBeNull();
        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Pending);
        stored.Attempts.ShouldBe(0);
        stored.Error.ShouldBeNull();
        stored.UpdatedAt.ShouldBe(KnowledgeTestContext.Start.AddMinutes(5));
        context.DrainQueue().ShouldBe([source.Id]);

        context.Embeddings.AlwaysFail = false;
        await context.ProcessAsync(source.Id, Ct);
        context.Repository.Find(source.Id)!.Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task Reingest_clears_the_progress_of_the_last_attempt()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedAsync(new Source
        {
            Type = SourceType.Text,
            Title = "Stuck",
            Status = SourceStatus.Processing,
            Stage = SourceStage.Embedding,
            EmbeddedChunks = 7,
            ChunkCount = 12,
        });

        await context.WithSourceServiceAsync(s => s.ReingestAsync(source.Id, Ct));

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Pending);
        stored.Stage.ShouldBeNull();
        stored.EmbeddedChunks.ShouldBe(0);
    }

    [Fact]
    public async Task Reingest_of_unknown_source_returns_null()
    {
        await using var context = new KnowledgeTestContext();

        (await context.WithSourceServiceAsync(s => s.ReingestAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Reingest_all_queues_every_source()
    {
        await using var context = new KnowledgeTestContext();
        var first = await context.SeedTextAsync("First", "one");
        context.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await context.SeedTextAsync("Second", "two");
        await context.ProcessAsync(first.Id, Ct);

        var count = await context.WithSourceServiceAsync(s => s.ReingestAllAsync(Ct));

        count.ShouldBe(2);
        context.DrainQueue().ShouldBe([first.Id, second.Id]);
        context.Repository.Find(first.Id)!.Status.ShouldBe(SourceStatus.Pending);
    }

    [Fact]
    public async Task Queuing_waiting_sources_resets_only_those_that_wait_for_a_model()
    {
        await using var context = new KnowledgeTestContext();
        var waiting = await WaitingSourceAsync(context, "Waiting");
        context.Embeddings.AlwaysFail = false;
        var ready = await context.SeedTextAsync("Ready", "two");
        await context.ProcessAsync(ready.Id, Ct);
        context.DrainQueue();

        var count = await context.WithSourceServiceAsync(s => s.QueueWaitingAsync(Ct));

        count.ShouldBe(1);
        context.DrainQueue().ShouldBe([waiting.Id]);
        var reset = context.Repository.Find(waiting.Id)!;
        reset.Status.ShouldBe(SourceStatus.Pending);
        reset.Error.ShouldBeNull();
        context.Repository.Find(ready.Id)!.Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task Waiting_sources_are_queued_once_an_embedding_model_resolves()
    {
        await using var context = new KnowledgeTestContext();
        var waiting = await WaitingSourceAsync(context, "Waiting");
        var queuer = context.Services.GetRequiredService<WaitingSourcesQueuer>();

        (await queuer.QueueIfReadyAsync(Ct)).ShouldBe(0, "still no embedding model");
        context.DrainQueue().ShouldBeEmpty();

        context.Embeddings.AlwaysFail = false;
        (await queuer.QueueIfReadyAsync(Ct)).ShouldBe(1);
        context.DrainQueue().ShouldBe([waiting.Id]);

        await context.ProcessAsync(waiting.Id, Ct);
        context.Repository.Find(waiting.Id)!.Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task Find_stats_and_get_come_from_the_repository()
    {
        await using var context = new KnowledgeTestContext();
        var older = await context.SeedTextAsync("Older", "one");
        context.Time.Advance(TimeSpan.FromSeconds(1));
        var newer = await context.SeedTextAsync("Newer", "two");
        await context.ProcessAsync(older.Id, Ct);

        var all = await context.WithSourceServiceAsync(s => s.FindAsync(new SourceQuery(), Ct));
        var firstPage = await context.WithSourceServiceAsync(s => s.FindAsync(new SourceQuery(Limit: 1), Ct));
        var searched = await context.WithSourceServiceAsync(s => s.FindAsync(new SourceQuery("OLD"), Ct));
        var ready = await context.WithSourceServiceAsync(s => s.FindAsync(new SourceQuery(Statuses: [SourceStatus.Ready]), Ct));
        var stats = await context.WithSourceServiceAsync(s => s.GetStatsAsync(Ct));
        var fetched = await context.WithSourceServiceAsync(s => s.GetAsync(older.Id, Ct));

        all.Items.Select(s => s.Id).ShouldBe([newer.Id, older.Id]);
        all.Total.ShouldBe(2);
        firstPage.Items.Select(s => s.Id).ShouldBe([newer.Id]);
        firstPage.NextOffset.ShouldBe(1);
        searched.Items.Select(s => s.Id).ShouldBe([older.Id]);
        ready.Items.Select(s => s.Id).ShouldBe([older.Id]);

        var processed = context.Repository.Find(older.Id)!;
        stats.Count(SourceStatus.Ready).ShouldBe(1);
        stats.Count(SourceStatus.Pending).ShouldBe(1);
        stats.ReadyChunkCount.ShouldBe(processed.ChunkCount);
        stats.ReadyCountByType.ShouldBe(new Dictionary<SourceType, int> { [SourceType.Text] = 1 });
        stats.ReadyCountByEmbeddingModel.ShouldBe(new Dictionary<string, int> { [processed.EmbeddingModel!] = 1 });

        fetched!.Title.ShouldBe("Older");
        (await context.WithSourceServiceAsync(s => s.GetAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Chunks_are_paged_from_the_vector_store()
    {
        await using var context = new KnowledgeTestContext(o =>
        {
            o.ChunkSizeTokens = 30;
            o.ChunkOverlapTokens = 0;
        });
        var source = await context.SeedTextAsync("Paged", TestDocuments.Prose(3));
        await context.ProcessAsync(source.Id, Ct);
        var total = context.Repository.Find(source.Id)!.ChunkCount;

        var page = await context.WithSourceServiceAsync(s => s.GetChunksAsync(source.Id, skip: 2, take: 3, Ct));
        var clamped = await context.WithSourceServiceAsync(s => s.GetChunksAsync(source.Id, skip: -5, take: 0, Ct));

        total.ShouldBeGreaterThan(5);
        page.Select(c => c.ChunkIndex).ShouldBe([2, 3, 4]);
        page.ShouldAllBe(c => c.SourceId == source.Id && c.Metadata[ChunkMetadataKeys.Title] == "Paged");
        clamped.Select(c => c.ChunkIndex).ShouldBe([0]);
    }

    // Leaves the embedding service failing, as it does while no embedding model is configured.
    private static async Task<Source> WaitingSourceAsync(KnowledgeTestContext context, string title)
    {
        context.Embeddings.AlwaysFail = true;
        context.Embeddings.FailureFactory = () => new ModelNotConfiguredException("No configured embedding provider is available for the 'embedding' profile.");
        var source = await context.SeedTextAsync(title, "one");
        await context.ProcessAsync(source.Id, Ct);
        context.Repository.Find(source.Id)!.Status.ShouldBe(SourceStatus.Waiting);
        context.DrainQueue();
        return source;
    }
}
