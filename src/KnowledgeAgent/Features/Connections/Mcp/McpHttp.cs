using System.Net;
using KnowledgeAgent.Features.Sources.Fetching;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Connections.Mcp;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public int CallTimeoutSeconds { get; set; } = 30;
    public int ConnectTimeoutSeconds { get; set; } = 15;

    public long MaxResponseBytes { get; set; } = 4 * 1024 * 1024;

    public int MaxTools { get; set; } = 200;
    public int MaxSchemaChars { get; set; } = 20_000;

    public int LoggedResultChars { get; set; } = 2_000;

    public int ToolListRefreshMinutes { get; set; } = 10;
    public int ToolListRefreshTimeoutSeconds { get; set; } = 5;

    public int MaxCallsPerTurn { get; set; } = 6;
    public int MaxAutoCallsPerTurn { get; set; } = 3;
    public int MaxConfirmationsPerTurn { get; set; } = 1;
}

// Same private-network rule as source fetching, with no redirects and no proxy so the address check can't be
// bypassed (SSRF), and a cap on response size.
internal static class McpHttp
{
    public static HttpClient Create(bool allowPrivateNetworks, McpOptions options, ILogger logger)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.ConnectTimeoutSeconds, 1, 120)),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 16,
            ConnectCallback = (context, cancellationToken) => SafeConnector.ConnectAsync(context, allowPrivateNetworks, logger, cancellationToken),
        };

        // Calls are limited by their cancellation tokens; a response may be a stream of events.
        return new HttpClient(new SizeLimitHandler(Math.Max(64 * 1024, options.MaxResponseBytes)) { InnerHandler = handler })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private sealed class SizeLimitHandler(long maxBytes) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            {
                response.Dispose();
                throw new HttpRequestException($"The MCP server sent more than {maxBytes / 1024} KB.");
            }

            var inner = response.Content;
            var limited = new StreamContent(new LimitedStream(await inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), maxBytes));
            foreach (var header in inner.Headers)
            {
                limited.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = limited;
            return response;
        }
    }

    private sealed class LimitedStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            if (_read > maxBytes)
            {
                throw new IOException($"The MCP server sent more than {maxBytes / 1024} KB.");
            }

            return read;
        }
    }
}
