using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;

// All operations share one ClickHouseClient, which is built for concurrent use and pools HTTP connections.
internal sealed partial class ClickHouseVectorStore : IVectorStore, IDisposable
{
    // Query parameters travel in the URL and ClickHouse caps one value at 128 KiB, so larger filters are split.
    internal const int MaxSourceIdsPerQuery = 1000;

    private const string DbSystem = "clickhouse";
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);

    private readonly ClickHouseClient _client;
    private readonly ClickHouseSql _sql;
    private readonly ILogger<ClickHouseVectorStore> _logger;

    public ClickHouseVectorStore(string connectionString, string database, string table, ILogger<ClickHouseVectorStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _sql = new ClickHouseSql(database, table);
        _client = new ClickHouseClient(ClickHouseConnectionSettings.Create(connectionString));
        _logger = logger;
    }

    public string ProviderName => VectorStoreProviders.ClickHouse;

    public string Database => _sql.DatabaseName;

    public string Table => _sql.TableName;

    internal ClickHouseClientSettings Settings => _client.Settings;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("ensure_schema");
        try
        {
            // Columns are read first so that an existing table needs no CREATE privilege.
            var columns = await ReadColumnsAsync(cancellationToken);
            if (columns.Count == 0)
            {
                await _client.ExecuteNonQueryAsync(_sql.CreateDatabase, cancellationToken: cancellationToken);
                await _client.ExecuteNonQueryAsync(_sql.CreateTable, cancellationToken: cancellationToken);
                columns = await ReadColumnsAsync(cancellationToken);
                if (columns.Count == 0)
                {
                    throw new VectorStoreSchemaException(
                        $"ClickHouse table {Database}.{Table} is not visible to the configured user after CREATE TABLE IF NOT EXISTS. " +
                        "Check the user's privileges.");
                }
            }

            if (!columns.ContainsKey("context"))
            {
                await _client.ExecuteNonQueryAsync(_sql.AddContextColumn, cancellationToken: cancellationToken);
                columns = await ReadColumnsAsync(cancellationToken);
            }

            EnsureCompatible(columns);
            Log.SchemaReady(_logger, Database, Table);
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        VectorStoreGuard.ValidateChunks(chunks);
        if (chunks.Count == 0)
        {
            return;
        }

        using var activity = StartOperation("insert");
        activity?.SetTag("vectorstore.rows", chunks.Count);
        var started = Stopwatch.GetTimestamp();
        try
        {
            // A batch of up to 100k rows is sent as a single INSERT, which is atomic for MergeTree.
            var written = await _client.InsertBinaryAsync(
                _sql.QualifiedTable,
                ClickHouseSql.InsertColumns,
                chunks.Select(ToRow),
                new InsertOptions { ColumnTypes = ClickHouseSql.ColumnTypes },
                cancellationToken);
            Log.Inserted(_logger, written, Database, Table, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("delete");
        try
        {
            var existing = await CountCoreAsync(sourceId, cancellationToken);
            if (existing > 0)
            {
                // lightweight_deletes_sync = 2 waits until the delete mask is applied, so later reads never see the rows.
                await _client.ExecuteNonQueryAsync(
                    _sql.DeleteBySource,
                    SourceParameters(sourceId),
                    new QueryOptions { CustomSettings = new Dictionary<string, object> { ["lightweight_deletes_sync"] = 2 } },
                    cancellationToken);
            }

            activity?.SetTag("vectorstore.rows", existing);
            Log.Deleted(_logger, existing, sourceId);
            return existing;
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeSearch(query);
        if (search.MatchesNothing)
        {
            return [];
        }

        using var activity = StartOperation("search");
        activity?.SetTag("vectorstore.top_k", search.TopK);
        activity?.SetTag("vectorstore.embedding_model", search.EmbeddingModel);
        var started = Stopwatch.GetTimestamp();
        try
        {
            IReadOnlyList<VectorSearchHit> hits;
            if (search.SourceIds is { Length: > MaxSourceIdsPerQuery } sourceIds)
            {
                // The top K of a union is the top K of the per-batch top Ks.
                var merged = new List<VectorSearchHit>();
                foreach (var batch in sourceIds.Chunk(MaxSourceIdsPerQuery))
                {
                    merged.AddRange(await QueryNearestAsync(search, batch, cancellationToken));
                }

                hits = merged
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.SourceId)
                    .ThenBy(hit => hit.ChunkIndex)
                    .Take(search.TopK)
                    .ToArray();
            }
            else
            {
                hits = await QueryNearestAsync(search, search.SourceIds, cancellationToken);
            }

            activity?.SetTag("vectorstore.hits", hits.Count);
            Log.Searched(_logger, search.EmbeddingModel, search.TopK, hits.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return hits;
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeKeywordSearch(query);
        if (search.MatchesNothing)
        {
            return [];
        }

        using var activity = StartOperation("keyword_search");
        activity?.SetTag("vectorstore.top_k", search.TopK);
        activity?.SetTag("vectorstore.embedding_model", search.EmbeddingModel);
        activity?.SetTag("vectorstore.terms", search.Terms.Count);
        var started = Stopwatch.GetTimestamp();
        try
        {
            IReadOnlyList<VectorSearchHit> hits;
            if (search.SourceIds is { Length: > MaxSourceIdsPerQuery } sourceIds)
            {
                // Statistics are global, so scores from different batches are comparable and can be merged.
                var merged = new List<VectorSearchHit>();
                foreach (var batch in sourceIds.Chunk(MaxSourceIdsPerQuery))
                {
                    merged.AddRange(await QueryKeywordAsync(search, batch, cancellationToken));
                }

                hits = merged
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.SourceId)
                    .ThenBy(hit => hit.ChunkIndex)
                    .Take(search.TopK)
                    .ToArray();
            }
            else
            {
                hits = await QueryKeywordAsync(search, search.SourceIds, cancellationToken);
            }

            activity?.SetTag("vectorstore.hits", hits.Count);
            Log.KeywordSearched(_logger, search.EmbeddingModel, search.Terms.Count, hits.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return hits;
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<TextSearchPage<VectorSearchHit>> FindTextAsync(TextSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeTextSearch(query);
        if (search.MatchesNothing)
        {
            return TextSearchPage<VectorSearchHit>.Empty;
        }

        using var activity = StartOperation("text_search");
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Batches hold disjoint sources, so their totals add up and a page continues from one batch into the next.
            var items = new List<VectorSearchHit>(Math.Min(search.Take, 256));
            long totalChunks = 0, totalSources = 0;
            long skip = search.Skip;
            foreach (var batch in TextSearchBatches(search.SourceIds))
            {
                var (chunks, sources) = await QueryTextTotalsAsync(search, batch, cancellationToken);
                totalChunks += chunks;
                totalSources += sources;
                if (items.Count < search.Take && skip < chunks)
                {
                    items.AddRange(await QueryTextChunksAsync(search, batch, (int)skip, search.Take - items.Count, cancellationToken));
                }

                skip = Math.Max(0, skip - chunks);
            }

            activity?.SetTag("vectorstore.hits", items.Count);
            Log.TextSearched(_logger, items.Count, totalChunks, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new TextSearchPage<VectorSearchHit>(items, totalChunks, totalSources);
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<TextSearchPage<SourceTextMatches>> CountTextBySourceAsync(TextSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeTextSearch(query);
        if (search.MatchesNothing)
        {
            return TextSearchPage<SourceTextMatches>.Empty;
        }

        using var activity = StartOperation("text_search_sources");
        var started = Stopwatch.GetTimestamp();
        try
        {
            TextSearchPage<SourceTextMatches> page;
            if (search.SourceIds is { Length: > MaxSourceIdsPerQuery } sourceIds)
            {
                // A batch matches at most as many sources as it holds ids, so every count is fetched and paged here.
                var merged = new List<SourceTextMatches>();
                foreach (var batch in TextSearchBatches(sourceIds))
                {
                    merged.AddRange(await QueryTextSourcesAsync(search, batch, 0, batch!.Length, cancellationToken));
                }

                var items = merged
                    .OrderByDescending(source => source.Chunks)
                    .ThenBy(source => source.SourceId)
                    .Skip(search.Skip)
                    .Take(search.Take)
                    .ToArray();
                page = new TextSearchPage<SourceTextMatches>(items, merged.Sum(source => source.Chunks), merged.Count);
            }
            else
            {
                var (chunks, sources) = await QueryTextTotalsAsync(search, search.SourceIds, cancellationToken);
                var items = await QueryTextSourcesAsync(search, search.SourceIds, search.Skip, search.Take, cancellationToken);
                page = new TextSearchPage<SourceTextMatches>(items, chunks, sources);
            }

            activity?.SetTag("vectorstore.rows", page.Items.Count);
            Log.TextSearched(_logger, page.Items.Count, page.TotalChunks, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return page;
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var (offset, limit) = VectorStoreGuard.NormalizePage(skip, take);
        if (limit == 0)
        {
            return [];
        }

        using var activity = StartOperation("get_chunks");
        try
        {
            var parameters = SourceParameters(sourceId);
            parameters.AddParameter("skip", (uint)offset);
            parameters.AddParameter("take", (uint)limit);

            var hits = new List<VectorSearchHit>(Math.Min(limit, 256));
            using var reader = await _client.ExecuteReaderAsync(_sql.GetChunks, parameters, cancellationToken: cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(ReadHit(reader, score: 0d));
            }

            activity?.SetTag("vectorstore.rows", hits.Count);
            return hits;
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    public async Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        using var activity = StartOperation("count");
        try
        {
            return await CountCoreAsync(sourceId, cancellationToken);
        }
        catch (Exception ex) when (RecordFailure(activity, ex))
        {
            throw;
        }
    }

    // SELECT 1 rather than the HTTP /ping endpoint, because it also verifies the credentials.
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PingTimeout);
            var result = await _client.ExecuteScalarAsync(ClickHouseSql.Ping, cancellationToken: timeout.Token);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }
        catch (Exception ex)
        {
            Log.PingFailed(_logger, ex);
            return false;
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<List<VectorSearchHit>> QueryNearestAsync(NormalizedSearch search, Guid[]? sourceIds, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("q", ToArray(search.Vector));
        parameters.AddParameter("model", search.EmbeddingModel);
        parameters.AddParameter("dims", (ushort)search.Vector.Length);
        parameters.AddParameter("k", (uint)search.TopK);
        if (sourceIds is not null)
        {
            parameters.AddParameter("ids", sourceIds);
        }

        if (search.MinScore is double minScore)
        {
            parameters.AddParameter("maxDistance", 1d - minScore);
        }

        var sql = _sql.Search(filterSources: sourceIds is not null, filterDistance: search.MinScore is not null);
        var hits = new List<VectorSearchHit>(search.TopK);
        using var reader = await _client.ExecuteReaderAsync(sql, parameters, cancellationToken: cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var distance = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture);
            var score = VectorStoreGuard.NormalizeScore(1d - distance);

            // The server compares Float32 distances, so the threshold is re-checked here to hold exactly.
            if (score < search.MinScore)
            {
                continue;
            }

            hits.Add(ReadHit(reader, score));
        }

        return hits;
    }

    private async Task<List<VectorSearchHit>> QueryKeywordAsync(NormalizedKeywordSearch search, Guid[]? sourceIds, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("model", search.EmbeddingModel);
        parameters.AddParameter("k", (uint)search.TopK);
        parameters.AddParameter("terms", search.Terms.ToArray());
        for (var i = 0; i < search.Terms.Count; i++)
        {
            parameters.AddParameter(string.Create(CultureInfo.InvariantCulture, $"t{i}"), search.Terms[i]);
            parameters.AddParameter(string.Create(CultureInfo.InvariantCulture, $"p{i}"), search.Patterns[i]);
        }

        if (sourceIds is not null)
        {
            parameters.AddParameter("ids", sourceIds);
        }

        var hits = new List<VectorSearchHit>(search.TopK);
        using var reader = await _client.ExecuteReaderAsync(
            _sql.KeywordSearch(search.Terms.Count, filterSources: sourceIds is not null),
            parameters,
            cancellationToken: cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(ReadHit(reader, Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture)));
        }

        return hits;
    }

    // One unfiltered query, or the sorted source filter in batches, so paging across batches always sees the same order.
    private static IEnumerable<Guid[]?> TextSearchBatches(Guid[]? sourceIds)
    {
        if (sourceIds is null)
        {
            yield return null;
            yield break;
        }

        foreach (var batch in sourceIds.Order().Chunk(MaxSourceIdsPerQuery))
        {
            yield return batch;
        }
    }

    private async Task<(long Chunks, long Sources)> QueryTextTotalsAsync(NormalizedTextSearch search, Guid[]? sourceIds, CancellationToken cancellationToken)
    {
        using var reader = await _client.ExecuteReaderAsync(
            _sql.TextSearch(TextSearchStatement.Totals, search.MatchCase, filterSources: sourceIds is not null),
            TextSearchParameters(search, sourceIds),
            cancellationToken: cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture))
            : (0, 0);
    }

    private async Task<List<VectorSearchHit>> QueryTextChunksAsync(
        NormalizedTextSearch search,
        Guid[]? sourceIds,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var parameters = TextSearchParameters(search, sourceIds);
        parameters.AddParameter("skip", (uint)skip);
        parameters.AddParameter("take", (uint)take);

        var hits = new List<VectorSearchHit>(Math.Min(take, 256));
        using var reader = await _client.ExecuteReaderAsync(
            _sql.TextSearch(TextSearchStatement.Chunks, search.MatchCase, filterSources: sourceIds is not null),
            parameters,
            cancellationToken: cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(ReadHit(reader, score: 0d));
        }

        return hits;
    }

    private async Task<List<SourceTextMatches>> QueryTextSourcesAsync(
        NormalizedTextSearch search,
        Guid[]? sourceIds,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var parameters = TextSearchParameters(search, sourceIds);
        parameters.AddParameter("skip", (uint)skip);
        parameters.AddParameter("take", (uint)take);

        var sources = new List<SourceTextMatches>(Math.Min(take, 256));
        using var reader = await _client.ExecuteReaderAsync(
            _sql.TextSearch(TextSearchStatement.Sources, search.MatchCase, filterSources: sourceIds is not null),
            parameters,
            cancellationToken: cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sources.Add(new SourceTextMatches(reader.GetGuid(0), Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)));
        }

        return sources;
    }

    private static ClickHouseParameterCollection TextSearchParameters(NormalizedTextSearch search, Guid[]? sourceIds)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("needle", search.Needle);
        parameters.AddParameter("pattern", search.Pattern);
        if (sourceIds is not null)
        {
            parameters.AddParameter("ids", sourceIds);
        }

        return parameters;
    }

    private async Task<long> CountCoreAsync(Guid? sourceId, CancellationToken cancellationToken)
    {
        var result = sourceId is Guid id
            ? await _client.ExecuteScalarAsync(_sql.CountBySource, SourceParameters(id), cancellationToken: cancellationToken)
            : await _client.ExecuteScalarAsync(_sql.CountAll, cancellationToken: cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<string, string>> ReadColumnsAsync(CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("database", Database);
        parameters.AddParameter("table", Table);

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = await _client.ExecuteReaderAsync(ClickHouseSql.SelectColumnsOfTable, parameters, cancellationToken: cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    // CREATE TABLE IF NOT EXISTS leaves an existing table untouched, so its columns are verified here.
    private void EnsureCompatible(Dictionary<string, string> actual)
    {
        var problems = new List<string>();
        foreach (var (column, expectedType) in ClickHouseSql.ColumnTypes)
        {
            if (!actual.TryGetValue(column, out var actualType))
            {
                problems.Add($"column '{column}' is missing (expected {expectedType})");
            }
            else if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
            {
                problems.Add($"column '{column}' is {actualType} (expected {expectedType})");
            }
        }

        if (problems.Count > 0)
        {
            throw new VectorStoreSchemaException(
                $"ClickHouse table {Database}.{Table} exists but does not match the vector store schema: {string.Join("; ", problems)}. " +
                "Drop or migrate the table, or configure another VectorStore:Table.");
        }
    }

    private static ClickHouseParameterCollection SourceParameters(Guid sourceId)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("sourceId", sourceId);
        return parameters;
    }

    private static object[] ToRow(ChunkRecord chunk) =>
    [
        chunk.ChunkId,
        chunk.SourceId,
        (uint)chunk.ChunkIndex,
        chunk.Content,
        chunk.Context,
        ChunkMetadataSerializer.Serialize(chunk.Metadata),
        chunk.EmbeddingModel,
        (ushort)chunk.Embedding.Length,
        ToArray(chunk.Embedding),
    ];

    private static VectorSearchHit ReadHit(ClickHouseDataReader reader, double score) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
        reader.GetString(3),
        score,
        ChunkMetadataSerializer.Deserialize(reader.GetString(4)));

    // The driver needs arrays, so the backing array is reused when the memory spans all of it.
    private static float[] ToArray(ReadOnlyMemory<float> memory) =>
        MemoryMarshal.TryGetArray(memory, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : memory.ToArray();

    private Activity? StartOperation(string operation) =>
        VectorStoreTelemetry.StartOperation(operation, DbSystem, Database, Table);

    // Exception filter that records the failure on the span and returns false so the exception is not caught.
    private static bool RecordFailure(Activity? activity, Exception exception)
    {
        activity.RecordException(exception);
        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "ClickHouse vector store schema is ready ({Database}.{Table})")]
        public static partial void SchemaReady(ILogger logger, string database, string table);

        [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message = "Inserted {Rows} chunks into {Database}.{Table} in {ElapsedMs:0.0} ms")]
        public static partial void Inserted(ILogger logger, long rows, string database, string table, double elapsedMs);

        [LoggerMessage(EventId = 1103, Level = LogLevel.Debug, Message = "Deleted {Rows} chunks of source {SourceId}")]
        public static partial void Deleted(ILogger logger, long rows, Guid sourceId);

        [LoggerMessage(EventId = 1104, Level = LogLevel.Debug, Message = "Vector search ({EmbeddingModel}, top {TopK}) returned {Hits} hits in {ElapsedMs:0.0} ms")]
        public static partial void Searched(ILogger logger, string embeddingModel, int topK, int hits, double elapsedMs);

        [LoggerMessage(EventId = 1106, Level = LogLevel.Debug, Message = "Keyword search ({EmbeddingModel}, {Terms} terms) returned {Hits} hits in {ElapsedMs:0.0} ms")]
        public static partial void KeywordSearched(ILogger logger, string embeddingModel, int terms, int hits, double elapsedMs);

        [LoggerMessage(EventId = 1105, Level = LogLevel.Debug, Message = "ClickHouse vector store ping failed")]
        public static partial void PingFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message = "Text search returned {Rows} rows for {Chunks} matching chunks in {ElapsedMs:0.0} ms")]
        public static partial void TextSearched(ILogger logger, int rows, long chunks, double elapsedMs);
    }
}
