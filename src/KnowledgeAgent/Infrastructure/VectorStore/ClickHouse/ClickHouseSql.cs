using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;

// Only validated identifiers are interpolated. Every user-supplied value is a {name:Type} query parameter.
internal sealed class ClickHouseSql
{
    // Insert rows follow this column order, and created_at comes from its default.
    public static IReadOnlyList<string> InsertColumns { get; } =
        ["chunk_id", "source_id", "chunk_index", "content", "context", "metadata", "embedding_model", "dimensions", "embedding"];

    private const string SearchText = "if(empty(context), content, concat(context, ' ', content))";

    // Written as system.columns reports them. Also sent with inserts so the driver skips its schema probe.
    public static IReadOnlyDictionary<string, string> ColumnTypes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["chunk_id"] = "UUID",
        ["source_id"] = "UUID",
        ["chunk_index"] = "UInt32",
        ["content"] = "String",
        ["context"] = "String",
        ["metadata"] = "String",
        ["embedding_model"] = "LowCardinality(String)",
        ["dimensions"] = "UInt16",
        ["embedding"] = "Array(Float32)",
    };

    public const string SelectColumnsOfTable =
        "SELECT name, type FROM system.columns WHERE database = {database:String} AND table = {table:String}";

    public const string Ping = "SELECT 1";

    private readonly string[] _search;
    private readonly ConcurrentDictionary<(int Terms, bool FilterSources), string> _keywordSearch = new();
    private readonly ConcurrentDictionary<(TextSearchStatement Statement, bool MatchCase, bool FilterSources), string> _textSearch = new();

    public ClickHouseSql(string database, string table)
    {
        DatabaseName = ClickHouseIdentifier.Validate(database, "VectorStore:Database");
        TableName = ClickHouseIdentifier.Validate(table, "VectorStore:Table");
        QualifiedTable = $"{ClickHouseIdentifier.Quote(DatabaseName)}.{ClickHouseIdentifier.Quote(TableName)}";

        CreateDatabase = $"CREATE DATABASE IF NOT EXISTS {ClickHouseIdentifier.Quote(DatabaseName)}";
        CreateTable = $$"""
            CREATE TABLE IF NOT EXISTS {{QualifiedTable}}
            (
                chunk_id UUID,
                source_id UUID,
                chunk_index UInt32,
                content String,
                context String DEFAULT '',
                metadata String,
                embedding_model LowCardinality(String),
                dimensions UInt16,
                embedding Array(Float32),
                created_at DateTime64(3, 'UTC') DEFAULT now64(3)
            )
            ENGINE = MergeTree
            ORDER BY (embedding_model, source_id, chunk_index)
            """;
        AddContextColumn = $"ALTER TABLE {QualifiedTable} ADD COLUMN IF NOT EXISTS context String DEFAULT '' AFTER content";
        CountAll = $"SELECT count() FROM {QualifiedTable}";
        CountBySource = $$"""SELECT count() FROM {{QualifiedTable}} WHERE source_id = {sourceId:UUID}""";
        DeleteBySource = $$"""DELETE FROM {{QualifiedTable}} WHERE source_id = {sourceId:UUID}""";
        GetChunks = $$"""
            SELECT chunk_id, source_id, chunk_index, content, metadata
            FROM {{QualifiedTable}}
            WHERE source_id = {sourceId:UUID}
            ORDER BY chunk_index ASC, embedding_model ASC
            LIMIT {take:UInt32} OFFSET {skip:UInt32}
            """;

        _search =
        [
            BuildSearch(filterSources: false, filterDistance: false),
            BuildSearch(filterSources: true, filterDistance: false),
            BuildSearch(filterSources: false, filterDistance: true),
            BuildSearch(filterSources: true, filterDistance: true),
        ];
    }

    public string DatabaseName { get; }
    public string TableName { get; }
    public string QualifiedTable { get; }
    public string CreateDatabase { get; }
    public string CreateTable { get; }

    // Tables created before contextual retrieval get the column; existing chunks keep an empty context.
    public string AddContextColumn { get; }
    public string CountAll { get; }
    public string CountBySource { get; }
    public string DeleteBySource { get; }
    public string GetChunks { get; }

    public string Search(bool filterSources, bool filterDistance) =>
        _search[(filterSources ? 1 : 0) + (filterDistance ? 2 : 0)];

    public string KeywordSearch(int termCount, bool filterSources)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(termCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(termCount, KeywordScoring.MaxTerms);
        return _keywordSearch.GetOrAdd((termCount, filterSources), key => BuildKeywordSearch(key.Terms, key.FilterSources));
    }

    public string TextSearch(TextSearchStatement statement, bool matchCase, bool filterSources) =>
        _textSearch.GetOrAdd((statement, matchCase, filterSources), key => BuildTextSearch(key.Statement, key.MatchCase, key.FilterSources));

    private string BuildTextSearch(TextSearchStatement statement, bool matchCase, bool filterSources)
    {
        // position is a cheap prefilter on a word every match contains, before the regular expression runs.
        // LIMIT 1 BY and uniqExact count a chunk stored for several embedding models once.
        var position = matchCase ? "position" : "positionCaseInsensitiveUTF8";
        var sourceFilter = filterSources ? "\nPREWHERE source_id IN {ids:Array(UUID)}" : string.Empty;
        var from = $$"""
            FROM {{QualifiedTable}}{{sourceFilter}}
            WHERE {{position}}(content, {needle:String}) > 0
              AND match(content, {pattern:String})
            """;

        return statement switch
        {
            TextSearchStatement.Totals => $"SELECT uniqExact(source_id, chunk_index), uniqExact(source_id)\n{from}",
            TextSearchStatement.Chunks => $$"""
                SELECT chunk_id, source_id, chunk_index, content, metadata
                {{from}}
                ORDER BY source_id ASC, chunk_index ASC, embedding_model ASC
                LIMIT 1 BY source_id, chunk_index
                LIMIT {take:UInt32} OFFSET {skip:UInt32}
                """,
            TextSearchStatement.Sources => $$"""
                SELECT source_id, uniqExact(chunk_index) AS chunks
                {{from}}
                GROUP BY source_id
                ORDER BY chunks DESC, source_id ASC
                LIMIT {take:UInt32} OFFSET {skip:UInt32}
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(statement), statement, null),
        };
    }

    private string BuildKeywordSearch(int termCount, bool filterSources)
    {
        // match and countMatches need constant patterns, so each term gets its own expression instead of an array lambda.
        // stats is (N, average length, df of term 0, df of term 1, ...) over every chunk of the model.
        // positionCaseInsensitiveUTF8 is a cheap prefilter before the word-start match.
        var documentFrequencies = string.Join(
            ",\n                   ",
            Enumerable.Range(0, termCount).Select(i => string.Create(CultureInfo.InvariantCulture,
                $"countIf(positionCaseInsensitiveUTF8({SearchText}, {{t{i}:String}}) > 0 AND match({SearchText}, {{p{i}:String}}))")));

        var termScores = new StringBuilder();
        for (var i = 0; i < termCount; i++)
        {
            var df = string.Create(CultureInfo.InvariantCulture, $"stats.{i + 3}");
            var tf = string.Create(CultureInfo.InvariantCulture, $"countMatches({SearchText}, {{p{i}:String}})");
            termScores.Append(i == 0 ? "         " : "\n       + ")
                .Append(CultureInfo.InvariantCulture, $"if({df} = 0, 0., log(1 + (stats.1 - {df} + 0.5) / ({df} + 0.5)) * {tf} * {KeywordScoring.K1 + 1:0.0#}")
                .Append(CultureInfo.InvariantCulture, $" / ({tf} + {KeywordScoring.K1:0.0#} * ({1 - KeywordScoring.B:0.0#} + {KeywordScoring.B:0.0#} * lengthUTF8({SearchText}) / stats.2)))");
        }

        var sourceFilter = filterSources ? "\n  AND source_id IN {ids:Array(UUID)}" : string.Empty;
        return $$"""
            WITH (
                SELECT tuple(count(), avg(lengthUTF8({{SearchText}})),
                       {{documentFrequencies}})
                FROM {{QualifiedTable}}
                PREWHERE embedding_model = {model:String}
            ) AS stats
            SELECT chunk_id, source_id, chunk_index, content, metadata,
                   (
            {{termScores}}
                   ) AS score
            FROM {{QualifiedTable}}
            PREWHERE embedding_model = {model:String}{{sourceFilter}}
            WHERE multiSearchAnyCaseInsensitiveUTF8({{SearchText}}, {terms:Array(String)}) AND score > 0
            ORDER BY score DESC, source_id ASC, chunk_index ASC
            LIMIT {k:UInt32}
            """;
    }

    private string BuildSearch(bool filterSources, bool filterDistance)
    {
        // PREWHERE runs first, so cosineDistance never sees embeddings of another size (SIZES_OF_ARRAYS_DONT_MATCH).
        // Zero-length vectors give NaN, which counts as distance 1 like in the in-memory store.
        var sourceFilter = filterSources ? "\n  AND source_id IN {ids:Array(UUID)}" : string.Empty;
        var distanceFilter = filterDistance ? "\nWHERE distance <= {maxDistance:Float64}" : string.Empty;
        return $$"""
            SELECT chunk_id, source_id, chunk_index, content, metadata,
                   ifNotFinite(cosineDistance(embedding, {q:Array(Float32)}), 1.0) AS distance
            FROM {{QualifiedTable}}
            PREWHERE embedding_model = {model:String}
              AND dimensions = {dims:UInt16}{{sourceFilter}}{{distanceFilter}}
            ORDER BY distance ASC, source_id ASC, chunk_index ASC
            LIMIT {k:UInt32}
            """;
    }
}

internal enum TextSearchStatement
{
    Totals,
    Chunks,
    Sources,
}
