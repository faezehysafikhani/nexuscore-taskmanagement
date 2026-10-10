using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Reporting.Application;
using Nexus.Reporting.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Reporting.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting").WithTags("Reporting").RequireAuthorization(ReportingPermissions.View);

        group.MapGet("/summary", async (
                ICurrentUserContext currentUser, Guid? organizationUnitId,
                IAuthorizationService authorizationService, HttpContext httpContext,
                IDashboardService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                var viewAll = await authorizationService.AuthorizeAsync(httpContext.User, ReportingPermissions.ViewAll);
                if (!viewAll.Succeeded)
                {
                    return Results.Forbid();
                }

                return (await service.GetSummaryAsync(currentUser.TenantId.Value, organizationUnitId, cancellationToken)).ToApiResult();
            });

        group.MapGet("/me", async (
                ICurrentUserContext currentUser,
                IDashboardService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.GetMyDashboardAsync(currentUser.TenantId.Value, currentUser.UserId.Value, cancellationToken)).ToApiResult();
            });

        group.MapGet("/projects/{projectId:guid}", async (
                Guid projectId,
                IAuthorizationService authorizationService, HttpContext httpContext,
                IDashboardService service, CancellationToken cancellationToken) =>
            {
                var viewAll = await authorizationService.AuthorizeAsync(httpContext.User, ReportingPermissions.ViewAll);
                if (!viewAll.Succeeded)
                {
                    return Results.Forbid();
                }

                return (await service.GetProjectDashboardAsync(projectId, cancellationToken)).ToApiResult();
            });

        return app;
    }
}
