using System.Text;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed record NormalizedKeywordSearch(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Patterns,
    string EmbeddingModel,
    int TopK,
    Guid[]? SourceIds)
{
    public bool MatchesNothing => Terms.Count == 0;
}

// The ClickHouse store evaluates the same BM25 formulas and patterns in SQL, so changes must be mirrored there.
internal static class KeywordScoring
{
    public const double K1 = 1.2;
    public const double B = 0.75;
    public const int MaxTerms = 16;
    public const int MaxTermLength = 100;

    // Matches the term at a word start, in syntax that both RE2 (ClickHouse) and .NET accept.
    public static string Pattern(string term) => @"(?i)(?:^|[^\p{L}\p{N}])" + Escape(term);

    public static double Idf(long chunkCount, long documentFrequency) =>
        Math.Log(1 + (chunkCount - documentFrequency + 0.5) / (documentFrequency + 0.5));

    public static double TermScore(double idf, int termFrequency, int length, double averageLength) =>
        termFrequency == 0 || averageLength <= 0
            ? 0
            : idf * termFrequency * (K1 + 1) / (termFrequency + K1 * (1 - B + B * length / averageLength));

    // Counts code points to match ClickHouse's lengthUTF8.
    public static int Length(string content) => content.EnumerateRunes().Count();

    // RE2 and .NET both read escaped punctuation literally, and neither accepts an escaped letter or underscore.
    internal static string Escape(string term)
    {
        var builder = new StringBuilder(term.Length * 2);
        foreach (var character in term)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
