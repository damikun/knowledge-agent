using System.Net;
using KnowledgeAgent.Features.Sources.Fetching;

namespace KnowledgeAgent.Tests.Features.Sources.Fetching;

public sealed class NetworkAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.254")]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]    // cloud metadata endpoint
    [InlineData("100.64.0.1")]         // CGNAT 100.64/10
    [InlineData("100.127.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.10")]         // documentation
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]            // unique local fc00::/7
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]            // link-local fe80::/10
    [InlineData("fe80::1%3")]
    [InlineData("febf:ffff::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]    // NAT64 of 127.0.0.1
    [InlineData("2002:a00:1::1")]      // 6to4 of 10.0.0.1
    [InlineData("2001:db8::1")]        // documentation
    public void Non_public_addresses_are_blocked(string address)
    {
        var ip = IPAddress.Parse(address);

        NetworkAddressPolicy.IsBlocked(ip).ShouldBeTrue();
        NetworkAddressPolicy.IsPublic(ip).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]     // just below 172.16/12
    [InlineData("172.32.0.1")]         // just above 172.16/12
    [InlineData("100.63.255.255")]     // just below CGNAT
    [InlineData("100.128.0.1")]        // just above CGNAT
    [InlineData("2606:4700::")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:80b::200e")]
    [InlineData("::ffff:1.1.1.1")]     // IPv4-mapped public address
    [InlineData("64:ff9b::101:101")]   // NAT64 of 1.1.1.1
    [InlineData("2002:101:101::1")]    // 6to4 of 1.1.1.1
    public void Public_addresses_are_allowed(string address)
    {
        var ip = IPAddress.Parse(address);

        NetworkAddressPolicy.IsPublic(ip).ShouldBeTrue();
        NetworkAddressPolicy.IsBlocked(ip).ShouldBeFalse();
    }
}
