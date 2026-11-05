using System.Diagnostics;
using System.Threading.Channels;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.Jobs;

// Runs jobs in the API process (in-memory mode and tests). Nothing is stored: queued jobs are lost on shutdown
// and the startup recoveries queue them again. Must honor JobPolicy the same way the Inngest engine does.
internal sealed partial class InProcessJobRunner(
    IEnumerable<JobRegistration> registrations,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InProcessJobRunner> logger) : BackgroundService, IJobQueue
{
    private static readonly ActivitySource ActivitySource = new(KnowledgeAgentTelemetry.Jobs);

    private readonly Dictionary<string, KindQueue> _kinds =
        registrations.ToDictionary(registration => registration.Kind, registration => new KindQueue(registration), StringComparer.Ordinal);

    private readonly CancellationTokenSource _shutdown = new();

    public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
    {
        if (!_kinds.TryGetValue(kind, out var queue))
        {
            throw new InvalidOperationException($"No job handler is registered for '{kind}'.");
        }

        var now = timeProvider.GetUtcNow();
        if (queue.Registration.Policy.Idempotent && !queue.TryClaim(key, now))
        {
            LogDuplicateDropped(logger, kind, key);
            return Task.CompletedTask;
        }

        var delay = runAt is { } due ? due - now : TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            _ = QueueLaterAsync(queue, key, delay);
        }
        else
        {
            Submit(queue, key);
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        await base.StopAsync(cancellationToken);
    }

    // Cancelled rather than disposed: a host can stop its services again after disposing them, as the test host does,
    // and a token source without a timer holds nothing to release.
    public override void Dispose()
    {
        _shutdown.Cancel();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_kinds.Values.Select(queue => Task.Run(() => RunKindAsync(queue, stoppingToken), CancellationToken.None)));

    private async Task QueueLaterAsync(KindQueue queue, string key, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, timeProvider, _shutdown.Token);
            Submit(queue, key);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Submit(KindQueue queue, string key)
    {
        if (queue.Registration.Policy.Debounce is { } period && period > TimeSpan.Zero)
        {
            _ = DebounceAsync(queue, key, queue.Debounce(key), period);
        }
        else
        {
            queue.Pending.Writer.TryWrite(key);
        }
    }

    // Only the latest request of a burst gets through: an earlier one finds a newer generation when its wait ends.
    private async Task DebounceAsync(KindQueue queue, string key, long generation, TimeSpan period)
    {
        try
        {
            await Task.Delay(period, timeProvider, _shutdown.Token);
            if (queue.EndDebounce(key, generation))
            {
                queue.Pending.Writer.TryWrite(key);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunKindAsync(KindQueue queue, CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(Math.Max(1, queue.Registration.Policy.MaxConcurrency));
        var running = new List<Task>();
        try
        {
            await foreach (var key in queue.Pending.Reader.ReadAllAsync(stoppingToken))
            {
                if (!queue.TryStart(key))
                {
                    if (queue.Registration.Policy.Overlap == JobOverlap.Skip)
                    {
                        LogSkipped(logger, queue.Registration.Kind, key);
                    }

                    continue;
                }

                await slots.WaitAsync(stoppingToken);
                running.RemoveAll(task => task.IsCompleted);
                running.Add(RunKeyAsync(queue, key, slots, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await Task.WhenAll(running);
    }

    private async Task RunKeyAsync(KindQueue queue, string key, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            do
            {
                await RunJobAsync(queue.Registration, key, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested && queue.TryRunAgain(key));
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task RunJobAsync(JobRegistration registration, string key, CancellationToken stoppingToken)
    {
        var jobId = Guid.CreateVersion7().ToString();
        var maxAttempts = Math.Max(1, registration.Policy.MaxAttempts);
        var steps = new InProcessJobSteps(maxAttempts, timeProvider, logger, stoppingToken);
        for (var attempt = 1; ; attempt++)
        {
            using var activity = ActivitySource.StartActivity($"job {registration.Kind}");
            activity?.SetTag("job.id", jobId);
            activity?.SetTag("job.kind", registration.Kind);
            activity?.SetTag("job.key", key);
            activity?.SetTag("job.attempt", attempt);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = (IJobHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
                await handler.HandleAsync(new JobContext(jobId, registration.Kind, key, attempt, maxAttempts) { Steps = steps }, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                if (attempt >= maxAttempts)
                {
                    LogFailed(logger, exception, registration.Kind, key, attempt);
                    await NotifyFailureAsync(registration, new JobFailure(jobId, registration.Kind, key, attempt, exception.Message), stoppingToken);
                    return;
                }

                LogRetrying(logger, exception, registration.Kind, key, attempt, maxAttempts);
            }
        }
    }

    private async Task NotifyFailureAsync(JobRegistration registration, JobFailure failure, CancellationToken stoppingToken)
    {
        if (!registration.HandlesFailure)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = (IJobFailureHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
            await handler.OnFailureAsync(failure, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogFailureHandlerFailed(logger, exception, registration.Kind, failure.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Kind} {Key} failed on attempt {Attempt} of {MaxAttempts}; retrying")]
    private static partial void LogRetrying(ILogger logger, Exception exception, string kind, string key, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Kind} {Key} failed after {Attempts} attempts")]
    private static partial void LogFailed(ILogger logger, Exception exception, string kind, string key, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "The failure handler of job {Kind} {Key} failed")]
    private static partial void LogFailureHandlerFailed(ILogger logger, Exception exception, string kind, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Kind} {Key} was queued again within the idempotency window and dropped")]
    private static partial void LogDuplicateDropped(ILogger logger, string kind, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Kind} {Key} is still running, so the new request was skipped")]
    private static partial void LogSkipped(ILogger logger, string kind, string key);

    private sealed class KindQueue(JobRegistration registration)
    {
        private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

        // Keys that run now; true when the key was queued again meanwhile and runs once more.
        private readonly Dictionary<string, bool> _running = new(StringComparer.Ordinal);

        // Keys of an idempotent kind and when they were first queued.
        private readonly Dictionary<string, DateTimeOffset> _claimed = new(StringComparer.Ordinal);

        // Keys waiting out their debounce period, with the generation of their latest request.
        private readonly Dictionary<string, long> _debouncing = new(StringComparer.Ordinal);

        private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;
        private long _generation;

        public JobRegistration Registration { get; } = registration;

        public Channel<string> Pending { get; } = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

        public bool TryClaim(string key, DateTimeOffset now)
        {
            lock (_claimed)
            {
                if (now >= _nextPrune)
                {
                    foreach (var expired in _claimed.Where(claim => now - claim.Value >= JobPolicy.IdempotencyWindow).Select(claim => claim.Key).ToList())
                    {
                        _claimed.Remove(expired);
                    }

                    _nextPrune = now + PruneInterval;
                }

                if (_claimed.TryGetValue(key, out var claimedAt) && now - claimedAt < JobPolicy.IdempotencyWindow)
                {
                    return false;
                }

                _claimed[key] = now;
                return true;
            }
        }

        public long Debounce(string key)
        {
            lock (_debouncing)
            {
                var generation = ++_generation;
                _debouncing[key] = generation;
                return generation;
            }
        }

        public bool EndDebounce(string key, long generation)
        {
            lock (_debouncing)
            {
                if (!_debouncing.TryGetValue(key, out var latest) || latest != generation)
                {
                    return false;
                }

                _debouncing.Remove(key);
                return true;
            }
        }

        public bool TryStart(string key)
        {
            lock (_running)
            {
                if (_running.ContainsKey(key))
                {
                    if (Registration.Policy.Overlap == JobOverlap.Queue)
                    {
                        _running[key] = true;
                    }

                    return false;
                }

                _running[key] = false;
                return true;
            }
        }

        public bool TryRunAgain(string key)
        {
            lock (_running)
            {
                if (_running.TryGetValue(key, out var again) && again)
                {
                    _running[key] = false;
                    return true;
                }

                _running.Remove(key);
                return false;
            }
        }
    }
}
