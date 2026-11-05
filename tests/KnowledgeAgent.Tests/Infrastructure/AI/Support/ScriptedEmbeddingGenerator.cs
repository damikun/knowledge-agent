using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal sealed class ScriptedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lock _gate = new();

    public List<IReadOnlyList<string>> Batches { get; } = [];

    public List<EmbeddingGenerationOptions?> Options { get; } = [];

    public Func<int, int> DimensionsFor { get; set; } = _ => 8;

    public int? VectorsPerBatch { get; set; }

    public IReadOnlyList<string> AllInputs
    {
        get
        {
            lock (_gate)
            {
                return Batches.SelectMany(b => b).ToList();
            }
        }
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var batch = values.ToList();
        int start;
        lock (_gate)
        {
            start = Batches.Sum(b => b.Count);
            Batches.Add(batch);
            Options.Add(options);
        }

        var count = VectorsPerBatch ?? batch.Count;
        var result = new GeneratedEmbeddings<Embedding<float>>(
            Enumerable.Range(0, count).Select(i => new Embedding<float>(Enumerable.Repeat(0.5f, DimensionsFor(start + i)).ToArray())));
        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
