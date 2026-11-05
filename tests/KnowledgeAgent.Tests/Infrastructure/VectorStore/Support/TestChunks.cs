using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;

internal static class TestChunks
{
    public const string ModelA = "test:model-a:4";
    public const string ModelB = "test:model-b:4";

    public static ChunkRecord Chunk(
        Guid sourceId,
        int chunkIndex,
        float[] embedding,
        string model = ModelA,
        string? content = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Guid.NewGuid(),
            sourceId,
            chunkIndex,
            content ?? $"Chunk {chunkIndex} of source {sourceId:N}",
            model,
            embedding,
            metadata ?? new Dictionary<string, string>());

    public static VectorSearchQuery Query(
        float[] vector,
        int topK = 10,
        string model = ModelA,
        IReadOnlyCollection<Guid>? sourceIds = null,
        double? minScore = null) =>
        new(vector, model, topK, sourceIds, minScore);

    public static float[] RandomUnitVector(Random random, int dimensions)
    {
        var vector = new float[dimensions];
        var sumOfSquares = 0d;
        for (var i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
            sumOfSquares += vector[i] * vector[i];
        }

        var norm = (float)Math.Sqrt(sumOfSquares);
        for (var i = 0; i < dimensions; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    public static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += (double)left[i] * right[i];
            leftNorm += (double)left[i] * left[i];
            rightNorm += (double)right[i] * right[i];
        }

        return dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
