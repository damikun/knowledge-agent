using System.Net;
using System.Net.Sockets;

namespace KnowledgeAgent.Features.Sources.Fetching;

internal static class NetworkAddressPolicy
{
    private static readonly IPNetwork[] BlockedIPv4 = Parse(
        "0.0.0.0/8",
        "10.0.0.0/8",
        "100.64.0.0/10",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.0.0.0/24",
        "192.0.2.0/24",
        "192.88.99.0/24",
        "192.168.0.0/16",
        "198.18.0.0/15",
        "198.51.100.0/24",
        "203.0.113.0/24",
        "224.0.0.0/4",
        "240.0.0.0/4");

    private static readonly IPNetwork[] BlockedIPv6 = Parse(
        "::/96",
        "64:ff9b:1::/48",
        "100::/64",
        "2001::/32",
        "2001:db8::/32",
        "3fff::/20",
        "fc00::/7",
        "fe80::/10",
        "fec0::/10",
        "ff00::/8");

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
