namespace NexusCore.Application.Identity.Sessions;

/// <summary>
/// Turns a User-Agent into the short label people recognize ("Chrome on Windows") and an address
/// into the range a "new network" is judged by. Deliberately small: it is for display and for
/// spotting a first-time device, never for a security decision.
/// </summary>
public static class DeviceLabel
{
    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown device";
        }

        var ua = userAgent;
        // Order matters: Edge and Opera also say Chrome, Chrome also says Safari.
        var browser =
            ua.Contains("Edg/", StringComparison.Ordinal) || ua.Contains("EdgA/", StringComparison.Ordinal) ? "Edge" :
            ua.Contains("OPR/", StringComparison.Ordinal) || ua.Contains("Opera", StringComparison.Ordinal) ? "Opera" :
            ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox" :
            ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) ? "Chrome" :
            ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari" :
            ua.Contains("MSIE", StringComparison.Ordinal) || ua.Contains("Trident/", StringComparison.Ordinal) ? "Internet Explorer" :
            "Browser";

        var os =
            ua.Contains("Windows", StringComparison.Ordinal) ? "Windows" :
            ua.Contains("Android", StringComparison.Ordinal) ? "Android" :
            ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) ? "iOS" :
            ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal) ? "macOS" :
            ua.Contains("CrOS", StringComparison.Ordinal) ? "ChromeOS" :
            ua.Contains("Linux", StringComparison.Ordinal) ? "Linux" :
            "Unknown system";

        return $"{browser} on {os}";
    }

    /// <summary>The network a "new place" is judged by: /24 for IPv4, /64 for IPv6; null when unknown.</summary>
    public static string? AddressRange(string? ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address))
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24"
            : string.Join(':', Enumerable.Range(0, 4).Select(i => (bytes[i * 2] << 8 | bytes[i * 2 + 1]).ToString("x"))) + "::/64";
    }
}
