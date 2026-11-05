using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Sources.Fetching;

internal sealed class BlockedAddressException(string host)
    : Exception("The URL points to a private, loopback or otherwise non-public network address, which is not allowed.")
{
    public string Host { get; } = host;
}

internal static partial class SafeConnector
{
    // Connects only to the addresses it validated, so DNS rebinding between check and connect is not possible.
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        bool allowPrivateNetworks,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = await ResolveAsync(host, cancellationToken).ConfigureAwait(false);

        if (!allowPrivateNetworks)
        {
            var allowed = addresses.Where(NetworkAddressPolicy.IsPublic).ToArray();
            if (allowed.Length == 0)
            {
                LogBlocked(logger, host, string.Join(", ", addresses.Select(a => a.ToString())));
                throw new BlockedAddressException(host);
            }

            addresses = allowed;
        }

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var literal = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        if (IPAddress.TryParse(literal, out var address))
        {
            return [address];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked connection to {Host}: it resolves only to non-public addresses ({Addresses})")]
    private static partial void LogBlocked(ILogger logger, string host, string addresses);
}
