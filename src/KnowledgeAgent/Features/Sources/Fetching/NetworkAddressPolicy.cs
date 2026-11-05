using System.Net;
using System.Net.Sockets;

namespace KnowledgeAgent.Features.Sources.Fetching;

internal static class NetworkAddressPolicy
{
    private static readonly IPNetwork[] BlockedIPv4 = Parse(
        "0.0.0.0/8",          // "this network", includes the unspecified address
        "10.0.0.0/8",         // private
        "100.64.0.0/10",      // carrier-grade NAT
        "127.0.0.0/8",        // loopback
        "169.254.0.0/16",     // link-local (cloud metadata endpoints)
        "172.16.0.0/12",      // private
        "192.0.0.0/24",       // IETF protocol assignments
        "192.0.2.0/24",       // documentation (TEST-NET-1)
        "192.88.99.0/24",     // 6to4 relay anycast (deprecated)
        "192.168.0.0/16",     // private
        "198.18.0.0/15",      // benchmarking
        "198.51.100.0/24",    // documentation (TEST-NET-2)
        "203.0.113.0/24",     // documentation (TEST-NET-3)
        "224.0.0.0/4",        // multicast
        "240.0.0.0/4");       // reserved, includes broadcast

    private static readonly IPNetwork[] BlockedIPv6 = Parse(
        "::/96",              // unspecified, loopback and deprecated IPv4-compatible addresses
        "64:ff9b:1::/48",     // local-use NAT64
        "100::/64",           // discard-only
        "2001::/32",          // Teredo tunnelling
        "2001:db8::/32",      // documentation
        "3fff::/20",          // documentation
        "fc00::/7",           // unique local
        "fe80::/10",          // link-local
        "fec0::/10",          // site-local (deprecated)
        "ff00::/8");          // multicast

    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    public static bool IsPublic(IPAddress address) => !IsBlocked(address);

    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                return Matches(BlockedIPv4, address);
            case AddressFamily.InterNetworkV6:
                // IPv6 forms that embed an IPv4 address are judged by the embedded address.
                if (address.IsIPv4MappedToIPv6)
                {
                    return IsBlocked(address.MapToIPv4());
                }

                if (TryGetEmbeddedIPv4(address) is { } embedded)
                {
                    return IsBlocked(embedded);
                }

                return Matches(BlockedIPv6, address);
            default:
                return true;
        }
    }

    private static IPAddress? TryGetEmbeddedIPv4(IPAddress address)
    {
        var withoutScope = address.ScopeId == 0 ? address : new IPAddress(address.GetAddressBytes());
        Span<byte> bytes = stackalloc byte[16];
        if (!withoutScope.TryWriteBytes(bytes, out _))
        {
            return null;
        }

        if (Nat64.Contains(withoutScope))
        {
            return new IPAddress(bytes[12..16]);
        }

        if (SixToFour.Contains(withoutScope))
        {
            return new IPAddress(bytes[2..6]);
        }

        return null;
    }

    private static bool Matches(IPNetwork[] networks, IPAddress address)
    {
        var withoutScope = address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes())
            : address;
        foreach (var network in networks)
        {
            if (network.Contains(withoutScope))
            {
                return true;
            }
        }

        return false;
    }

    private static IPNetwork[] Parse(params string[] networks) => [.. networks.Select(network => IPNetwork.Parse(network))];
}
