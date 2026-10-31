using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Endpoints;

/// <summary>
/// The simple chart (org / unit / position) under /api/org-structure/kinds: the two kinds made when
/// missing, the whole tree in one read, the people of a position and the head of a unit. Nodes
/// themselves are made, renamed, moved and deactivated with the ordinary /nodes endpoints.
/// </summary>
public static class OrgKindEndpoints
{
    public static IEndpointRouteBuilder MapOrgKindEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/org-structure/kinds").WithTags("Organization structure: simple chart").RequireAuthorization();

        group.MapPost("/ensure", (ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.EnsureAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeCreate);

        group.MapGet("/chart", (ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetChartAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/nodes/{id:guid}/persons", (Guid id, AddNodePersonRequest request, ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.AddPersonAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeAssign);

        group.MapDelete("/nodes/{id:guid}/persons/{userId:guid}", (Guid id, Guid userId, ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.RemovePersonAsync(tenant, id, userId, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeAssign);

        group.MapPut("/nodes/{id:guid}/persons/{userId:guid}/primary", (Guid id, Guid userId, ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetPrimaryPersonAsync(tenant, id, userId, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeAssign);

        group.MapPut("/nodes/{id:guid}/head", (Guid id, SetNodeHeadRequest request, ICurrentUserContext user, IOrgKindsService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetHeadAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeAssign);

        return app;
    }

    private static async Task<IResult> InTenant<T>(ICurrentUserContext user, Func<Guid, Task<Result<T>>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();

    private static async Task<IResult> InTenant(ICurrentUserContext user, Func<Guid, Task<Result>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();
}
