namespace RetailPOS.Infrastructure.Audit;

public static class UserAgentParser
{
    public static (string? device, string? browser, string? os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return (null, null, null);

        var ua = userAgent;
        var lower = ua.ToLowerInvariant();

        var browser = DetectBrowser(lower);
        var os = DetectOs(lower);
        var device = DetectDevice(lower);

        return (device, browser, os);
    }

    private static string? DetectBrowser(string ua)
    {
        if (ua.Contains("edg/")) return "Edge";
        if (ua.Contains("opr/") || ua.Contains("opera")) return "Opera";
        if (ua.Contains("chrome/") && !ua.Contains("edg/") && !ua.Contains("opr/")) return "Chrome";
        if (ua.Contains("firefox/")) return "Firefox";
        if (ua.Contains("safari/") && !ua.Contains("chrome/")) return "Safari";
        if (ua.Contains("msie") || ua.Contains("trident/")) return "Internet Explorer";
        if (ua.Contains("postman")) return "Postman";
        if (ua.Contains("curl/")) return "curl";
        return null;
    }

    private static string? DetectOs(string ua)
    {
        if (ua.Contains("windows nt 10")) return "Windows 10/11";
        if (ua.Contains("windows nt")) return "Windows";
        if (ua.Contains("mac os x") || ua.Contains("macintosh")) return "macOS";
        if (ua.Contains("android")) return "Android";
        if (ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ios")) return "iOS";
        if (ua.Contains("linux")) return "Linux";
        return null;
    }

    private static string? DetectDevice(string ua)
    {
        if (ua.Contains("ipad")) return "iPad";
        if (ua.Contains("iphone")) return "iPhone";
        if (ua.Contains("android") && ua.Contains("mobile")) return "Android Phone";
        if (ua.Contains("android")) return "Android Tablet";
        if (ua.Contains("mobile")) return "Mobile";
        return "Desktop";
    }
}
