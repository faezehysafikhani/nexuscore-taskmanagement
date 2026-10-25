using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Security;

/// <summary>
/// Security:OutboundAddresses - where the server may be pointed at by settings an administrator
/// can edit (the SMS gateway address, the LDAP server). Without limits those fields turn the
/// server into a request forger for the internal network and send stored API keys to any address.
/// </summary>
public sealed class OutboundAddressOptions
{
    public const string SectionName = "Security:OutboundAddresses";

    /// <summary>
    /// Host names the SMS gateway address may use besides the providers' own default hosts: an
    /// exact name ("sms.bank.local") or a suffix ("*.bank.local"). Hosts listed here are trusted
    /// even when they resolve to a private network (an in-house gateway).
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Whether a listed host may be reached over plain http. Never for the default hosts.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// When set, the LDAP server must be one of these hosts (names or addresses). Empty: any
    /// host that is not loopback, link-local (the cloud metadata address) or otherwise unroutable.
    /// </summary>
    public string[] LdapAllowedHosts { get; set; } = [];

    /// <summary>The ports an LDAP server may use, so the test button cannot scan other services.</summary>
    public int[] LdapAllowedPorts { get; set; } = [389, 636, 3268, 3269];
}

public interface IOutboundAddressPolicy
{
    /// <summary>
    /// Checks the base address of an SMS gateway: https, no credentials in the address, a host
    /// that is a provider's own or listed in AllowedHosts, and - for the provider's own - only
    /// public addresses behind it.
    /// </summary>
    Task<Result> ValidateGatewayUrlAsync(string url, IReadOnlyCollection<string> providerDefaultUrls, CancellationToken cancellationToken);

    Task<Result> ValidateLdapServerAsync(string host, int port, CancellationToken cancellationToken);

    /// <summary>Whether the SMS gateway host may resolve to a non-public address (it is listed in AllowedHosts).</summary>
    bool IsTrustedGatewayHost(string host);
}

public sealed class OutboundAddressPolicy : IOutboundAddressPolicy
{
    private readonly OutboundAddressOptions _options;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public OutboundAddressPolicy(IOptions<OutboundAddressOptions> options)
        : this(options, static (host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    public OutboundAddressPolicy(IOptions<OutboundAddressOptions> options, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        _options = options.Value;
        _resolve = resolve;
    }

    public bool IsTrustedGatewayHost(string host) =>
        _options.AllowedHosts.Any(entry => HostMatches(entry, host));

    public async Task<Result> ValidateGatewayUrlAsync(string url, IReadOnlyCollection<string> providerDefaultUrls, CancellationToken cancellationToken)
    {
        var refused = Result.Failure(Error.Validation("This address is not allowed for the SMS service. Use the provider's own address, or ask the administrator of the server to list your gateway host."));

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return refused;
        }

        var host = uri.IdnHost;
        var listed = IsTrustedGatewayHost(host);
        var providerOwn = providerDefaultUrls
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var known) ? known.IdnHost : null)
            .Any(known => known is not null && string.Equals(known, host, StringComparison.OrdinalIgnoreCase));
        if (!listed && !providerOwn)
        {
            return refused;
        }

        var secure = uri.Scheme == Uri.UriSchemeHttps;
        var plainAllowed = uri.Scheme == Uri.UriSchemeHttp && listed && _options.AllowHttp;
        if (!secure && !plainAllowed)
        {
            return Result.Failure(Error.Validation("The SMS service address must use https."));
        }

        // A name that points to the inside of the network is not the provider any more; only a
        // host the server's operator listed is trusted to be there.
        if (!listed)
        {
            return await EnsurePublicAsync(host, refused, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> ValidateLdapServerAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!_options.LdapAllowedPorts.Contains(port))
        {
            return Result.Failure(Error.Validation("This LDAP port is not allowed. Allowed: " + string.Join(", ", _options.LdapAllowedPorts) + "."));
        }

        var value = host.Trim();
        if (_options.LdapAllowedHosts.Length > 0
            && !_options.LdapAllowedHosts.Any(entry => HostMatches(entry, value)))
        {
            return Result.Failure(Error.Validation("This LDAP server is not in the list of allowed servers of this installation."));
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(value, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _resolve(value, cancellationToken);
            }
            catch (SocketException)
            {
                return Result.Failure(Error.Validation("The LDAP server name could not be resolved."));
            }
        }

        // A directory server is normally inside the network, so private ranges are fine; what is
        // never a directory server is this machine, the link-local range (cloud metadata) or an
        // address that is not a host at all.
        return addresses.Length == 0 || addresses.Any(IsNeverAServerAddress)
            ? Result.Failure(Error.Validation("This LDAP server address is not allowed."))
            : Result.Success();
    }

    private async Task<Result> EnsurePublicAsync(string host, Result refused, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _resolve(host, cancellationToken);
            }
            catch (SocketException)
            {
                return Result.Failure(Error.Validation("The SMS service address could not be resolved."));
            }
        }

        return addresses.Length == 0 || addresses.Any(address => !IsPublic(address)) ? refused : Result.Success();
    }

    private static bool HostMatches(string pattern, string host)
    {
        var p = pattern.Trim();
        if (p.Length == 0)
        {
            return false;
        }

        if (p.StartsWith("*.", StringComparison.Ordinal))
        {
            return host.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase) && host.Length > p.Length - 1;
        }

        return string.Equals(p, host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An address on the public internet: not private, loopback, link-local, multicast or reserved.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] == 0
                     || bytes[0] == 10
                     || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)       // carrier-grade NAT
                     || bytes[0] == 127
                     || (bytes[0] == 169 && bytes[1] == 254)                     // link-local, cloud metadata
                     || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                     || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                     || (bytes[0] == 192 && bytes[1] == 168)
                     || (bytes[0] == 198 && bytes[1] is 18 or 19)                // benchmarking
                     || bytes[0] >= 224);                                        // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal
                     || address.IsIPv6SiteLocal
                     || address.IsIPv6Multicast
                     || (bytes[0] & 0xFE) == 0xFC);                              // unique local fc00::/7
        }

        return false;
    }

    private static bool IsNeverAServerAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] >= 224
            : address.IsIPv6LinkLocal || address.IsIPv6Multicast;
    }
}
