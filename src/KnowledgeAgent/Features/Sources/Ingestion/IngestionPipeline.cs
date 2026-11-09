using System.Diagnostics;
using System.Globalization;
using KnowledgeAgent.Features.Sources.Chunking;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources.Ingestion;

internal sealed partial class IngestionPipeline(
    ISourceRepository repository,
    IVectorStore vectorStore,
    IEmbeddingService embeddingService,
    UrlFetcher fetcher,
    ContentExtractor extractor,
    TextChunker chunker,
    IChunkContextualizer contextualizer,
    IIngestionQueue queue,
    IOptionsMonitor<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestionPipeline> logger) : IIngestionPipeline
{
    public const string NoTextMessage = "No text could be extracted from the source.";

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan StatusResetTimeout = TimeSpan.FromSeconds(5);

    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Clamp(attempts, 0, 30)), MaxRetryDelay.TotalSeconds));

    public async Task ProcessAsync(Guid sourceId, string? jobRunId, CancellationToken cancellationToken = default)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ingest source");
        activity?.SetTag(IngestionTelemetry.Tags.SourceId, sourceId);

        var source = await repository.GetAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            LogSourceMissing(logger, sourceId);
            activity?.SetTag(IngestionTelemetry.Tags.Outcome, "missing");
            return;
        }

        if (source.Type == SourceType.Website)
        {
            // A website has no content of its own; its crawl ingests the pages.
            await queue.EnqueueCrawlAsync(source.Id, cancellationToken).ConfigureAwait(false);
            activity?.SetTag(IngestionTelemetry.Tags.Outcome, "crawl");
            return;
        }

        var settings = options.CurrentValue;
        var typeName = SourceDefaults.TypeName(source.Type);
        var started = timeProvider.GetTimestamp();
        var storedChunkCount = source.ChunkCount;

        source.Status = SourceStatus.Processing;
        source.Stage = source.Type == SourceType.Url ? SourceStage.Fetching : SourceStage.Extracting;
        source.EmbeddedChunks = 0;
        source.JobRunId = jobRunId;
        source.Attempts++;
        source.UpdatedAt = timeProvider.GetUtcNow();
        await repository.UpdateAsync(source, cancellationToken).ConfigureAwait(false);

        activity?.SetTag(IngestionTelemetry.Tags.SourceType, typeName);
        activity?.SetTag(IngestionTelemetry.Tags.Attempt, source.Attempts);
        LogStarted(logger, source.Id, typeName, source.Attempts);

        string outcome;
        try
        {
            await IngestAsync(source, settings, activity, cancellationToken).ConfigureAwait(false);
            outcome = "ready";
            LogReady(logger, source.Id, source.ChunkCount, source.EmbeddingModel, timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AbandonAttempt(source, storedChunkCount);
            await ResetAfterCancellationAsync(source).ConfigureAwait(false);
            activity?.SetTag(IngestionTelemetry.Tags.Outcome, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            AbandonAttempt(source, storedChunkCount);
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ErrorSanitizer.ToUserMessage(ex));
            outcome = await HandleFailureAsync(source, ex, settings).ConfigureAwait(false);
        }

        activity?.SetTag(IngestionTelemetry.Tags.Outcome, outcome);
        var tags = new TagList
        {
            { IngestionTelemetry.Tags.Outcome, outcome },
            { IngestionTelemetry.Tags.SourceType, typeName },
        };
        IngestionTelemetry.SourcesProcessed.Add(1, tags);
        IngestionTelemetry.Duration.Record(timeProvider.GetElapsedTime(started).TotalSeconds, tags);
    }

    // For pages the crawler already fetched and extracted. Retries are the caller's: a transient failure leaves the page
    // Pending and is rethrown instead of being queued again.
    internal async Task<SourceStatus> IngestFetchedAsync(Source source, ExtractedDocument document, string? jobRunId, CancellationToken cancellationToken)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ingest page");
        activity?.SetTag(IngestionTelemetry.Tags.SourceId, source.Id);
        var settings = options.CurrentValue;
        var storedChunkCount = source.ChunkCount;
        source.Attempts = source.Status == SourceStatus.Pending && source.Error is not null ? source.Attempts + 1 : 1;
        source.Status = SourceStatus.Processing;
        source.Stage = SourceStage.Extracting;
        source.EmbeddedChunks = 0;
        source.JobRunId = jobRunId;
        await repository.UpdateAsync(source, cancellationToken).ConfigureAwait(false);

        try
        {
            await IngestAsync(source, settings, activity, cancellationToken, document).ConfigureAwait(false);
            IngestionTelemetry.SourcesProcessed.Add(1, new TagList { { IngestionTelemetry.Tags.Outcome, "ready" }, { IngestionTelemetry.Tags.SourceType, "url" } });
            return SourceStatus.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AbandonAttempt(source, storedChunkCount);
            await ResetAfterCancellationAsync(source).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            AbandonAttempt(source, storedChunkCount);
            activity?.AddException(ex);
            var message = ErrorSanitizer.ToUserMessage(ex);
            if (ex is ModelNotConfiguredException)
            {
                await SetStatusAsync(source, SourceStatus.Waiting, message).ConfigureAwait(false);
                return SourceStatus.Waiting;
            }

            if (ex is IngestionException { IsPermanent: true })
            {
                await SetStatusAsync(source, SourceStatus.Failed, message).ConfigureAwait(false);
                return SourceStatus.Failed;
            }

            await SetStatusAsync(source, SourceStatus.Pending, message).ConfigureAwait(false);
            throw;
        }
    }

    private async Task IngestAsync(
        Source source,
        IngestionOptions settings,
        Activity? activity,
        CancellationToken cancellationToken,
        ExtractedDocument? fetchedDocument = null)
    {
        var document = fetchedDocument ?? await ObtainDocumentAsync(source, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(IngestionTelemetry.Tags.ContentKind, document.Kind.ToString());
        if (string.IsNullOrWhiteSpace(document.Text))
        {
            throw new IngestionException(NoTextMessage, isPermanent: true);
        }

        if (SourceDefaults.HasAutomaticTitle(source) && SourceDefaults.CleanTitle(document.Title) is { } documentTitle)
        {
            source.Title = documentTitle;
        }

        await repository.SaveContentAsync(source.Id, document.Text, rawBytes: null, cancellationToken).ConfigureAwait(false);
        source.ContentHash = TextUtilities.Sha256Hex(document.Text);
        await SaveStageAsync(source, SourceStage.Chunking, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TextChunk> chunks;
        using (IngestionTelemetry.ActivitySource.StartActivity("chunk"))
        {
            chunks = chunker.Chunk(document.Text, new ChunkingSettings(settings.ChunkSizeTokens, settings.ChunkOverlapTokens, document.HasPages));
        }

        if (chunks.Count == 0)
        {
            throw new IngestionException(NoTextMessage, isPermanent: true);
        }

        source.ChunkCount = chunks.Count;
        FlagSuspicious(source, chunks);
        await SaveStageAsync(source, SourceStage.Contextualizing, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> contexts;
        using (IngestionTelemetry.ActivitySource.StartActivity("contextualize"))
        {
            contexts = await contextualizer.ContextualizeAsync(source, document.Text, chunks, cancellationToken).ConfigureAwait(false);
        }

        await SaveStageAsync(source, SourceStage.Embedding, cancellationToken).ConfigureAwait(false);
        var inputs = chunks.Select((chunk, index) => contexts[index].Length == 0 ? chunk.Content : $"{contexts[index]}\n\n{chunk.Content}").ToList();
        var (modelKey, vectors) = await EmbedAsync(source, inputs, settings.EmbeddingBatchSize, cancellationToken).ConfigureAwait(false);
        var records = CreateRecords(source, chunks, contexts, modelKey, vectors);

        source.EmbeddedChunks = chunks.Count;
        await SaveStageAsync(source, SourceStage.Storing, cancellationToken).ConfigureAwait(false);
        using (var storeActivity = IngestionTelemetry.ActivitySource.StartActivity("store vectors"))
        {
            storeActivity?.SetTag(IngestionTelemetry.Tags.ChunkCount, records.Count);
            await vectorStore.DeleteBySourceAsync(source.Id, cancellationToken).ConfigureAwait(false);
            await vectorStore.InsertAsync(records, cancellationToken).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow();
        source.Status = SourceStatus.Ready;
        source.Stage = null;
        source.PageCount = document.PageCount;
        source.TokenEstimate = TextChunker.EstimateTokens(document.Text);
        source.EmbeddingModel = modelKey;
        source.ProcessedAt = now;
        source.UpdatedAt = now;
        source.Error = null;
        await repository.UpdateAsync(source, cancellationToken).ConfigureAwait(false);

        activity?.SetTag(IngestionTelemetry.Tags.ChunkCount, chunks.Count);
        activity?.SetTag(IngestionTelemetry.Tags.PageCount, document.PageCount);
        activity?.SetTag(IngestionTelemetry.Tags.EmbeddingModel, modelKey);
        IngestionTelemetry.ChunksStored.Add(chunks.Count);
    }

    // Labels the source for a person to read; on the daily refresh too, so a changed page is caught.
    private void FlagSuspicious(Source source, IReadOnlyList<TextChunk> chunks)
    {
        var suspicious = InjectionScan.CountSuspicious(chunks.Select(chunk => chunk.Content));
        var metadata = new Dictionary<string, string>(source.Metadata);
        if (suspicious > 0)
        {
            metadata[SourceMetadataKeys.SuspiciousChunks] = suspicious.ToString(CultureInfo.InvariantCulture);
            IngestionTelemetry.SuspiciousChunks.Add(suspicious);
            LogSuspicious(logger, source.Id, suspicious);
        }
        else
        {
            metadata.Remove(SourceMetadataKeys.SuspiciousChunks);
        }

        source.Metadata = metadata;
    }

    private async Task<ExtractedDocument> ObtainDocumentAsync(Source source, CancellationToken cancellationToken)
    {
        if (source.Type == SourceType.Url)
        {
            return await FetchAndExtractAsync(source, cancellationToken).ConfigureAwait(false);
        }

        var kind = source.Type switch
        {
            SourceType.Pdf => ContentKind.Pdf,
            SourceType.Markdown => ContentKind.Markdown,
            _ => ContentKind.PlainText,
        };

        var content = await repository.GetContentAsync(source.Id, cancellationToken).ConfigureAwait(false);
        if (content?.RawBytes is { Length: > 0 } rawBytes)
        {
            using var extractActivity = IngestionTelemetry.ActivitySource.StartActivity("extract");
            return extractor.Extract(rawBytes, new ContentDescriptor(source.ContentType, FileName: source.FileName, KindHint: kind), cancellationToken);
        }

        if (content?.ExtractedText is { } text)
        {
            return ContentExtractor.FromText(text, kind);
        }

        throw new IngestionException("The source has no stored content to ingest.", isPermanent: true);
    }

    private async Task<ExtractedDocument> FetchAndExtractAsync(Source source, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
        {
            throw new IngestionException("The source URL is not a valid absolute URL.", isPermanent: true);
        }

        FetchedContent fetched;
        using (var fetchActivity = IngestionTelemetry.ActivitySource.StartActivity("fetch"))
        {
            fetchActivity?.SetTag("server.address", uri.IdnHost);
            fetched = await fetcher.FetchAsync(uri, cancellationToken).ConfigureAwait(false);
            fetchActivity?.SetTag("http.response.body.size", fetched.Content.Length);
        }

        await SaveStageAsync(source, SourceStage.Extracting, cancellationToken).ConfigureAwait(false);
        ExtractedDocument document;
        using (IngestionTelemetry.ActivitySource.StartActivity("extract"))
        {
            document = extractor.Extract(
                fetched.Content,
                new ContentDescriptor(fetched.MediaType, fetched.CharSet, fetched.FileName, fetched.FinalUri),
                cancellationToken);
        }

        source.Metadata = new Dictionary<string, string>(source.Metadata)
        {
            [SourceMetadataKeys.FinalUrl] = fetched.FinalUri.AbsoluteUri,
            [SourceMetadataKeys.SiteName] = SourceDefaults.CleanTitle(document.SiteName) ?? fetched.FinalUri.IdnHost,
        };
        source.ContentType = fetched.MediaType ?? source.ContentType;
        source.SizeBytes = fetched.Content.LongLength;
        return document;
    }

    // Each input is a chunk with its context in front, so the vector captures what the chunk is about.
    private async Task<(string ModelKey, List<ReadOnlyMemory<float>> Vectors)> EmbedAsync(
        Source source,
        IReadOnlyList<string> chunks,
        int batchSize,
        CancellationToken cancellationToken)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("embed chunks");
        batchSize = Math.Max(1, batchSize);
        var vectors = new List<ReadOnlyMemory<float>>(chunks.Count);
        string? modelKey = null;
        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = new string[Math.Min(batchSize, chunks.Count - offset)];
            for (var i = 0; i < batch.Length; i++)
            {
                batch[i] = chunks[offset + i];
            }

            var result = await embeddingService.EmbedAsync(batch, EmbeddingPurpose.Document, cancellationToken).ConfigureAwait(false);
            if (result.Vectors.Count != batch.Length)
            {
                throw new InvalidOperationException(
                    $"The embedding service returned {result.Vectors.Count} vectors for {batch.Length} inputs.");
            }

            if (modelKey is null)
            {
                modelKey = result.ModelKey;
            }
            else if (!string.Equals(modelKey, result.ModelKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The embedding model changed while the source was being embedded.");
            }

            vectors.AddRange(result.Vectors);

            // The last batch is saved together with the switch to storing.
            if (vectors.Count < chunks.Count)
            {
                source.EmbeddedChunks = vectors.Count;
                await SaveStageAsync(source, SourceStage.Embedding, cancellationToken).ConfigureAwait(false);
            }
        }

        activity?.SetTag(IngestionTelemetry.Tags.EmbeddingModel, modelKey);
        activity?.SetTag(IngestionTelemetry.Tags.ChunkCount, chunks.Count);
        return (modelKey ?? string.Empty, vectors);
    }

    private Task SaveStageAsync(Source source, SourceStage stage, CancellationToken cancellationToken)
    {
        source.Stage = stage;
        return repository.UpdateAsync(source, cancellationToken);
    }

    // A failed attempt leaves the stored vectors as they were until it reaches the storing stage, so ChunkCount
    // goes back to counting them.
    private static void AbandonAttempt(Source source, int storedChunkCount)
    {
        if (source.Stage != SourceStage.Storing)
        {
            source.ChunkCount = storedChunkCount;
        }

        source.Stage = null;
    }

    private List<ChunkRecord> CreateRecords(
        Source source,
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<string> contexts,
        string modelKey,
        List<ReadOnlyMemory<float>> vectors)
    {
        var typeName = SourceDefaults.TypeName(source.Type);
        var url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url;
        var now = timeProvider.GetUtcNow();
        var records = new List<ChunkRecord>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ChunkMetadataKeys.Title] = source.Title,
                [ChunkMetadataKeys.SourceType] = typeName,
            };
            if (url is not null)
            {
                metadata[ChunkMetadataKeys.Url] = url;
            }

            if (chunk.Page is { } page)
            {
                metadata[ChunkMetadataKeys.Page] = page.ToString(CultureInfo.InvariantCulture);
            }

            records.Add(new ChunkRecord(Guid.CreateVersion7(now), source.Id, chunk.Index, chunk.Content, modelKey, vectors[i], metadata, contexts[i]));
        }

        return records;
    }

    private async Task<string> HandleFailureAsync(Source source, Exception exception, IngestionOptions settings)
    {
        if (!await StillExistsAsync(source.Id).ConfigureAwait(false))
        {
            // Deleted during processing, so drop anything this attempt may have stored.
            await TryDeleteVectorsAsync(source.Id).ConfigureAwait(false);
            LogDeletedWhileProcessing(logger, source.Id);
            return "deleted";
        }

        var message = ErrorSanitizer.ToUserMessage(exception);
        if (exception is ModelNotConfiguredException)
        {
            // The embedding model is the only model ingestion uses. Retrying cannot help; saving one queues the source.
            await SetStatusAsync(source, SourceStatus.Waiting, message).ConfigureAwait(false);
            LogWaiting(logger, source.Id, message);
            return "waiting";
        }

        var permanent = exception is IngestionException { IsPermanent: true };
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        var retry = !permanent && source.Attempts < maxAttempts;

        await SetStatusAsync(source, retry ? SourceStatus.Pending : SourceStatus.Failed, message).ConfigureAwait(false);
        if (retry)
        {
            var delay = RetryDelay(source.Attempts);
            try
            {
                await queue.EnqueueAsync(source.Id, delay, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception enqueueException)
            {
                // The source stays Pending, so the startup recovery queues it again.
                LogRetryNotQueued(logger, enqueueException, source.Id);
            }

            LogRetryScheduled(logger, exception, source.Id, source.Attempts, maxAttempts, delay.TotalSeconds);
            return "retry";
        }

        LogFailed(logger, exception, source.Id, source.Attempts, message);
        return "failed";
    }

    private async Task SetStatusAsync(Source source, SourceStatus status, string error)
    {
        source.Status = status;
        source.Error = error;
        source.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            await repository.UpdateAsync(source, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception updateException)
        {
            LogStatusUpdateFailed(logger, updateException, source.Id);
        }
    }

    private async Task<bool> StillExistsAsync(Guid sourceId)
    {
        try
        {
            return await repository.GetAsync(sourceId, CancellationToken.None).ConfigureAwait(false) is not null;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task TryDeleteVectorsAsync(Guid sourceId)
    {
        try
        {
            await vectorStore.DeleteBySourceAsync(sourceId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogVectorCleanupFailed(logger, ex, sourceId);
        }
    }

    // An attempt interrupted by shutdown does not count. The source is recovered on the next start.
    private async Task ResetAfterCancellationAsync(Source source)
    {
        try
        {
            using var timeout = new CancellationTokenSource(StatusResetTimeout, timeProvider);
            source.Status = SourceStatus.Pending;
            source.Attempts = Math.Max(0, source.Attempts - 1);
            source.UpdatedAt = timeProvider.GetUtcNow();
            await repository.UpdateAsync(source, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStatusUpdateFailed(logger, ex, source.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceId} no longer exists; nothing to ingest")]
    private static partial void LogSourceMissing(ILogger logger, Guid sourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingesting source {SourceId} ({SourceType}), attempt {Attempt}")]
    private static partial void LogStarted(ILogger logger, Guid sourceId, string sourceType, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceId} is ready: {ChunkCount} chunks embedded with {EmbeddingModel} in {ElapsedMs:0} ms")]
    private static partial void LogReady(ILogger logger, Guid sourceId, int chunkCount, string? embeddingModel, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion attempt {Attempt} of {MaxAttempts} for source {SourceId} failed; retrying in {DelaySeconds} s")]
    private static partial void LogRetryScheduled(ILogger logger, Exception exception, Guid sourceId, int attempt, int maxAttempts, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion of source {SourceId} failed after {Attempt} attempt(s): {Error}")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid sourceId, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} waits for an embedding model: {Error}")]
    private static partial void LogWaiting(ILogger logger, Guid sourceId, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not update the status of source {SourceId}")]
    private static partial void LogStatusUpdateFailed(ILogger logger, Exception exception, Guid sourceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue the next ingestion attempt for source {SourceId}")]
    private static partial void LogRetryNotQueued(ILogger logger, Exception exception, Guid sourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceId} was deleted while it was being ingested")]
    private static partial void LogDeletedWhileProcessing(ILogger logger, Guid sourceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} has {Count} chunk(s) that look written to the assistant; it is labelled for review")]
    private static partial void LogSuspicious(ILogger logger, Guid sourceId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the vectors of deleted source {SourceId}")]
    private static partial void LogVectorCleanupFailed(ILogger logger, Exception exception, Guid sourceId);
}
