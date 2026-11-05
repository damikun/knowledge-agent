namespace KnowledgeAgent.Infrastructure.Persistence;

// DbContext does not support concurrent operations, so the stores of one scope serialize their calls through this gate.
internal sealed class DbContextGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<Lease> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    public readonly struct Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
