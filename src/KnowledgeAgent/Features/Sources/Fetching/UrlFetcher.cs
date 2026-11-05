using System.Buffers;
using System.Globalization;
using System.Net;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources.Fetching;

internal sealed record FetchedContent(
    Uri RequestedUri,
    Uri FinalUri,
    string? MediaType,
    string? CharSet,
    string? FileName,
    byte[] Content,
    int Redirects)
{
    public bool NotModified { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
}

internal sealed record FetchOptions
{
    public static FetchOptions Default { get; } = new();

    public string? IfNoneMatch { get; init; }
    public string? IfModifiedSince { get; init; }
    public string? Accept { get; init; }

    // Runs before every request, redirects included, such as to wait for the host's turn.
    public Func<Uri, CancellationToken, ValueTask>? BeforeRequest { get; init; }
}

internal sealed partial class UrlFetcher(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<UrlFetcher> logger)
{
    public const string HttpClientName = "KnowledgeAgent.Sources.Fetch";
    public const int MaxRedirects = 5;

    private const string AcceptHeader =
        "text/html,application/xhtml+xml,application/pdf;q=0.9,text/markdown;q=0.9,text/plain;q=0.8,*/*;q=0.5";

    public Task<FetchedContent> FetchAsync(Uri url, CancellationToken cancellationToken = default) =>
        FetchAsync(url, FetchOptions.Default, cancellationToken);

    // A 304 answer to a conditional request comes back with NotModified set and no content.
    public async Task<FetchedContent> FetchAsync(Uri url, FetchOptions fetchOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(fetchOptions);
        var settings = options.CurrentValue;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.FetchTimeoutSeconds));
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var token = linkedSource.Token;

        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = url;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                EnsureHttpScheme(current);
                if (!settings.AllowPrivateNetworks && IsBlockedLiteral(current))
                {
                    throw new BlockedAddressException(current.Host);
                }

                if (fetchOptions.BeforeRequest is { } beforeRequest)
                {
                    await beforeRequest(current, token).ConfigureAwait(false);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("User-Agent", settings.UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", fetchOptions.Accept ?? AcceptHeader);
                if (fetchOptions.IfNoneMatch is { Length: > 0 } etag)
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                }

                if (fetchOptions.IfModifiedSince is { Length: > 0 } modifiedSince)
                {
                    request.Headers.TryAddWithoutValidation("If-Modified-Since", modifiedSince);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= MaxRedirects)
                    {
                        throw new IngestionException($"Too many redirects (more than {MaxRedirects}).", isPermanent: true);
                    }

                    var location = response.Headers.Location
                        ?? throw new IngestionException("The server sent a redirect without a Location header.", isPermanent: true);
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    LogRedirect(logger, current, next, (int)response.StatusCode);
                    current = next;
                    continue;
                }

                var etagHeader = response.Headers.ETag?.ToString();
                var lastModifiedHeader = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new FetchedContent(url, current, null, null, null, [], redirects)
                    {
                        NotModified = true,
                        ETag = etagHeader ?? fetchOptions.IfNoneMatch,
                        LastModified = lastModifiedHeader ?? fetchOptions.IfModifiedSince,
                    };
                }

                EnsureSuccess(response);
                var body = await ReadBodyAsync(response.Content, settings.MaxDownloadBytes, token).ConfigureAwait(false);
                var headers = response.Content.Headers;
                var fileName = headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName?.Trim('"');
                return new FetchedContent(
                    url,
                    current,
                    ContentTypeDetector.NormalizeMediaType(headers.ContentType?.MediaType),
                    headers.ContentType?.CharSet,
                    string.IsNullOrWhiteSpace(fileName) ? current.AbsolutePath : fileName,
                    body,
                    redirects)
                {
                    ETag = etagHeader,
                    LastModified = lastModifiedHeader,
                };
            }
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new IngestionException(
                string.Create(CultureInfo.InvariantCulture, $"The request timed out after {timeout.TotalSeconds:0} seconds."),
                isPermanent: false,
                ex);
        }
        catch (BlockedAddressException ex)
        {
            throw new IngestionException(ex.Message, isPermanent: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Translate(ex, current);
        }
        catch (IOException ex)
        {
            // HttpIOException: the connection dropped while the body was being read.
            throw new IngestionException($"The connection to '{current.IdnHost}' was interrupted.", isPermanent: false, ex);
        }
    }

    internal static SocketsHttpHandler CreateHandler(IOptionsMonitor<IngestionOptions> options, ILogger logger)
    {
        var connectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.FetchTimeoutSeconds, 1, 120));
        return new SocketsHttpHandler
        {
            // FetchAsync follows redirects itself to cap them and check the scheme of every hop.
            AllowAutoRedirect = false,
            // A proxy would open the connections instead of SafeConnector and bypass the address check.
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 16,
            ConnectCallback = (context, cancellationToken) =>
                SafeConnector.ConnectAsync(context, options.CurrentValue.AllowPrivateNetworks, logger, cancellationToken),
        };
    }

    private static void EnsureHttpScheme(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new IngestionException(
                $"Only http and https URLs can be fetched (got '{(uri.IsAbsoluteUri ? uri.Scheme : "relative")}').",
                isPermanent: true);
        }
    }

    // Caught before connecting, so a crawled link or redirect to an address literal is never requested. Host names are
    // checked by SafeConnector once they resolve.
    private static bool IsBlockedLiteral(Uri uri)
    {
        var host = uri.Host.Length > 2 && uri.Host[0] == '[' && uri.Host[^1] == ']' ? uri.Host[1..^1] : uri.Host;
        return IPAddress.TryParse(host, out var address) && NetworkAddressPolicy.IsBlocked(address);
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = (int)response.StatusCode;

        // Client errors will not change on retry, except timeouts, "too early" and rate limiting.
        var permanent = code is >= 400 and < 500 and not 408 and not 425 and not 429;
        throw new IngestionException(
            string.Create(CultureInfo.InvariantCulture, $"The server responded with HTTP {code} ({response.StatusCode})."),
            isPermanent: permanent)
        {
            StatusCode = code,
        };
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw TooLarge(maxBytes);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream((int)Math.Min(content.Headers.ContentLength ?? 64 * 1024, maxBytes));
        var chunk = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw TooLarge(maxBytes);
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }

    private static IngestionException TooLarge(long maxBytes) =>
        new(string.Create(CultureInfo.InvariantCulture, $"The content exceeds the maximum download size of {FormatBytes(maxBytes)}."), isPermanent: true);

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024d):0.#} MB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    private static IngestionException Translate(HttpRequestException exception, Uri uri)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is BlockedAddressException blocked)
            {
                return new IngestionException(blocked.Message, isPermanent: true, exception);
            }

            if (inner is IngestionException ingestion)
            {
                return ingestion;
            }
        }

        var host = uri.IdnHost;
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => new IngestionException($"The host '{host}' could not be resolved.", isPermanent: false, exception),
            HttpRequestError.ConnectionError => new IngestionException($"Could not connect to '{host}'.", isPermanent: false, exception),
            HttpRequestError.SecureConnectionError => new IngestionException($"The secure connection to '{host}' failed.", isPermanent: true, exception),
            HttpRequestError.ConfigurationLimitExceeded => new IngestionException("The server response exceeds the allowed limits.", isPermanent: true, exception),
            HttpRequestError.ResponseEnded or HttpRequestError.InvalidResponse => new IngestionException($"'{host}' sent an invalid or incomplete response.", isPermanent: false, exception),
            _ => new IngestionException($"The request to '{host}' failed.", isPermanent: false, exception),
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Following HTTP {StatusCode} redirect from {From} to {To}")]
    private static partial void LogRedirect(ILogger logger, Uri from, Uri to, int statusCode);
}
