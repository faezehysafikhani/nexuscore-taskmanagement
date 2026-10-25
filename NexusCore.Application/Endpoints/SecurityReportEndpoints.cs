using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Security.RateLimiting;

namespace NexusCore.Application.Endpoints;

/// <summary>
/// Where browsers send Content-Security-Policy violation reports (the policy's report-uri). While
/// the policy is in report-only mode this is how an administrator learns what it would have
/// blocked, before it is switched to enforcing. Anonymous (browsers send reports without
/// credentials), small, rate limited, and only ever written to the log.
/// </summary>
public static class SecurityReportEndpoints
{
    private const int MaxBodyBytes = 8 * 1024;
    private const int MaxFieldLength = 200;

    public static IEndpointRouteBuilder MapSecurityReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/platform/csp-report", async (HttpRequest request, ILoggerFactory loggers, CancellationToken cancellationToken) =>
            {
                var logger = loggers.CreateLogger("NexusCore.SecurityReports");

                using var buffer = new MemoryStream();
                var chunk = new byte[1024];
                int read;
                while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > MaxBodyBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    buffer.Write(chunk, 0, read);
                }

                try
                {
                    using var document = JsonDocument.Parse(buffer.ToArray());
                    var report = document.RootElement.TryGetProperty("csp-report", out var wrapped) ? wrapped : document.RootElement;
                    logger.LogWarning(
                        "CSP violation: {Directive} blocked {BlockedUri} on {DocumentUri} (disposition {Disposition})",
                        Field(report, "effective-directive", "violated-directive"),
                        Field(report, "blocked-uri", "blockedURL"),
                        Field(report, "document-uri", "documentURL"),
                        Field(report, "disposition"));
                }
                catch (JsonException)
                {
                    // Not a report: nothing to keep.
                }

                return Results.NoContent();
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(NexusRateLimitPolicies.SecurityReports)
            .ExcludeFromDescription()
            .WithTags("Platform");

        return app;
    }

    /// <summary>One string of the report, cut short and without control characters, so a report cannot forge log lines.</summary>
    private static string Field(JsonElement report, params string[] names)
    {
        foreach (var name in names)
        {
            if (report.ValueKind == JsonValueKind.Object && report.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                var builder = new StringBuilder(Math.Min(text.Length, MaxFieldLength));
                foreach (var ch in text.Take(MaxFieldLength))
                {
                    builder.Append(char.IsControl(ch) ? ' ' : ch);
                }

                return builder.ToString();
            }
        }

        return "-";
    }
}
