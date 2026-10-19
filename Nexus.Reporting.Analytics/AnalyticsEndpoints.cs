using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Contracts.Permissions;
using Nexus.Reporting.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Reporting.Analytics;

public static class AnalyticsEndpoints
{
    /// <summary>Opt-in (see AddReportingAnalytics). Maps next to the dashboards under the same
    /// /api/reporting prefix. The reports are tenant-wide, so each one needs <c>Reporting.ViewAll</c>
    /// (checked here, like the summary) and reads the caller's own tenant. The two reports about ONE project
    /// (performance and cost curve) are also open to whoever may see that project - the same rule as the portfolio -
    /// provided they may read contracts, because the actual cost is made of invoices.</summary>
    public static IEndpointRouteBuilder MapReportingAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting").WithTags("Reporting Analytics").RequireAuthorization(ReportingPermissions.View);

        group.MapGet("/projects/{projectId:guid}/performance", (
                Guid projectId, DateOnly? asOf, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IProjectViewAccess access, IAnalyticsService service, CancellationToken cancellationToken) =>
            RunForProject(projectId, currentUser, authorization, httpContext, access, async tenantId =>
                (await service.GetProjectPerformanceAsync(tenantId, projectId, asOf, cancellationToken)).ToApiResult(), cancellationToken));

        group.MapGet("/projects/{projectId:guid}/cost-curve", (
                Guid projectId, int? stepDays, ICurrentUserContext currentUser, IAuthorizationService authorization,
                HttpContext httpContext, IProjectViewAccess access, IAnalyticsService service, CancellationToken cancellationToken) =>
            RunForProject(projectId, currentUser, authorization, httpContext, access, async tenantId =>
                (await service.GetProjectCostCurveAsync(tenantId, projectId, stepDays, cancellationToken)).ToApiResult(), cancellationToken));

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

    /// <summary>
    /// ViewAll sees every project. Anyone else sees the projects the portfolio would show them (their own team's), and
    /// only with Contracts.View, since the figures include what has been invoiced and paid. Every other case is Forbid.
    /// </summary>
    internal static async Task<IResult> RunForProject(
        Guid projectId, ICurrentUserContext currentUser, IAuthorizationService authorization, HttpContext httpContext,
        IProjectViewAccess access, Func<Guid, Task<IResult>> report, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var tenantId = currentUser.TenantId.Value;
        if ((await authorization.AuthorizeAsync(httpContext.User, ReportingPermissions.ViewAll)).Succeeded)
        {
            return await report(tenantId);
        }

        var mayReadContracts = (await authorization.AuthorizeAsync(httpContext.User, ContractPermissions.View)).Succeeded;
        return mayReadContracts && await access.CanViewAsync(tenantId, currentUser.UserId.Value, projectId, cancellationToken)
            ? await report(tenantId)
            : Results.Forbid();
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
