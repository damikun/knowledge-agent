using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Sources.Fakes;
using KnowledgeAgent.Tests.Features.Sources.Support;

namespace KnowledgeAgent.Tests.Features.Sources.Ingestion;

public sealed class IngestionPipelineTests
{
    private const string NoTextMessage = "No text could be extracted from the source.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_source_becomes_ready_with_chunks_vectors_and_metadata()
    {
        await using var context = new KnowledgeTestContext(o =>
        {
            o.ChunkSizeTokens = 60;
            o.ChunkOverlapTokens = 10;
        });
        var text = TestDocuments.Prose(8);
        var source = await context.SeedTextAsync("Grounding guide", text);
        context.Time.Advance(TimeSpan.FromMinutes(1));

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Attempts.ShouldBe(1);
        stored.Error.ShouldBeNull();
        stored.ChunkCount.ShouldBeGreaterThan(1);
        stored.PageCount.ShouldBeNull();
        stored.EmbeddingModel.ShouldBe(context.Embeddings.ModelKey);
        stored.TokenEstimate.ShouldBe((text.Length + 3) / 4);
        stored.ContentHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        stored.ProcessedAt.ShouldBe(KnowledgeTestContext.Start.AddMinutes(1));
        stored.UpdatedAt.ShouldBe(KnowledgeTestContext.Start.AddMinutes(1));
        context.Repository.StatusHistory(source.Id).ShouldBe([SourceStatus.Pending, SourceStatus.Processing, SourceStatus.Ready]);
        context.Repository.FindContent(source.Id)!.ExtractedText.ShouldBe(text);

        var vectors = context.VectorStore.ForSource(source.Id);
        vectors.Count.ShouldBe(stored.ChunkCount);
        vectors.Select(v => v.ChunkIndex).ShouldBe(Enumerable.Range(0, vectors.Count));
        vectors.ShouldAllBe(v => v.EmbeddingModel == context.Embeddings.ModelKey && v.Embedding.Length == context.Embeddings.Dimensions);
        vectors.ShouldAllBe(v => v.Content.Length > 0 && v.Content.Length <= 240);
        vectors.Select(v => v.ChunkId).Distinct().Count().ShouldBe(vectors.Count);
        foreach (var vector in vectors)
        {
            vector.Metadata[ChunkMetadataKeys.Title].ShouldBe("Grounding guide");
            vector.Metadata[ChunkMetadataKeys.SourceType].ShouldBe("text");
            vector.Metadata.ShouldNotContainKey(ChunkMetadataKeys.Url);
            vector.Metadata.ShouldNotContainKey(ChunkMetadataKeys.Page);
        }
    }

    [Fact]
    public async Task Pasted_text_is_normalized_before_it_is_stored_and_hashed()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Messy", "Line   one  \r\n\r\n\r\n\r\n    indented   code\n");

        await context.ProcessAsync(source.Id, Ct);

