using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Voyage;

internal sealed class VoyageClient : IDisposable
{
    private const int MaxErrorDetailLength = 300;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;

    public VoyageClient(Uri endpoint, string apiKey, HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            // The trailing slash keeps a path prefix such as /voyage/v1/ when relative paths are combined.
            BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/"),
            Timeout = RequestTimeout,
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<double>> RerankAsync(string model, string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["query"] = query,
            ["documents"] = new JsonArray(documents.Select(document => (JsonNode)document).ToArray()),
            ["model"] = model,
            ["truncation"] = true,
        };

        var json = await PostAsync("rerank", body, cancellationToken);
        var scores = new double[documents.Count];
        foreach (var result in json["data"] as JsonArray ?? [])
        {
            var index = result?["index"]?.GetValue<int>() ?? -1;
            if (index >= 0 && index < scores.Length)
            {
                scores[index] = result!["relevance_score"]?.GetValue<double>() ?? 0;
            }
        }

        return scores;
    }

    public async Task<(IReadOnlyList<float[]> Vectors, int? TotalTokens)> EmbedAsync(
        string model,
        IReadOnlyList<string> inputs,
        string? inputType,
        int? dimensions,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["input"] = new JsonArray(inputs.Select(input => (JsonNode)input).ToArray()),
            ["model"] = model,
            ["truncation"] = true,
        };
        if (inputType is not null)
        {
            body["input_type"] = inputType;
        }

        if (dimensions is { } size)
        {
            body["output_dimension"] = size;
        }

        var json = await PostAsync("embeddings", body, cancellationToken);
        var vectors = new float[inputs.Count][];
        foreach (var item in json["data"] as JsonArray ?? [])
        {
            var index = item?["index"]?.GetValue<int>() ?? -1;
            if (index >= 0 && index < vectors.Length && item!["embedding"] is JsonArray embedding)
            {
                vectors[index] = embedding.Select(value => value!.GetValue<float>()).ToArray();
            }
        }

        if (vectors.Any(vector => vector is null))
        {
            throw new VoyageApiException("Voyage returned fewer embeddings than inputs.", null);
        }

        return (vectors, json["usage"]?["total_tokens"]?.GetValue<int>());
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(path, content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new VoyageApiException($"Voyage is unreachable ({ex.Message}).", null, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new VoyageApiException(
                    $"Voyage returned {(int)response.StatusCode} ({response.StatusCode}) for /{path}{Detail(text)}",
                    response.StatusCode);
            }

            try
            {
                return JsonNode.Parse(text) ?? throw new VoyageApiException("Voyage returned an empty response.", response.StatusCode);
            }
            catch (JsonException ex)
            {
                throw new VoyageApiException("Voyage returned a response that is not valid JSON.", response.StatusCode, ex);
            }
        }
    }

    private static string Detail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            if (JsonNode.Parse(text)?["detail"] is JsonValue detail && detail.TryGetValue<string>(out var message))
            {
                text = message;
            }
        }
        catch (JsonException)
        {
            // Not JSON, for example a proxy's error page: the raw text is used.
        }

        text = text.ReplaceLineEndings(" ").Trim();
        return ": " + (text.Length <= MaxErrorDetailLength ? text : text[..MaxErrorDetailLength] + "…");
    }
}

public sealed class VoyageApiException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
