using System.Net;
using System.Net.Sockets;

namespace api.Firmware
{
    /// <summary>Blocks HttpFirmwareFetcher from fetching a private/loopback address, since an admin-configured Custom repository URL is otherwise trusted outright.</summary>
    public static class SsrfGuard
    {
        public static async Task EnsureAllowedAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new SsrfBlockedException($"'{uri.Scheme}' is not allowed - only https is.");
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                throw new SsrfBlockedException($"could not resolve host '{uri.Host}'.", ex);
            }

            // Every resolved address is checked, not just the first - DNS can return a public
            // address alongside a private one, and .NET doesn't guarantee which one a later actual
            // connect attempt picks.
            if (addresses.Length == 0 || Array.Exists(addresses, IsPrivateOrReserved))
            {
                throw new SsrfBlockedException($"'{uri.Host}' resolves to a private/reserved address.");
            }
        }

        /// <summary>RFC 1918/5735/4193 etc. ranges plus loopback/link-local/multicast - internal, not
        /// just "obviously internal-looking hostnames", since a public hostname can still resolve to
        /// one of these (DNS rebinding, or simply a misconfigured record).</summary>
        internal static bool IsPrivateOrReserved(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                return b[0] == 10                                          // 10.0.0.0/8
                    || (b[0] == 172 && b[1] is >= 16 and <= 31)             // 172.16.0.0/12
                    || (b[0] == 192 && b[1] == 168)                        // 192.168.0.0/16
                    || (b[0] == 169 && b[1] == 254)                        // 169.254.0.0/16 link-local
                    || b[0] == 0                                           // 0.0.0.0/8
                    || (b[0] == 100 && b[1] is >= 64 and <= 127)           // 100.64.0.0/10 CGNAT
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)             // 192.0.0.0/24 IETF protocol assignments
                    || (b[0] == 192 && b[1] == 0 && b[2] == 2)             // 192.0.2.0/24 TEST-NET-1
                    || (b[0] == 198 && b[1] is 18 or 19)                   // 198.18.0.0/15 benchmarking
                    || (b[0] == 198 && b[1] == 51 && b[2] == 100)          // 198.51.100.0/24 TEST-NET-2
                    || (b[0] == 203 && b[1] == 0 && b[2] == 113)           // 203.0.113.0/24 TEST-NET-3
                    || b[0] >= 224;                                        // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                {
                    return true;
                }
                byte[] b = address.GetAddressBytes();
                return (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
            }

            return false;
        }
    }

    /// <summary>Thrown by SsrfGuard - deliberately distinct from HttpRequestException/generic
    /// exceptions so a caller (FirmwareApiController) can tell "this URL was rejected as unsafe"
    /// apart from "the remote server errored" and surface an admin-actionable message instead of a
    /// generic 500.</summary>
    public sealed class SsrfBlockedException : Exception
    {
        public SsrfBlockedException(string reason) : base($"Blocked outbound request: {reason}") { }
        public SsrfBlockedException(string reason, Exception innerException) : base($"Blocked outbound request: {reason}", innerException) { }
    }
}
