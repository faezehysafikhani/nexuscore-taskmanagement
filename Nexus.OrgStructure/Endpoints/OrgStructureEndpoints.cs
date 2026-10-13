using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Endpoints;

/// <summary>
/// Companies, the units each one owns, positions, and each user's company / unit / position /
/// manager - always within the caller's own tenant. Everyone signed in may read their own place
/// (/me); the rest needs this module's permissions.
/// </summary>
public static class OrgStructureEndpoints
{
    public static IEndpointRouteBuilder MapOrgStructureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/org-structure").WithTags("Organization structure").RequireAuthorization();

        // --- companies
        group.MapGet("/companies", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListCompaniesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapGet("/companies/{id:guid}", (Guid id, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetCompanyAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapPost("/companies", (CreateCompanyRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateCompanyAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        group.MapPut("/companies/{id:guid}", (Guid id, UpdateCompanyRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateCompanyAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        group.MapDelete("/companies/{id:guid}", (Guid id, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteCompanyAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        // --- which company each unit (deputy) belongs to
        group.MapGet("/company-units", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListCompanyUnitsAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapPut("/company-units/{unitId:guid}", (Guid unitId, LinkUnitRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.LinkUnitAsync(tenant, unitId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        group.MapDelete("/company-units/{unitId:guid}", (Guid unitId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UnlinkUnitAsync(tenant, unitId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        // --- positions
        group.MapGet("/positions", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListPositionsAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapPost("/positions", (CreatePositionRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreatePositionAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapPut("/positions/{id:guid}", (Guid id, UpdatePositionRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdatePositionAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapDelete("/positions/{id:guid}", (Guid id, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeletePositionAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        // --- each user's company / unit / position / manager
        group.MapGet("/assignments", (Guid? companyId, Guid? unitId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListAssignmentsAsync(tenant, companyId, unitId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapGet("/assignments/{userId:guid}", (Guid userId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetAssignmentAsync(tenant, userId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapPut("/assignments/{userId:guid}", (Guid userId, SetUserAssignmentRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetAssignmentAsync(tenant, userId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageAssignments);

        group.MapDelete("/assignments/{userId:guid}", (Guid userId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.RemoveAssignmentAsync(tenant, userId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageAssignments);

        // --- hierarchy: a user's manager and subordinates (their own, or anyone's with the read permission)
        group.MapGet("/users/{userId:guid}/manager", (Guid userId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                SelfOrReader(user, userId) ?? InTenant(user, tenant => service.GetManagerAsync(tenant, userId, ct)));

        group.MapGet("/users/{userId:guid}/subordinates", (Guid userId, bool? all, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                SelfOrReader(user, userId) ?? InTenant(user, tenant => service.GetSubordinatesAsync(tenant, userId, all ?? false, ct)));

        group.MapGet("/me", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                user.UserId is { } me ? InTenant(user, tenant => service.GetPlacementAsync(tenant, me, ct)) : Task.FromResult(Results.Unauthorized()));

        group.MapGet("/me/subordinates", (bool? all, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                user.UserId is { } me ? InTenant(user, tenant => service.GetSubordinatesAsync(tenant, me, all ?? false, ct)) : Task.FromResult(Results.Unauthorized()));

        group.MapGet("/chart", (Guid? companyId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetChartAsync(tenant, companyId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        return app;
    }

    private static async Task<IResult> InTenant<T>(ICurrentUserContext user, Func<Guid, Task<NexusCore.SharedKernel.Results.Result<T>>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();

    private static async Task<IResult> InTenant(ICurrentUserContext user, Func<Guid, Task<NexusCore.SharedKernel.Results.Result>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();

    /// <summary>Null when the caller may read this user's place: it is themselves, or they hold a read permission.</summary>
    private static Task<IResult>? SelfOrReader(ICurrentUserContext user, Guid userId) =>
        user.UserId == userId || OrgStructurePermissions.All.Any(p => user.HasPermission(p.Name))
            ? null
            : Task.FromResult(Results.Forbid());
}
