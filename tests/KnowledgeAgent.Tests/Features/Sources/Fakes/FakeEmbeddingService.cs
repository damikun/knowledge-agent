using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Tests.Features.Sources.Fakes;

internal sealed record EmbeddingCall(IReadOnlyList<string> Inputs, EmbeddingPurpose Purpose);

internal sealed class FakeEmbeddingService(int dimensions = 64) : IEmbeddingService
{
    private int _failuresRemaining;

    public int Dimensions { get; } = dimensions;

    public string Model { get; set; } = "fake-embedding";

    public string ModelKey => $"fake:{Model}:{Dimensions}";

    public ConcurrentQueue<EmbeddingCall> Calls { get; } = new();

    public bool AlwaysFail { get; set; }

    public Func<Exception> FailureFactory { get; set; } = () => new InvalidOperationException("Embedding provider unavailable.");

    public TaskCompletionSource? Gate { get; set; }

    public void FailNext(int count) => Volatile.Write(ref _failuresRemaining, count);

    // A model is missing while every call fails with ModelNotConfiguredException, as the real service does then.
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!(AlwaysFail && FailureFactory() is ModelNotConfiguredException));

    public async Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(new EmbeddingCall([.. inputs], purpose));
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (AlwaysFail || Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw FailureFactory();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var vectors = inputs.Select(input => new ReadOnlyMemory<float>(FakeEmbeddingGenerator.Embed(input, Dimensions))).ToList();
        return new EmbeddingResult(ModelKey, Dimensions, vectors);
    }
}
