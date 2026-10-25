using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexusCore.Application.Security.RateLimiting;

/// <summary>
/// NexusCore:ForwardedHeaders. Behind a reverse proxy (IIS ARR, nginx) the connection's remote
/// address is the proxy's, so every user would share one rate-limit quota and the audit log
/// would record the proxy. The proxy tells the real address in X-Forwarded-For, but that header
/// may only be believed when the request really came from the proxy.
///
/// Only the proxies listed here are trusted. With neither list set, ASP.NET trusts loopback only,
/// so a header sent from anywhere else is ignored and the connection's own address is used.
/// </summary>
public sealed class NexusForwardedHeadersOptions
{
    public const string SectionName = "NexusCore:ForwardedHeaders";

    /// <summary>Addresses of the reverse proxies in front of this host, e.g. "10.0.0.5".</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks of the reverse proxies in CIDR form, e.g. "10.0.0.0/24".</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxies are chained in front of the host.</summary>
    public int ForwardLimit { get; set; } = 1;
}

public static class NexusForwardedHeadersExtensions
{
    public static IServiceCollection AddNexusCoreForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configured =
            configuration.GetSection(NexusForwardedHeadersOptions.SectionName).Get<NexusForwardedHeadersOptions>()
            ?? new NexusForwardedHeadersOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = Math.Max(1, configured.ForwardLimit);

            // Replace the loopback-only default with exactly what is configured.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var proxy in configured.KnownProxies.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));
            }

            foreach (var network in configured.KnownNetworks.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var parts = network.Trim().Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
                {
                    throw new InvalidOperationException($"{NexusForwardedHeadersOptions.SectionName}:KnownNetworks has an invalid entry '{network}'. Use CIDR form, e.g. 10.0.0.0/24.");
                }

                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
            }

            // Nothing configured: trust loopback only (the ASP.NET default), never "everyone".
            if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
            {
                options.KnownProxies.Add(IPAddress.Loopback);
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
            }
        });

        return services;
    }

    /// <summary>First in the pipeline, so everything after it (rate limiting, audit, logging) sees the real address.</summary>
    public static IApplicationBuilder UseNexusCoreForwardedHeaders(this IApplicationBuilder app) =>
        app.UseForwardedHeaders();
}
