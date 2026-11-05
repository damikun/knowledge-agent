namespace KnowledgeAgent.Features.Search;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public string Mode { get; set; } = SearchModes.Hybrid;
    public int CandidatePool { get; set; } = 50;
    public int RrfK { get; set; } = 60;
    public double VectorWeight { get; set; } = 1;
    public double KeywordWeight { get; set; } = 1;
    public bool Rerank { get; set; } = true;
    public int RerankCandidates { get; set; } = 30;
}

public static class SearchModes
{
    public const string Hybrid = "hybrid";
    public const string Vector = "vector";
    public const string Keyword = "keyword";

    public static readonly IReadOnlyList<string> All = [Hybrid, Vector, Keyword];

    public static string? Normalize(string? mode) =>
        All.FirstOrDefault(candidate => string.Equals(candidate, mode?.Trim(), StringComparison.OrdinalIgnoreCase));
}
