using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Security.RateLimiting;

namespace NexusCore.Tests.Identity;

/// <summary>The endpoint browsers send Content-Security-Policy violation reports to.</summary>
public sealed class SecurityReportTests
{
    private sealed class Capture : ILoggerProvider
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(Lines);
        public void Dispose() { }

        private sealed class Logger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add(formatter(state, exception));
        }
    }

    private static async Task<(WebApplication App, Capture Log)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        var log = new Capture();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Services.AddNexusCoreRateLimiting(builder.Configuration);
        var app = builder.Build();
        app.UseNexusCoreRateLimiting();
        app.MapSecurityReportEndpoints();
        await app.StartAsync();
        return (app, log);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/csp-report");

    [Fact]
    public async Task AReport_IsAnsweredWithNothing_AndLoggedWithoutControlCharacters()
    {
        var (app, log) = await StartAsync();
        await using var _ = app;
        using var client = app.GetTestClient();

        var response = await client.PostAsync("/api/platform/csp-report", Json(
            "{\"csp-report\":{\"document-uri\":\"https://app.local/\",\"effective-directive\":\"script-src-elem\",\"blocked-uri\":\"https://evil.example/x.js\\r\\nFAKE LOG LINE\",\"disposition\":\"report\"}}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var line = Assert.Single(log.Lines, l => l.StartsWith("CSP violation"));
        Assert.Contains("script-src-elem", line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public async Task AHugeBody_IsRefused_AndGarbageIsIgnored()
    {
        var (app, _) = await StartAsync();
        await using var _a = app;
        using var client = app.GetTestClient();

        var huge = await client.PostAsync("/api/platform/csp-report", Json("{\"csp-report\":{\"x\":\"" + new string('a', 20_000) + "\"}}"));
        var garbage = await client.PostAsync("/api/platform/csp-report", Json("not json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, garbage.StatusCode);
    }
}
