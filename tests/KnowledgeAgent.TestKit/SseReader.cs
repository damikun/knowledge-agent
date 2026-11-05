using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace KnowledgeAgent.TestKit;

public sealed record SseEvent(string Event, string Data, string? Id = null)
{
    public T Deserialize<T>() => JsonSerializer.Deserialize<T>(Data, JsonSerializerOptions.Web)!;

    public JsonElement Json => JsonDocument.Parse(Data).RootElement.Clone();
}

public static class SseReader
{
    public static async Task<IReadOnlyList<SseEvent>> ReadAllAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var events = new List<SseEvent>();
        await foreach (var item in ReadAsync(response, cancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    public static async IAsyncEnumerable<SseEvent> ReadAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string eventName = "message";
        string? id = null;
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new SseEvent(eventName, data.ToString(), id);
                }

                eventName = "message";
                id = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? string.Empty : line[(separator + 1)..].TrimStart(' ');
            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    break;
            }
        }

        if (data.Length > 0)
        {
            yield return new SseEvent(eventName, data.ToString(), id);
        }
    }

    public static MediaTypeWithQualityHeaderValue AcceptHeader { get; } = new("text/event-stream");
}
