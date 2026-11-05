using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI;

public sealed record ProviderRejection(string ProviderKey, int Status, DateTimeOffset At);

// The last rejected key per provider, in memory per instance; a later successful call clears it.
public interface IProviderErrorLog
{
    void RecordFailure(string providerKey, Exception exception);

    void RecordSuccess(string providerKey);

    IReadOnlyList<ProviderRejection> ListRejections(DateTimeOffset since);
}

internal sealed class ProviderErrorLog(TimeProvider timeProvider) : IProviderErrorLog
{
    private readonly ConcurrentDictionary<string, ProviderRejection> _rejections = new(StringComparer.OrdinalIgnoreCase);

    public void RecordFailure(string providerKey, Exception exception)
    {
        if (RejectedStatus(exception) is { } status)
        {
            _rejections[providerKey] = new ProviderRejection(providerKey, status, timeProvider.GetUtcNow());
        }
    }

    public void RecordSuccess(string providerKey)
    {
        // Most calls succeed, so the common path is a lookup without a write.
        if (!_rejections.IsEmpty)
        {
            _rejections.TryRemove(providerKey, out _);
        }
    }

    public IReadOnlyList<ProviderRejection> ListRejections(DateTimeOffset since) =>
        _rejections.Values.Where(item => item.At >= since).OrderBy(item => item.ProviderKey, StringComparer.Ordinal).ToList();

    // The provider SDKs throw their own exception types; each carries the HTTP status as StatusCode or Status.
    internal static int? RejectedStatus(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var status = current switch
            {
                HttpRequestException { StatusCode: { } code } => (int)code,
                _ => StatusProperty(current),
            };
            if (status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
            {
                return status;
            }
        }

        return null;
    }

    private static int? StatusProperty(Exception exception)
    {
        var type = exception.GetType();
        foreach (var name in (string[])["StatusCode", "Status"])
        {
            switch (type.GetProperty(name)?.GetValue(exception))
            {
                case HttpStatusCode code:
                    return (int)code;
                case int number:
                    return number;
            }
        }

        return null;
    }
}

// Innermost in the chat pipeline: sees the provider's own exceptions before anything wraps them.
internal sealed class ProviderErrorChatClient(IChatClient innerClient, IProviderErrorLog log, string providerKey) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            log.RecordSuccess(providerKey);
            return response;
        }
        catch (Exception ex) when (Observe(ex))
        {
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (enumerator)
        {
            var first = true;
            while (true)
            {
                // The filter only looks at the exception, so nothing is caught and yield can stay outside the try.
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        yield break;
                    }
                }
                catch (Exception ex) when (Observe(ex))
                {
                    throw;
                }

                if (first)
                {
                    first = false;
                    log.RecordSuccess(providerKey);
                }

                yield return enumerator.Current;
            }
        }
    }

    private bool Observe(Exception exception)
    {
        if (exception is not OperationCanceledException)
        {
            log.RecordFailure(providerKey, exception);
        }

        return false;
    }
}

internal sealed class ProviderErrorEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> innerGenerator,
    IProviderErrorLog log,
    string providerKey) : DelegatingEmbeddingGenerator<string, Embedding<float>>(innerGenerator)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await base.GenerateAsync(values, options, cancellationToken);
            log.RecordSuccess(providerKey);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && Observe(ex))
        {
            throw;
        }
    }

    private bool Observe(Exception exception)
    {
        log.RecordFailure(providerKey, exception);
        return false;
    }
}
