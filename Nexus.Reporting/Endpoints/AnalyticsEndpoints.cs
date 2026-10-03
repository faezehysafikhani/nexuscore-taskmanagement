using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.Reporting.Application.Analytics;
using Nexus.Reporting.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Reporting.Endpoints;

public static class AnalyticsEndpoints
{
    /// <summary>Mapped by <see cref="DashboardEndpoints.MapDashboardEndpoints"/>, so a host that already maps
    /// the dashboards picks these up without a new call. Every report is tenant-wide, so each one needs
    /// <c>Reporting.ViewAll</c> (checked here, like the summary) and reads the caller's own tenant.</summary>
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting").WithTags("Reporting Analytics").RequireAuthorization(ReportingPermissions.View);

        group.MapGet("/projects/{projectId:guid}/performance", (
                Guid projectId, DateOnly? asOf, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IAnalyticsService service, CancellationToken cancellationToken) =>
            Run(currentUser, authorization, httpContext, async tenantId =>
                (await service.GetProjectPerformanceAsync(tenantId, projectId, asOf, cancellationToken)).ToApiResult()));

        group.MapGet("/units/performance", (
                int? level, DateOnly? asOf, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IAnalyticsService service, CancellationToken cancellationToken) =>
            Run(currentUser, authorization, httpContext, async tenantId =>
                (await service.GetUnitPerformanceAsync(tenantId, level ?? 2, asOf, cancellationToken)).ToApiResult()));

        group.MapGet("/units/status-matrix", (
                int? level, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IAnalyticsService service, CancellationToken cancellationToken) =>
            Run(currentUser, authorization, httpContext, async tenantId =>
                (await service.GetUnitStatusMatrixAsync(tenantId, level ?? 2, cancellationToken)).ToApiResult()));

        group.MapGet("/project-managers/evaluation", (
                DateOnly? asOf, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IAnalyticsService service, CancellationToken cancellationToken) =>
            Run(currentUser, authorization, httpContext, async tenantId =>
                (await service.GetProjectManagerEvaluationAsync(tenantId, asOf, cancellationToken)).ToApiResult()));

        group.MapGet("/strategy-alignment-matrix", (
                ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IAnalyticsService service, CancellationToken cancellationToken) =>
            Run(currentUser, authorization, httpContext, async tenantId =>
                (await service.GetStrategyAlignmentMatrixAsync(tenantId, cancellationToken)).ToApiResult()));

        return app;
    }

    private static async Task<IResult> Run(
        ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext, Func<Guid, Task<IResult>> report)
    {
        if (currentUser.TenantId is null)
        {
            return Results.Unauthorized();
        }

        var viewAll = await authorization.AuthorizeAsync(httpContext.User, ReportingPermissions.ViewAll);
        return viewAll.Succeeded ? await report(currentUser.TenantId.Value) : Results.Forbid();
    }
}
