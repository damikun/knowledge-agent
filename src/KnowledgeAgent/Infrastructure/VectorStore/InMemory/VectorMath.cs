namespace KnowledgeAgent.Infrastructure.VectorStore.InMemory;

internal static class VectorMath
{
    public static double SumOfSquares(ReadOnlySpan<float> vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += (double)value * value;
        }

        return sum;
    }

    public static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException($"Vectors have different dimensions ({left.Length} vs {right.Length}).", nameof(right));
        }

        var sum = 0d;
        for (var i = 0; i < left.Length; i++)
        {
            sum += (double)left[i] * right[i];
        }

        return sum;
    }

    // sqrt(s * s) == s in IEEE double arithmetic, so identical vectors score exactly 1.
    public static double CosineSimilarity(ReadOnlySpan<float> left, double leftSumOfSquares, ReadOnlySpan<float> right, double rightSumOfSquares)
    {
        if (leftSumOfSquares <= 0d || rightSumOfSquares <= 0d)
        {
            return 0d;
        }

        return VectorStoreGuard.NormalizeScore(Dot(left, right) / Math.Sqrt(leftSumOfSquares * rightSumOfSquares));
    }
}
