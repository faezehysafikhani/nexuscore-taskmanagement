using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Security.RateLimiting;

namespace NexusCore.Tests.Identity;

/// <summary>
/// The address rate limits are counted by: the connection's own, unless the request came from a
/// proxy the installation lists - then the address that proxy reports.
/// </summary>
public sealed class ClientAddressTests
{
    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NexusCore:RateLimiting:Auth:PermitLimit"] = "2",
            ["NexusCore:RateLimiting:Auth:WindowMinutes"] = "5",
        }.Concat(settings).ToDictionary(pair => pair.Key, pair => pair.Value));
        builder.Services.AddNexusCoreRateLimiting(builder.Configuration);
        builder.Services.AddNexusCoreForwardedHeaders(builder.Configuration);

        var app = builder.Build();
        // The test server has no network: the connection's address is whatever the test says.
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Connection"].FirstOrDefault() ?? "203.0.113.9");
            return next();
        });
        app.UseNexusCoreForwardedHeaders();
        app.UseNexusCoreRateLimiting();
        app.MapGet("/who", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString());
        app.MapGet("/limited", () => "ok").RequireRateLimiting(NexusRateLimitPolicies.Auth);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string path, string connection, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Connection", connection);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task AHeaderSentStraightToTheHost_CannotChooseTheRateLimitPartition()
    {
        await using var app = await StartAsync([]);
        using var client = app.GetTestClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add(await GetAsync(client, "/limited", "203.0.113.9", $"198.51.100.{i + 1}"));
        }

        // A new "address" in every request would have escaped the limit; it did not.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task TheAddressAListedProxyReports_IsTheOneUsed()
    {
        await using var app = await StartAsync(new() { ["NexusCore:ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });
        using var client = app.GetTestClient();

        // From the proxy: two different users behind it have their own quotas.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/limited", "10.0.0.5", "198.51.100.1"));
            if (i == 1) break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(client, "/limited", "10.0.0.5", "198.51.100.1"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/limited", "10.0.0.5", "198.51.100.2"));

        // From anywhere else the header is ignored, even though a proxy is configured.
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/limited", "203.0.113.50", "198.51.100.77"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/limited", "203.0.113.50", "198.51.100.78"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(client, "/limited", "203.0.113.50", "198.51.100.79"));
    }

    [Fact]
    public async Task TheForwardedAddress_ReplacesTheConnectionsOnlyForAListedProxy()
    {
        await using var app = await StartAsync(new() { ["NexusCore:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24" });
        using var client = app.GetTestClient();

        async Task<string> WhoAsync(string connection, string forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/who");
            request.Headers.Add("X-Test-Connection", connection);
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        }

        Assert.Equal("198.51.100.1", await WhoAsync("10.0.0.77", "198.51.100.1"));
        Assert.Equal("203.0.113.50", await WhoAsync("203.0.113.50", "198.51.100.1"));
    }
}
