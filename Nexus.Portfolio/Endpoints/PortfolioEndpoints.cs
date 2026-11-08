using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;
using Nexus.Portfolio.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Portfolio.Endpoints;

public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio").WithTags("Portfolio").RequireAuthorization(PortfolioPermissions.View);

        group.MapGet("/", async (
                Guid? organizationUnitId, string? status, string? search, Guid? involvedUserId, string? type, string? approvalStatus, string? priority,
                ICurrentUserContext currentUser, IAuthorizationService authorizationService, HttpContext httpContext,
                IPortfolioService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var viewAllAuthorization = await authorizationService.AuthorizeAsync(httpContext.User, PortfolioPermissions.ViewAll);
                var query = new PortfolioQuery(currentUser.TenantId.Value, currentUser.UserId.Value, viewAllAuthorization.Succeeded, organizationUnitId, status,
                    search, involvedUserId, type, approvalStatus, priority);
                return (await service.GetPortfolioAsync(query, cancellationToken)).ToApiResult();
            });

        // The projects the caller may see, in full detail (what the project list screens need), instead of the portfolio's summary items.
        group.MapGet("/projects", async (
                Guid? organizationUnitId, string? status, string? search, Guid? involvedUserId, string? type, string? approvalStatus, bool? includeArchived,
                ICurrentUserContext currentUser, IAuthorizationService authorizationService, HttpContext httpContext,
                IPortfolioService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null || currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                var viewAllAuthorization = await authorizationService.AuthorizeAsync(httpContext.User, PortfolioPermissions.ViewAll);
                var query = new PortfolioQuery(currentUser.TenantId.Value, currentUser.UserId.Value, viewAllAuthorization.Succeeded, organizationUnitId, status,
                    search, involvedUserId, type, approvalStatus);
                return (await service.GetVisibleProjectsAsync(query, includeArchived ?? false, cancellationToken)).ToApiResult();
            });

        return app;
    }
}
