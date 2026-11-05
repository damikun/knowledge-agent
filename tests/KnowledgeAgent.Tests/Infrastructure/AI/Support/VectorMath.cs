namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal static class VectorMath
{
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length.");
        }

        var sum = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return (float)sum;
    }

    public static float Norm(ReadOnlySpan<float> a) => MathF.Sqrt(Dot(a, a));

    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b) => Dot(a, b) / (Norm(a) * Norm(b));
}
