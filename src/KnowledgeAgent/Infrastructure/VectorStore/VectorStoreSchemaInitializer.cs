using System.Diagnostics;
using System.Globalization;
using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.VectorStore;

// AttemptTimeout bounds a single attempt, since an unreachable host would otherwise hang until the HTTP timeout.
internal sealed record StartupRetryPolicy(TimeSpan Timeout, TimeSpan InitialDelay, TimeSpan MaxDelay, TimeSpan AttemptTimeout)
{
    public static StartupRetryPolicy Default { get; } = new(
        Timeout: TimeSpan.FromSeconds(60),
        InitialDelay: TimeSpan.FromMilliseconds(500),
        MaxDelay: TimeSpan.FromSeconds(5),
        AttemptTimeout: TimeSpan.FromSeconds(10));
}

internal sealed partial class VectorStoreSchemaInitializer : IStartupInitializer
{
    private readonly IVectorStore _store;
    private readonly VectorStoreOptions _options;
    private readonly ILogger<VectorStoreSchemaInitializer> _logger;
    private readonly StartupRetryPolicy _retryPolicy;

    public VectorStoreSchemaInitializer(IVectorStore store, IOptions<VectorStoreOptions> options, ILogger<VectorStoreSchemaInitializer> logger)
        : this(store, options, logger, StartupRetryPolicy.Default)
    {
    }

    internal VectorStoreSchemaInitializer(
        IVectorStore store,
        IOptions<VectorStoreOptions> options,
        ILogger<VectorStoreSchemaInitializer> logger,
        StartupRetryPolicy retryPolicy)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
        _retryPolicy = retryPolicy;
    }

    public int Order => StartupOrder.VectorStore;

    public string Name => "VectorStore";

    public bool IsCritical => true;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnsureSchemaOnStartup)
        {
            Log.Skipped(_logger, _store.ProviderName);
            return;
        }

        var elapsed = Stopwatch.StartNew();
        var delay = _retryPolicy.InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(_retryPolicy.AttemptTimeout);
            try
            {
                await _store.EnsureSchemaAsync(attemptTimeout.Token);
                Log.Ready(_logger, _store.ProviderName, attempt, elapsed.Elapsed.TotalSeconds);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && VectorStoreErrors.IsTransient(ex))
            {
                var remaining = _retryPolicy.Timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The {_store.ProviderName} vector store could not be initialized within {_retryPolicy.Timeout.TotalSeconds:0.#} s ({attempt} attempts). Last error: {ex.Message}"),
                        ex);
                }

                var wait = delay < remaining ? delay : remaining;
                Log.AttemptFailed(_logger, _store.ProviderName, attempt, wait.TotalSeconds, ex);
                await Task.Delay(wait, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _retryPolicy.MaxDelay.Ticks));
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "{Provider} vector store schema check skipped (VectorStore:EnsureSchemaOnStartup is false)")]
        public static partial void Skipped(ILogger logger, string provider);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "{Provider} vector store initialized after {Attempts} attempt(s) in {ElapsedSeconds:0.0} s")]
        public static partial void Ready(ILogger logger, string provider, int attempts, double elapsedSeconds);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "{Provider} vector store is not ready (attempt {Attempt}); retrying in {DelaySeconds:0.0} s")]
        public static partial void AttemptFailed(ILogger logger, string provider, int attempt, double delaySeconds, Exception exception);
    }
}
