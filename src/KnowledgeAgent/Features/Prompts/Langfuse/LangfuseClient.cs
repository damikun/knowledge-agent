using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Infrastructure.Langfuse;

namespace KnowledgeAgent.Features.Prompts.Langfuse;

internal sealed class LangfuseClient(HttpClient httpClient)
{
    public const string DefaultLabel = "production";
    internal const int ListPageSize = 50;
    internal const int MaxListPages = 100;

    private const string PromptsPath = "api/public/v2/prompts";
    private const int MaxErrorDetailLength = 300;

    public static void ConfigureHttpClient(HttpClient client, LangfuseOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!options.IsConfigured)
        {
            return;
        }

        // The trailing slash keeps a path prefix such as https://host/langfuse/ when relative paths are combined.
        // Any other value leaves the client unconfigured, so calls fail and prompts fall back to the local defaults.
        if (Uri.TryCreate(options.BaseUrl!.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
            && (baseAddress.Scheme == Uri.UriSchemeHttp || baseAddress.Scheme == Uri.UriSchemeHttps))
        {
            client.BaseAddress = baseAddress;
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.PublicKey!.Trim()}:{options.SecretKey!.Trim()}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PromptTemplate?> GetPromptAsync(string name, string? label = null, int? version = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var query = version is { } number
            ? "version=" + number.ToString(CultureInfo.InvariantCulture)
            : "label=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(label) ? DefaultLabel : label.Trim());

        // Escaped so that folder-style names such as team/summary work.
        var path = $"{PromptsPath}/{Uri.EscapeDataString(name)}?{query}";

        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParsePrompt(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LangfusePromptPage> ListPromptsAsync(int page = 1, int limit = ListPageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var path = string.Create(CultureInfo.InvariantCulture, $"{PromptsPath}?page={page}&limit={limit}");
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParsePromptPage(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false), page, limit);
    }

    public async Task<IReadOnlyList<LangfusePromptListItem>> ListAllPromptsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<LangfusePromptListItem>();
        for (var page = 1; page <= MaxListPages; page++)
        {
            var result = await ListPromptsAsync(page, ListPageSize, cancellationToken).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (result.Items.Count == 0 || page >= result.TotalPages)
            {
                break;
            }
        }

        return items;
    }

    public async Task<PromptTemplate> CreatePromptAsync(LangfuseCreatePromptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var body = LangfuseJson.BuildCreateBody(request).ToJsonString();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, PromptsPath, content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Post, PromptsPath, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParsePrompt(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LangfusePromptListItem?> GetPromptMetaAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var path = string.Create(CultureInfo.InvariantCulture, $"{PromptsPath}?name={Uri.EscapeDataString(name)}&page=1&limit={ListPageSize}");
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        var page = LangfuseJson.ParsePromptPage(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false), 1, ListPageSize);
        return page.Items.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
    }

    // A label belongs to one version at a time, so Langfuse takes it off the other versions.
    public async Task<PromptTemplate> UpdateLabelsAsync(string name, int version, IReadOnlyList<string> newLabels, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(newLabels);

        var path = string.Create(CultureInfo.InvariantCulture, $"{PromptsPath}/{Uri.EscapeDataString(name)}/versions/{version}");
        var body = new JsonObject { ["newLabels"] = new JsonArray(newLabels.Select(label => (JsonNode)JsonValue.Create(label)!).ToArray()) };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Patch, path, content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Patch, path, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParsePrompt(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LangfuseHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        const string path = "api/public/health";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParseHealth(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<LangfuseProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        const string path = "api/public/projects";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        return LangfuseJson.ParseProjects(await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            throw new LangfuseApiException(
                "Langfuse is not configured: Langfuse:BaseUrl (an absolute URL), Langfuse:PublicKey and Langfuse:SecretKey are required.");
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
        var route = path.Split('?')[0];
        throw new LangfuseApiException(
            $"Langfuse returned {(int)response.StatusCode} ({response.StatusCode}) for {method} {route}{(detail is null ? "" : ": " + detail)}",
            response.StatusCode);
    }

    private static async Task<string?> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                if (JsonNode.Parse(text) is JsonObject error && error["message"] is JsonValue message
                    && message.GetValueKind() == JsonValueKind.String)
                {
                    text = message.GetValue<string>();
                }
            }
            catch (JsonException)
            {
                // Not JSON, such as an HTML error page from a proxy, so the raw text is used.
            }

            text = text.ReplaceLineEndings(" ").Trim();
            return text.Length <= MaxErrorDetailLength ? text : text[..MaxErrorDetailLength] + "…";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new LangfuseApiException("Langfuse returned a response that is not valid JSON.", response.StatusCode, ex);
        }
    }
}
