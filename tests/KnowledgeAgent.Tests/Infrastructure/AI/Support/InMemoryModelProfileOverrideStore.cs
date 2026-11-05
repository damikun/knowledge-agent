using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI.Configuration;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal sealed class InMemoryOverrideState
{
    private int _reads;

    public ConcurrentDictionary<string, ModelProfileOverride> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Failure { get; set; }

    public int Reads => Volatile.Read(ref _reads);

    public void Set(ModelProfileOverride profileOverride) => Items[profileOverride.Name] = profileOverride;

    internal void CountRead() => Interlocked.Increment(ref _reads);
}

internal sealed class InMemoryModelProfileOverrideStore(InMemoryOverrideState state) : IModelProfileOverrideStore
{
    public Task<IReadOnlyList<ModelProfileOverride>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        state.CountRead();
        return Task.FromResult<IReadOnlyList<ModelProfileOverride>>(state.Items.Values.OrderBy(o => o.Name, StringComparer.Ordinal).ToList());
    }

    public Task<ModelProfileOverride?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        state.CountRead();
        return Task.FromResult(state.Items.TryGetValue(name, out var profileOverride) ? profileOverride : null);
    }

    public Task UpsertAsync(ModelProfileOverride profileOverride, CancellationToken cancellationToken = default)
    {
        state.Set(profileOverride);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.Items.TryRemove(name, out _));

    private void ThrowIfFailing()
    {
        if (state.Failure is { } failure)
        {
            throw failure;
        }
    }
}
