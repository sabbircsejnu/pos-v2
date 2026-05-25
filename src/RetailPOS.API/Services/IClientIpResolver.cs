namespace RetailPOS.API.Services;

/// <summary>
/// Encapsulates the result of client IP resolution from an HTTP request.
/// </summary>
/// <param name="RawIp">The raw IP address from the TCP connection before any header inspection.</param>
/// <param name="DetectedIp">
/// The best resolved client IP in priority order:
/// CF-Connecting-IP → first public IP in X-Forwarded-For → X-Real-IP → connection IP.
/// Use this as the canonical audit IP address.
/// </param>
/// <param name="LocalMachineIp">
/// The server machine's LAN IPv4 address (e.g. 192.168.x.x).
/// Populated only when <see cref="IsLocalRequest"/> is true; null in production.
/// </param>
/// <param name="IsLocalRequest">True when the detected IP is a loopback address (::1 or 127.0.0.1).</param>
public sealed record ClientIpResolution(
    string? RawIp,
    string? DetectedIp,
    string? LocalMachineIp,
    bool IsLocalRequest
);

/// <summary>
/// Resolves the true client IP address from an HTTP request, honouring trusted reverse-proxy
/// headers (Cloudflare, Nginx, IIS, Docker, load balancers) while falling back gracefully when
/// those headers are absent.
/// </summary>
public interface IClientIpResolver
{
    /// <summary>
    /// Inspects the current <paramref name="context"/> and returns full IP resolution metadata.
    /// </summary>
    ClientIpResolution Resolve(HttpContext context);
}
