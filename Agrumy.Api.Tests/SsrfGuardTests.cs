using System.Net;
using api.Firmware;
using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>roadmap #182: SsrfGuard is what stops an admin-configured Custom firmware repository
/// URL from being used to reach the API server's own private network. IsPrivateOrReserved is a
/// pure function, so it's tested directly; EnsureAllowedAsync's scheme check is tested without any
/// real DNS/network dependency since it short-circuits before resolving the host.</summary>
public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("10.0.0.1")]         // 10.0.0.0/8
    [InlineData("172.16.0.1")]       // 172.16.0.0/12
    [InlineData("172.31.255.255")]   // 172.16.0.0/12, top of range
    [InlineData("192.168.1.1")]      // 192.168.0.0/16
    [InlineData("169.254.1.1")]      // link-local
    [InlineData("100.64.0.1")]       // CGNAT
    [InlineData("192.0.2.1")]        // TEST-NET-1
    [InlineData("198.18.0.1")]       // benchmarking
    [InlineData("198.51.100.1")]     // TEST-NET-2
    [InlineData("203.0.113.1")]      // TEST-NET-3
    [InlineData("224.0.0.1")]        // multicast
    [InlineData("240.0.0.1")]        // reserved
    [InlineData("0.0.0.0")]
    [InlineData("::1")]              // IPv6 loopback
    [InlineData("fe80::1")]          // IPv6 link-local
    [InlineData("fc00::1")]          // IPv6 unique local
    [InlineData("fd12:3456:789a::1")] // IPv6 unique local
    public void IsPrivateOrReserved_BlocksInternalRanges(string ip)
    {
        Assert.True(SsrfGuard.IsPrivateOrReserved(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("140.82.112.3")]     // a real github.com address range
    [InlineData("172.15.255.255")]   // just below the 172.16.0.0/12 private range
    [InlineData("172.32.0.0")]       // just above the 172.16.0.0/12 private range
    [InlineData("2606:4700:4700::1111")] // Cloudflare public IPv6
    public void IsPrivateOrReserved_AllowsPublicAddresses(string ip)
    {
        Assert.False(SsrfGuard.IsPrivateOrReserved(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task EnsureAllowedAsync_RejectsNonHttpsScheme_WithoutResolvingHost()
    {
        // If this reached DNS resolution for a bogus host, it would throw a different exception
        // (or hang) - reaching SsrfBlockedException here proves the scheme check runs first.
        var ex = await Assert.ThrowsAsync<SsrfBlockedException>(
            () => SsrfGuard.EnsureAllowedAsync(new Uri("http://this-host-does-not-resolve.invalid/manifest.json"), CancellationToken.None));

        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public async Task EnsureAllowedAsync_RejectsUnresolvableHost()
    {
        var ex = await Assert.ThrowsAsync<SsrfBlockedException>(
            () => SsrfGuard.EnsureAllowedAsync(new Uri("https://this-host-does-not-resolve.invalid/manifest.json"), CancellationToken.None));

        Assert.Contains("resolve", ex.Message);
    }
}
