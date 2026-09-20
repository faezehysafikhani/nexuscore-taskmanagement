using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Application.Dtos;
using Nexus.ProjectManagement.Kpi.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Kpi.Endpoints;

public static class KpiEndpoints
{
    public static IEndpointRouteBuilder MapKpiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/kpis").WithTags("KPI").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, Guid? deliverableId, IKpiService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, deliverableId, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IKpiService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.View);

        group.MapPost("/", async (CreateKpiDefinitionRequest request, IKpiService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateKpiDefinitionRequest request, IKpiService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Edit);

        return app;
    }
}
