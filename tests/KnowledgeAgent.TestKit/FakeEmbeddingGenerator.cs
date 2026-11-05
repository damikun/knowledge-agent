using System.Text;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.TestKit;

public sealed class FakeEmbeddingGenerator(int dimensions = 64, string modelId = "fake-embedding") : IEmbeddingGenerator<string, Embedding<float>>
{
    private int _calls;

    public int Dimensions { get; } = dimensions;

    public int CallCount => Volatile.Read(ref _calls);

    public EmbeddingGeneratorMetadata Metadata { get; } = new("fake", null, modelId, dimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _calls);
        var dims = options?.Dimensions ?? Dimensions;
        var embeddings = values.Select(v => new Embedding<float>(Embed(v, dims)) { ModelId = options?.ModelId ?? modelId });
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    public static float[] Embed(string text, int dimensions)
    {
        var vector = new float[dimensions];
        foreach (var token in Tokenize(text))
        {
            var hash = Fnv1a(token);
            var index = (int)(hash % (uint)dimensions);
            vector[index] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
        else
        {
            vector[0] = 1f;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0)
            {
                if (builder.Length > 2)
                {
                    yield return builder.ToString();
                }

                builder.Clear();
            }
        }

        if (builder.Length > 2)
        {
            yield return builder.ToString();
        }
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }
}
