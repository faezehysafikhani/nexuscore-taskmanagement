using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Reporting.Export.Application;
using Nexus.Reporting.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Reporting.Export.Endpoints;

public static class ExportEndpoints
{
    /// <summary>GET /api/reporting/exports/{report}?format=xlsx|html&amp;level=&amp;asOf= . Every report is organisation-wide, so like the
    /// analytics it needs <c>Reporting.ViewAll</c>, checked here. <c>format=html</c> is the print-ready page the browser saves as PDF.</summary>
    public static IEndpointRouteBuilder MapReportingExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/exports").WithTags("Reporting Export").RequireAuthorization(ReportingPermissions.View);

        group.MapGet("/", () => Results.Ok(ReportKeys.All));

        group.MapGet("/{report}", async (
            string report, string? format, int? level, DateOnly? asOf, ICurrentUserContext currentUser,
            IAuthorizationService authorization, HttpContext httpContext, IReportExportService service, CancellationToken cancellationToken) =>
        {
            if (currentUser.TenantId is null)
            {
                return Results.Unauthorized();
            }

            var viewAll = await authorization.AuthorizeAsync(httpContext.User, ReportingPermissions.ViewAll);
            if (!viewAll.Succeeded)
            {
                return Results.Forbid();
            }

            if (!TryParseFormat(format, out var exportFormat))
            {
                return Results.Problem("The format must be 'xlsx' or 'html'.", statusCode: StatusCodes.Status400BadRequest, title: "validation.error");
            }

            var result = await service.ExportAsync(currentUser.TenantId.Value, new ExportRequest(report, exportFormat, level ?? 2, asOf), cancellationToken);
            if (result.IsFailure)
            {
                return result.ToApiResult();
            }

            var file = result.Value!;
            httpContext.Response.Headers.XContentTypeOptions = "nosniff";
            if (exportFormat == ExportFormat.Html)
            {
                httpContext.Response.Headers.ContentSecurityPolicy = HtmlRenderer.ContentSecurityPolicy;
                return Results.Content(System.Text.Encoding.UTF8.GetString(file.Content), HtmlRenderer.ContentType);
            }

            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return app;
    }

    private static bool TryParseFormat(string? text, out ExportFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "xlsx":
                format = ExportFormat.Xlsx;
                return true;
            case "html":
                format = ExportFormat.Html;
                return true;
            default:
                format = default;
                return false;
        }
    }
}
