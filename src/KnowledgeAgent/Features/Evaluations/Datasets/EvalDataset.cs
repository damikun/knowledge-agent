namespace KnowledgeAgent.Features.Evaluations.Datasets;

public sealed class EvalDataset
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Source { get; init; }
    public IReadOnlyList<EvalCorpusDocument> Corpus { get; init; } = [];
    public required IReadOnlyList<EvalItem> Items { get; init; }
    public EvalGateConfig Gate { get; init; } = EvalGateConfig.Default;

    public IReadOnlyDictionary<string, EvalBaseline> Baselines { get; init; } =
        new Dictionary<string, EvalBaseline>(StringComparer.OrdinalIgnoreCase);

    public string? DirectoryPath { get; init; }
    public string? LangfuseDatasetId { get; init; }

    public IReadOnlyList<string> Tags =>
        Items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public EvalDataset With(IReadOnlyList<EvalItem> items, string? langfuseDatasetId = null) => new()
    {
        Name = Name,
        Description = Description,
        Source = Source,
        Corpus = Corpus,
        Items = items,
        Gate = Gate,
        Baselines = Baselines,
        DirectoryPath = DirectoryPath,
        LangfuseDatasetId = langfuseDatasetId ?? LangfuseDatasetId,
    };
}

public sealed record EvalCorpusDocument(string Key, string Title, string FileName, string? Url, byte[] Content);

public sealed record EvalItem(
    string Id,
    string Question,
    bool Answerable,
    IReadOnlyList<string> ExpectedSources,
    IReadOnlyList<string> ExpectedFacts,
    string? ReferenceAnswer,
    IReadOnlyList<string> Tags)
{
    public string? LangfuseItemId { get; init; }
    public string? LangfuseItemVersion { get; init; }

    public IReadOnlyList<EvalTurn> History { get; init; } = [];
}

public sealed record EvalGateConfig(
    IReadOnlyDictionary<string, double> Minimums,
    double MaxRegression,
    IReadOnlyList<string>? RegressionMetrics)
{
    public const double DefaultMaxRegression = 0.05;

    public static EvalGateConfig Default { get; } = new(
        new Dictionary<string, double>(StringComparer.Ordinal) { [EvalMetrics.CitationValidity] = 0.99 },
        DefaultMaxRegression,
        null);
}

public sealed record EvalBaseline(string Signature, IReadOnlyDictionary<string, double> Metrics, int Items, DateTimeOffset? CreatedAt);

public sealed class EvalDatasetException(string dataset, IReadOnlyList<string> problems)
    : Exception($"Dataset '{dataset}' is invalid: {string.Join(" ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
