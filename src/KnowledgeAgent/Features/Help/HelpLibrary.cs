namespace KnowledgeAgent.Features.Help;

internal sealed class HelpLibrary(IHelpSource source) : IDisposable
{
    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile HelpIndex? _index;

    public async Task<HelpIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_index is { } index)
        {
            return index;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            return _index ??= new HelpIndex(await source.LoadAsync(cancellationToken));
        }
        finally
        {
            _loading.Release();
        }
    }

    public async Task<HelpIndex> ReloadAsync(CancellationToken cancellationToken)
    {
        await _loading.WaitAsync(cancellationToken);
        try
        {
            return _index = new HelpIndex(await source.LoadAsync(cancellationToken));
        }
        finally
        {
            _loading.Release();
        }
    }

    public void Dispose() => _loading.Dispose();
}
