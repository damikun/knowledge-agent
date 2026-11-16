using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.Jobs;

public interface IJobSteps
{
    Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> step);
}

public sealed class JobStepFailedException(string stepId, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string StepId { get; } = stepId;
}

public static class JobSteps
{
    public static IJobSteps Inline { get; } = new InlineJobSteps();

    private sealed class InlineJobSteps : IJobSteps
    {
        public async Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> step)
        {
            try
            {
                return await step(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new JobStepFailedException(id, exception.Message, exception);
            }
        }
    }
}

internal sealed partial class InProcessJobSteps(
    int maxAttempts,
    TimeProvider timeProvider,
    ILogger logger,
    CancellationToken stoppingToken) : IJobSteps
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, object?> _completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> _failed = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public async Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> step)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(step);
        lock (_gate)
        {
            if (_completed.TryGetValue(id, out var result))
            {
                return (T)result!;
            }

            if (_failed.TryGetValue(id, out var failure))
            {
                throw new JobStepFailedException(id, failure.Message, failure);
            }
        }

        var attempts = Math.Max(1, maxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await step(stoppingToken);
                lock (_gate)
                {
                    _completed[id] = result;
                }

                return result;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (attempt >= attempts)
                {
                    lock (_gate)
                    {
                        _failed[id] = exception;
                    }

                    LogStepFailed(logger, exception, id, attempt);
                    throw new JobStepFailedException(id, exception.Message, exception);
                }

                var delay = TimeSpan.FromTicks(Math.Min(FirstRetryDelay.Ticks << (attempt - 1), MaxRetryDelay.Ticks));
                LogStepRetrying(logger, exception, id, attempt, attempts);
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job step {StepId} failed on attempt {Attempt} of {MaxAttempts}; retrying")]
    private static partial void LogStepRetrying(ILogger logger, Exception exception, string stepId, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job step {StepId} failed after {Attempts} attempts")]
    private static partial void LogStepFailed(ILogger logger, Exception exception, string stepId, int attempts);
}