        // Runs of spaces and blank lines shrink, but leading indentation survives for code and nested lists.
        var normalized = "Line one\n\n    indented code";
        context.Repository.FindContent(source.Id)!.ExtractedText.ShouldBe(normalized);
        context.Repository.Find(source.Id)!.ContentHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))));
    }

    [Fact]
    public async Task Markdown_source_records_its_type_in_chunk_metadata()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Readme", "# Install\n\nRun the installer and follow the prompts.", SourceType.Markdown);

        await context.ProcessAsync(source.Id, Ct);

        context.Repository.Find(source.Id)!.Status.ShouldBe(SourceStatus.Ready);
        context.VectorStore.ForSource(source.Id).ShouldAllBe(v => v.Metadata[ChunkMetadataKeys.SourceType] == "markdown");
    }

    [Fact]
    public async Task Chunks_are_embedded_in_batches_with_the_document_purpose()
    {
        await using var context = new KnowledgeTestContext(o =>
        {
            o.EmbeddingBatchSize = 3;
            o.ChunkSizeTokens = 30;
            o.ChunkOverlapTokens = 0;
        });
        var source = await context.SeedTextAsync("Batches", TestDocuments.Prose(3));

        await context.ProcessAsync(source.Id, Ct);

        var chunkCount = context.Repository.Find(source.Id)!.ChunkCount;
        chunkCount.ShouldBeGreaterThan(6);
        var calls = context.Embeddings.Calls.ToList();
        calls.ShouldAllBe(c => c.Purpose == EmbeddingPurpose.Document);
        calls.Select(c => c.Inputs.Count).ShouldBe(
            Enumerable.Range(0, (chunkCount + 2) / 3).Select(i => Math.Min(3, chunkCount - (i * 3))));
        // Each chunk is embedded with its context (here the title) in front; the store keeps the two apart.
        var stored = context.VectorStore.ForSource(source.Id);
        stored.ShouldAllBe(chunk => chunk.Context == "Batches");
        calls.SelectMany(c => c.Inputs).ShouldBe(stored.Select(v => $"Batches\n\n{v.Content}"));
    }

    [Fact]
    public async Task Progress_moves_through_the_stages_and_counts_the_embedded_chunks()
    {
        await using var context = new KnowledgeTestContext(o =>
        {
            o.EmbeddingBatchSize = 3;
            o.ChunkSizeTokens = 30;
            o.ChunkOverlapTokens = 0;
        });
        var source = await context.SeedTextAsync("Progress", TestDocuments.Prose(3));
        var updates = new ConcurrentQueue<Source>();
        context.Repository.AfterUpdate = updates.Enqueue;

        await context.ProcessAsync(source.Id, "run-1", Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Stage.ShouldBeNull();
        stored.EmbeddedChunks.ShouldBe(stored.ChunkCount);
        stored.JobRunId.ShouldBe("run-1");
        var chunks = stored.ChunkCount;
        chunks.ShouldBeGreaterThan(6);
        var expected = new List<(SourceStatus, SourceStage?, int, int)>
        {
            (SourceStatus.Processing, SourceStage.Extracting, 0, 0),
            (SourceStatus.Processing, SourceStage.Chunking, 0, 0),
            (SourceStatus.Processing, SourceStage.Contextualizing, 0, chunks),
            (SourceStatus.Processing, SourceStage.Embedding, 0, chunks),
        };
        for (var embedded = 3; embedded < chunks; embedded += 3)
        {
            expected.Add((SourceStatus.Processing, SourceStage.Embedding, embedded, chunks));
        }

        expected.Add((SourceStatus.Processing, SourceStage.Storing, chunks, chunks));
        expected.Add((SourceStatus.Ready, null, chunks, chunks));
        updates.Select(update => (update.Status, update.Stage, update.EmbeddedChunks, update.ChunkCount)).ShouldBe(expected);
        updates.ShouldAllBe(update => update.JobRunId == "run-1");
    }

    [Fact]
    public async Task Url_source_reports_fetching_before_extracting()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("articles/vector-databases", TestDocuments.ArticleHtml);
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("articles/vector-databases"), title: null);
        var stages = new ConcurrentQueue<SourceStage?>();
        context.Repository.AfterUpdate = update => stages.Enqueue(update.Stage);

        await context.ProcessAsync(source.Id, Ct);

        stages.ShouldBe([SourceStage.Fetching, SourceStage.Extracting, SourceStage.Chunking, SourceStage.Contextualizing, SourceStage.Embedding, SourceStage.Storing, null]);
    }

    [Fact]
    public async Task A_failed_attempt_clears_the_stage_and_keeps_counting_the_stored_chunks()
    {
        await using var context = new KnowledgeTestContext(o =>
        {
            o.MaxAttempts = 3;
            o.EmbeddingBatchSize = 3;
            o.ChunkSizeTokens = 30;
            o.ChunkOverlapTokens = 0;
        });
        var source = new Source { Type = SourceType.Text, Title = "Reingested", ContentType = "text/plain", ChunkCount = 5 };
        await context.SeedAsync(source, new SourceContent { SourceId = source.Id, ExtractedText = TestDocuments.Prose(3) });
        context.Repository.AfterUpdate = update =>
        {
            if (update is { Stage: SourceStage.Embedding, EmbeddedChunks: 3 })
            {
                context.Embeddings.FailNext(1);
            }
        };

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Pending);
        stored.Stage.ShouldBeNull();
        stored.EmbeddedChunks.ShouldBe(3);
        stored.ChunkCount.ShouldBe(5);
    }

    [Fact]
    public async Task Pdf_source_tracks_pages_and_takes_the_document_title()
    {
        await using var context = new KnowledgeTestContext();
        var bytes = TestDocuments.QuarterlyReportPdf("Quarterly Report");
        var source = await SeedFileAsync(context, SourceType.Pdf, "q3-report.pdf", "application/pdf", bytes, autoTitle: true);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Title.ShouldBe("Quarterly Report");
        stored.PageCount.ShouldBe(2);
        context.Repository.FindContent(source.Id)!.ExtractedText!.Split('\f').Length.ShouldBe(2);
        context.Repository.FindContent(source.Id)!.RawBytes.ShouldBe(bytes);

        var vectors = context.VectorStore.ForSource(source.Id);
        vectors.Select(v => v.Metadata[ChunkMetadataKeys.Page]).Distinct().ShouldBe(["1", "2"]);
        vectors.Single(v => v.Content.Contains("Revenue grew")).Metadata[ChunkMetadataKeys.Page].ShouldBe("1");
        vectors.Single(v => v.Content.Contains("Operating expenses")).Metadata[ChunkMetadataKeys.Page].ShouldBe("2");
        vectors.ShouldAllBe(v => v.Metadata[ChunkMetadataKeys.SourceType] == "pdf" && v.Metadata[ChunkMetadataKeys.Title] == "Quarterly Report");
    }

    [Fact]
    public async Task Title_chosen_by_the_user_is_kept()
    {
        await using var context = new KnowledgeTestContext();
        var source = await SeedFileAsync(context, SourceType.Pdf, "q3-report.pdf", "application/pdf", TestDocuments.QuarterlyReportPdf(), autoTitle: false, title: "Board pack");

        await context.ProcessAsync(source.Id, Ct);

        context.Repository.Find(source.Id)!.Title.ShouldBe("Board pack");
        context.VectorStore.ForSource(source.Id).ShouldAllBe(v => v.Metadata[ChunkMetadataKeys.Title] == "Board pack");
    }

    [Fact]
    public async Task Text_file_upload_is_decoded_from_raw_bytes()
    {
        await using var context = new KnowledgeTestContext();
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Release notes\n\nVersion 2 adds offline mode.")];
        var source = await SeedFileAsync(context, SourceType.Text, "notes.txt", "text/plain", bytes, autoTitle: true);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Title.ShouldBe("notes");
        context.Repository.FindContent(source.Id)!.ExtractedText.ShouldBe("Release notes\n\nVersion 2 adds offline mode.");
    }

    [Fact]
    public async Task Url_source_is_fetched_extracted_and_titled_from_the_page()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("articles/vector-databases", TestDocuments.ArticleHtml);
        await using var context = new KnowledgeTestContext();
        var uri = server.Url("articles/vector-databases");
        var source = await SeedUrlAsync(context, uri, title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready, stored.Error);
        stored.Title.ShouldBe("Understanding Vector Databases");
        stored.Metadata[SourceMetadataKeys.FinalUrl].ShouldBe(uri.AbsoluteUri);
        stored.Metadata[SourceMetadataKeys.SiteName].ShouldBe("Example Engineering");
        stored.ContentType.ShouldBe("text/html");
        stored.SizeBytes.ShouldBe(Encoding.UTF8.GetByteCount(TestDocuments.ArticleHtml));
        stored.PageCount.ShouldBeNull();

        var text = context.Repository.FindContent(source.Id)!.ExtractedText!;
        text.ShouldContain("Vector databases store embeddings");
        text.ShouldNotContain("TRACKING_SCRIPT_MARKER");

        var vectors = context.VectorStore.ForSource(source.Id);
        vectors.ShouldNotBeEmpty();
        vectors.ShouldAllBe(v =>
            v.Metadata[ChunkMetadataKeys.Url] == uri.AbsoluteUri
            && v.Metadata[ChunkMetadataKeys.Title] == "Understanding Vector Databases"
            && v.Metadata[ChunkMetadataKeys.SourceType] == "url"
            && !v.Metadata.ContainsKey(ChunkMetadataKeys.Page));
    }

    [Fact]
    public async Task Url_source_keeps_the_title_given_by_the_user()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("article", TestDocuments.ArticleHtml);
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("article"), title: "My bookmark");

        await context.ProcessAsync(source.Id, Ct);

        context.Repository.Find(source.Id)!.Title.ShouldBe("My bookmark");
    }

    [Fact]
    public async Task Url_source_without_a_site_name_falls_back_to_the_host()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("plain", "<html><head><title>Plain page</title></head><body><p>Plain page content that is worth indexing.</p></body></html>");
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("plain"), title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Title.ShouldBe("Plain page");
        stored.Metadata[SourceMetadataKeys.SiteName].ShouldBe("127.0.0.1");
    }

    [Fact]
    public async Task Url_pointing_to_a_pdf_is_extracted_as_a_pdf()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.Add("files/report.pdf", "application/pdf", TestDocuments.QuarterlyReportPdf());
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("files/report.pdf"), title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready, stored.Error);
        stored.PageCount.ShouldBe(2);
        stored.Title.ShouldBe("Quarterly Report");
        context.VectorStore.ForSource(source.Id).Select(v => v.Metadata[ChunkMetadataKeys.Page]).Distinct().ShouldBe(["1", "2"]);
    }

    [Fact]
    public async Task Missing_page_fails_immediately_without_retry()
    {
        await using var server = await TestHttpServer.StartAsync();
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("missing"), title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Attempts.ShouldBe(1);
        stored.Error!.ShouldContain("HTTP 404");
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Blocked_address_fails_immediately_when_private_networks_are_not_allowed()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("page", TestDocuments.ArticleHtml);
        await using var context = new KnowledgeTestContext(o => o.AllowPrivateNetworks = false);
        var source = await SeedUrlAsync(context, server.Url("page"), title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Error!.ShouldContain("not allowed");
        server.Hits("page").ShouldBe(0);
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Source_without_text_fails_without_retry()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Blank", "   \n\n\t  ");

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Error.ShouldBe(NoTextMessage);
        context.Queue.Count.ShouldBe(0);
        context.Embeddings.Calls.ShouldBeEmpty();
        context.VectorStore.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Html_page_without_text_fails_with_the_no_text_message()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("empty", "<html><head><title>Empty</title><script>var a = 1;</script></head><body><script>render();</script></body></html>");
        await using var context = new KnowledgeTestContext();
        var source = await SeedUrlAsync(context, server.Url("empty"), title: null);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Error.ShouldBe(NoTextMessage);
    }

    [Fact]
    public async Task Transient_failure_is_retried_after_backoff_and_then_succeeds()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxAttempts = 3);
        context.Embeddings.FailNext(1);
        var source = await context.SeedTextAsync("Retry me", TestDocuments.Prose(2));

        await context.ProcessAsync(source.Id, Ct);

        var afterFirst = context.Repository.Find(source.Id)!;
        afterFirst.Status.ShouldBe(SourceStatus.Pending);
        afterFirst.Attempts.ShouldBe(1);
        afterFirst.Error.ShouldBe("Embedding provider unavailable.");
        context.Queue.Drain().ShouldBe([new QueuedIngestion(source.Id, TimeSpan.FromSeconds(2))]);

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Attempts.ShouldBe(2);
        stored.Error.ShouldBeNull();
        context.VectorStore.ForSource(source.Id).Count.ShouldBe(stored.ChunkCount);
        context.Repository.StatusHistory(source.Id).ShouldBe(
            [SourceStatus.Pending, SourceStatus.Processing, SourceStatus.Pending, SourceStatus.Processing, SourceStatus.Ready]);
    }

    [Fact]
    public async Task A_missing_embedding_model_makes_the_source_wait_without_retrying()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxAttempts = 3);
        context.Embeddings.AlwaysFail = true;
        context.Embeddings.FailureFactory = () => new ModelNotConfiguredException("No configured embedding provider is available for the 'embedding' profile.");
        var source = await context.SeedTextAsync("Needs a model", TestDocuments.Prose(2));

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Waiting);
        stored.Stage.ShouldBeNull();
        stored.Error.ShouldNotBeNull().ShouldContain("'embedding' profile");
        context.Queue.Drain().ShouldBeEmpty("retrying cannot help until a model is configured");
        context.VectorStore.ForSource(source.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Persistent_failure_marks_the_source_failed_after_max_attempts()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxAttempts = 3);
        context.Embeddings.AlwaysFail = true;
        var source = await context.SeedTextAsync("Broken", TestDocuments.Prose(2));

        await context.ProcessAsync(source.Id, Ct);
        context.Queue.Drain().ShouldBe([new QueuedIngestion(source.Id, TimeSpan.FromSeconds(2))]);

        await context.ProcessAsync(source.Id, Ct);
        context.Repository.Find(source.Id)!.Status.ShouldBe(SourceStatus.Pending);
        context.Queue.Drain().ShouldBe([new QueuedIngestion(source.Id, TimeSpan.FromSeconds(4))], "the second retry waits 2^2 = 4 seconds");

        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Failed);
        stored.Attempts.ShouldBe(3);
        stored.Error.ShouldBe("Embedding provider unavailable.");
        context.Queue.Count.ShouldBe(0);
        context.VectorStore.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stored_errors_are_short_single_line_and_free_of_secrets()
    {
        await using var context = new KnowledgeTestContext(o => o.MaxAttempts = 1);
        context.Embeddings.AlwaysFail = true;
        context.Embeddings.FailureFactory = () => new InvalidOperationException(
            "Request to https://api.example.com/v1/embed?api_key=SECRET123 failed: api_key=SECRET123 Authorization: Bearer abc.def.ghi "
            + new string('x', 500) + "\n   at Some.Stack.Trace()");
        var source = await context.SeedTextAsync("Leaky", TestDocuments.Prose(1));

        await context.ProcessAsync(source.Id, Ct);

        var error = context.Repository.Find(source.Id)!.Error!;
        error.ShouldNotContain("SECRET123");
        error.ShouldNotContain("abc.def.ghi");
        error.ShouldNotContain("Stack.Trace");
        error.ShouldNotContain("\n");
        error.Length.ShouldBeLessThanOrEqualTo(300);
        error.ShouldStartWith("Request to https://api.example.com/v1/embed failed");
    }

    [Fact]
    public async Task Reingesting_replaces_the_previous_vectors()
    {
        await using var context = new KnowledgeTestContext(o => o.ChunkSizeTokens = 50);
        var source = await context.SeedTextAsync("Evolving", TestDocuments.Prose(6, topic: "alpha"));
        await context.ProcessAsync(source.Id, Ct);
        var oldIds = context.VectorStore.ForSource(source.Id).Select(v => v.ChunkId).ToHashSet();
        oldIds.Count.ShouldBeGreaterThan(1);

        await context.Repository.SaveContentAsync(source.Id, TestDocuments.Prose(1, topic: "beta"), cancellationToken: Ct);
        (await context.WithSourceServiceAsync(s => s.ReingestAsync(source.Id, Ct))).ShouldNotBeNull();
        context.DrainQueue().ShouldBe([source.Id]);
        await context.ProcessAsync(source.Id, Ct);

        var stored = context.Repository.Find(source.Id)!;
        var vectors = context.VectorStore.ForSource(source.Id);
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.Attempts.ShouldBe(1);
        vectors.Count.ShouldBe(stored.ChunkCount);
        vectors.ShouldAllBe(v => !oldIds.Contains(v.ChunkId) && v.Content.Contains("beta") && !v.Content.Contains("alpha"));
        (await context.VectorStore.CountAsync(cancellationToken: Ct)).ShouldBe(stored.ChunkCount);
    }

    [Fact]
    public async Task Changed_embedding_model_is_recorded_on_reingest()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Model switch", TestDocuments.Prose(2));
        await context.ProcessAsync(source.Id, Ct);
        context.Repository.Find(source.Id)!.EmbeddingModel.ShouldBe("fake:fake-embedding:64");

        context.Embeddings.Model = "better-embedding";
        await context.WithSourceServiceAsync(s => s.ReingestAsync(source.Id, Ct));
        await context.ProcessAsync(source.Id, Ct);

        context.Repository.Find(source.Id)!.EmbeddingModel.ShouldBe("fake:better-embedding:64");
        context.VectorStore.ForSource(source.Id).ShouldAllBe(v => v.EmbeddingModel == "fake:better-embedding:64");
    }

    [Fact]
    public async Task Missing_source_is_ignored()
    {
        await using var context = new KnowledgeTestContext();

        await context.ProcessAsync(Guid.NewGuid(), Ct);

        context.VectorStore.All.ShouldBeEmpty();
        context.Embeddings.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Source_deleted_while_processing_leaves_no_vectors_behind()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Doomed", TestDocuments.Prose(3));
        context.VectorStore.AfterInsert = _ => context.Repository.DeleteAsync(source.Id);

        await context.ProcessAsync(source.Id, Ct);

        context.Repository.Find(source.Id).ShouldBeNull();
        context.VectorStore.ForSource(source.Id).ShouldBeEmpty();
        context.Queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Shutdown_cancellation_returns_the_source_to_pending_without_counting_the_attempt()
    {
        await using var context = new KnowledgeTestContext();
        context.Embeddings.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = await context.SeedTextAsync("Interrupted", TestDocuments.Prose(2));
        using var shutdown = new CancellationTokenSource();

        var processing = context.ProcessAsync(source.Id, shutdown.Token);
        await WaitUntilAsync(() => !context.Embeddings.Calls.IsEmpty);
        await shutdown.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => processing);
        var stored = context.Repository.Find(source.Id)!;
        stored.Status.ShouldBe(SourceStatus.Pending);
        stored.Stage.ShouldBeNull();
        stored.Attempts.ShouldBe(0);
        context.Queue.Count.ShouldBe(0);
    }

    private static async Task<Source> SeedFileAsync(
        KnowledgeTestContext context,
        SourceType type,
        string fileName,
        string contentType,
        byte[] bytes,
        bool autoTitle,
        string? title = null)
    {
        var source = new Source
        {
            Type = type,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = bytes.Length,
            Title = title ?? SourceDefaults.TitleForFile(fileName),
        };
        if (autoTitle)
        {
            source.Metadata[SourceMetadataKeys.AutoTitle] = "true";
        }

        return await context.SeedAsync(source, new SourceContent { SourceId = source.Id, RawBytes = bytes });
    }

    private static Task<Source> SeedUrlAsync(KnowledgeTestContext context, Uri uri, string? title)
    {
        var source = new Source { Type = SourceType.Url, Url = uri.AbsoluteUri, Title = title ?? SourceDefaults.TitleForUrl(uri) };
        if (title is null)
        {
            source.Metadata[SourceMetadataKeys.AutoTitle] = "true";
        }

        return context.SeedAsync(source);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
