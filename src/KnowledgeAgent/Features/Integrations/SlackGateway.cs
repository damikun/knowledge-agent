using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Integrations;

public sealed class SlackApiException(string method, string error) : Exception($"Slack {method} failed: {error}")
{
    public string Error { get; } = error;
}

public interface ISlackGateway
{
    // Returns the message ts, which identifies it (and its thread) in the channel.
    Task<string> PostMessageAsync(string channel, string text, JsonArray? blocks = null, string? threadTs = null, CancellationToken cancellationToken = default);

    Task UpdateMessageAsync(string channel, string ts, string text, JsonArray? blocks = null, CancellationToken cancellationToken = default);

    Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default);

    // auth.test: which workspace and bot the token belongs to.
    Task<string> TestAsync(CancellationToken cancellationToken = default);

    // apps.connections.open: a fresh Socket Mode WebSocket address.
    Task<Uri> OpenSocketAsync(CancellationToken cancellationToken = default);
}

internal sealed class SlackWebApiGateway(HttpClient http, IIntegrationSettings integrations) : ISlackGateway
{
    public static readonly Uri BaseAddress = new("https://slack.com/api/");

    private readonly ConcurrentDictionary<string, string> _userNames = new(StringComparer.Ordinal);

    public async Task<string> PostMessageAsync(string channel, string text, JsonArray? blocks = null, string? threadTs = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["channel"] = channel, ["text"] = text, ["unfurl_links"] = false, ["unfurl_media"] = false };
        if (blocks is not null)
        {
            body["blocks"] = blocks.DeepClone();
        }

        if (threadTs is not null)
        {
            body["thread_ts"] = threadTs;
        }

        var response = await CallAsync("chat.postMessage", body, appToken: false, cancellationToken);
        return response["ts"]?.GetValue<string>() ?? throw new SlackApiException("chat.postMessage", "no ts in the response");
    }

    public async Task UpdateMessageAsync(string channel, string ts, string text, JsonArray? blocks = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["channel"] = channel, ["ts"] = ts, ["text"] = text };
        if (blocks is not null)
        {
            body["blocks"] = blocks.DeepClone();
        }

        await CallAsync("chat.update", body, appToken: false, cancellationToken);
    }

    public async Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_userNames.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var settings = await integrations.GetSlackAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, "users.info?user=" + Uri.EscapeDataString(userId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.BotToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        if (json?["ok"]?.GetValue<bool>() != true)
        {
            return null;
        }

        var user = json["user"];
        var name = new[]
            {
                user?["profile"]?["display_name"]?.GetValue<string>(),
                user?["profile"]?["real_name"]?.GetValue<string>(),
                user?["real_name"]?.GetValue<string>(),
                user?["name"]?.GetValue<string>(),
            }
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (name is not null)
        {
            _userNames[userId] = name;
        }

        return name;
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("auth.test", new JsonObject(), appToken: false, cancellationToken);
        return $"Signed in to {response["team"]?.GetValue<string>() ?? "Slack"} as {response["user"]?.GetValue<string>() ?? "the bot"}.";
    }

    public async Task<Uri> OpenSocketAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("apps.connections.open", new JsonObject(), appToken: true, cancellationToken);
        return Uri.TryCreate(response["url"]?.GetValue<string>(), UriKind.Absolute, out var url)
            ? url
            : throw new SlackApiException("apps.connections.open", "no url in the response");
    }

    private async Task<JsonObject> CallAsync(string method, JsonObject body, bool appToken, CancellationToken cancellationToken)
    {
        var settings = await integrations.GetSlackAsync(cancellationToken);
        var token = appToken ? settings.AppToken : settings.BotToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new SlackApiException(method, appToken ? "no app-level token is set" : "no bot token is set");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, method)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken) ?? new JsonObject();
        if (json["ok"]?.GetValue<bool>() != true)
        {
            throw new SlackApiException(method, json["error"]?.GetValue<string>() ?? $"HTTP {(int)response.StatusCode}");
        }

        return json;
    }
}
