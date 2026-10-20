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

        // --- posts: each unit's (deputy's) in its order, and those directly in a company; one holder each
        group.MapGet("/unit-positions", (Guid? unitId, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListUnitPositionsAsync(tenant, unitId, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        group.MapPost("/units/{unitId:guid}/positions", (Guid unitId, AddUnitPositionRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.AddUnitPositionAsync(tenant, unitId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapPost("/companies/{companyId:guid}/positions", (Guid companyId, AddUnitPositionRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.AddCompanyPositionAsync(tenant, companyId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapPut("/unit-positions/{id:guid}", (Guid id, UpdateUnitPositionRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateUnitPositionAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapPut("/unit-positions/{id:guid}/parent", (Guid id, SetPostParentRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetPostParentAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapPut("/units/{unitId:guid}/positions/order", (Guid unitId, OrderUnitPositionsRequest request, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.OrderUnitPositionsAsync(tenant, unitId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapDelete("/unit-positions/{id:guid}", (Guid id, ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.RemoveUnitPositionAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        // --- the substitution inbox (tasks given to posts): vacant posts' tasks and who stands in for them.
        // Signed-in users only; the service shows each post to the managers above it and to ManageSubstitution holders.
        group.MapGet("/substitution", (ICurrentUserContext user, ISubstitutionService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetInboxAsync(tenant, ct)))
            .RequireAuthorization();

        group.MapPut("/substitution/{postId:guid}", (Guid postId, SetActingRequest request, ICurrentUserContext user, ISubstitutionService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetActingAsync(tenant, postId, request, ct)))
            .RequireAuthorization();

        group.MapDelete("/substitution/{postId:guid}", (Guid postId, ICurrentUserContext user, ISubstitutionService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ClearActingAsync(tenant, postId, ct)))
            .RequireAuthorization();

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

        // People of the organization with no place in the structure yet (to place them).
        group.MapGet("/unplaced-users", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListUnplacedUsersAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        // Work teams whose builder is in no unit of a company (the chart shows the rest under units).
        group.MapGet("/teams-outside-units", (ICurrentUserContext user, IOrgStructureService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListTeamsOutsideUnitsAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        // --- titles before people's names, managed with the users (see OrgStructurePermissions)
        group.MapGet("/titles", (ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesReadPolicy);

        group.MapPost("/titles", (CreateHonorificRequest request, ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesManagePermission);

        group.MapPut("/titles/{id:guid}", (Guid id, UpdateHonorificRequest request, ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesManagePermission);

        group.MapDelete("/titles/{id:guid}", (Guid id, ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesManagePermission);

        group.MapGet("/users/{userId:guid}/title", (Guid userId, ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetUserTitleAsync(tenant, userId, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesReadPolicy);

        group.MapPut("/users/{userId:guid}/title", (Guid userId, SetUserTitleRequest request, ICurrentUserContext user, IHonorificService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetUserTitleAsync(tenant, userId, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.TitlesAssignPolicy);

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
