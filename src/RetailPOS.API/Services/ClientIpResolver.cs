using System.Net;
using System.Net.Sockets;

namespace RetailPOS.API.Services;

/// <summary>
/// Default implementation of <see cref="IClientIpResolver"/>.
///
/// IP resolution priority (first non-null wins):
///   1. CF-Connecting-IP      — Cloudflare sets this to the true client IP.
///   2. X-Forwarded-For       — First PUBLIC IP in the comma-list (skips private/loopback IPs
///                              added by internal hops). Falls back to the first entry if all
///                              are private (e.g. all traffic is internal).
///   3. X-Real-IP             — Single-value header set by Nginx/IIS.
///   4. HttpContext.Connection.RemoteIpAddress — Already normalized by ForwardedHeaders middleware.
///
/// SECURITY NOTE — Trusting forwarded headers:
///   Forwarded headers can be spoofed by clients unless you explicitly list the IP ranges of
///   your trusted proxies. Configure <see cref="ForwardedHeadersOptions.KnownProxies"/> or
///   <see cref="ForwardedHeadersOptions.KnownNetworks"/> in Program.cs with the real IP(s) of
///   your Nginx/Cloudflare/load-balancer nodes. The UseForwardedHeaders() middleware will then
///   only honour headers from those sources and rewrite RemoteIpAddress accordingly.
///   In production behind Cloudflare, add Cloudflare's published IP ranges to KnownNetworks.
/// </summary>
public sealed class ClientIpResolver : IClientIpResolver
{
    public ClientIpResolution Resolve(HttpContext context)
    {
        var rawIp = context.Connection.RemoteIpAddress?.ToString();

        string? detectedIp = null;

        // 1. Cloudflare: CF-Connecting-IP is set by Cloudflare to the real browser IP.
        var cf = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()?.Trim();
        if (!string.IsNullOrEmpty(cf) && IPAddress.TryParse(cf, out _))
        {
            detectedIp = cf;
        }

        // 2. X-Forwarded-For: "client, proxy1, proxy2"
        //    Walk left-to-right and take the first IP that is not private/loopback.
        //    If every IP is private (all-internal deployment), fall back to the leftmost entry.
        if (detectedIp is null)
        {
            var xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(xff))
            {
                var parts = xff.Split(',');
                string? firstEntry = null;

                foreach (var raw in parts)
                {
                    var candidate = raw.Trim();
                    if (!IPAddress.TryParse(candidate, out var parsed)) continue;

                    firstEntry ??= candidate;

                    if (!IsPrivateOrLoopback(parsed))
                    {
                        detectedIp = candidate;
                        break;
                    }
                }

                // All hops were private — take the leftmost as the best available IP.
                detectedIp ??= firstEntry;
            }
        }

        // 3. X-Real-IP: single value set by Nginx proxy_set_header X-Real-IP $remote_addr.
        if (detectedIp is null)
        {
            var xri = context.Request.Headers["X-Real-IP"].FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(xri) && IPAddress.TryParse(xri, out _))
            {
                detectedIp = xri;
            }
        }

        // 4. Connection IP (already rewritten by UseForwardedHeaders() in Program.cs).
        detectedIp ??= rawIp;

        var isLocal = IsLoopback(detectedIp);

        // In local dev, resolve the LAN IP of this machine so the audit log shows something
        // more useful than "::1". This is never stored in production because isLocal = false.
        string? localMachineIp = isLocal ? TryGetLanIp() : null;

        return new ClientIpResolution(
            RawIp: rawIp,
            DetectedIp: detectedIp,
            LocalMachineIp: localMachineIp,
            IsLocalRequest: isLocal
        );
    }

    private static bool IsLoopback(string? ip) =>
        ip is "::1" or "127.0.0.1";

    /// <summary>
    /// Returns true for loopback, link-local, and RFC-1918 private addresses.
    /// These should not be treated as "real" client IPs in X-Forwarded-For.
    /// </summary>
    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        // Unwrap IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1).
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;

        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var b = ip.GetAddressBytes();
        return
            b[0] == 10                                          // 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)       // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254);                   // 169.254.0.0/16 link-local
    }

    /// <summary>
    /// Best-effort: finds the first LAN IPv4 of this machine.
    /// Returns null if resolution fails (never throws).
    /// </summary>
    private static string? TryGetLanIp()
    {
        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var addr in hostEntry.AddressList)
            {
                if (addr.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = addr.GetAddressBytes();
                if ((b[0] == 192 && b[1] == 168)
                    || b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31))
                {
                    return addr.ToString();
                }
            }
        }
        catch
        {
            // DNS lookup failure is non-fatal; audit logging must not break the request.
        }

        return null;
    }
}
