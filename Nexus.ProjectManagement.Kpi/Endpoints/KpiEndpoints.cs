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

        group.MapDelete("/{id:guid}", async (Guid id, IKpiService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Delete);

        group.MapGet("/{kpiId:guid}/measurements", async (Guid kpiId, IKpiMeasurementService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(kpiId, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.View);

        group.MapPost("/{kpiId:guid}/measurements", async (Guid kpiId, RecordKpiMeasurementRequest request, IKpiMeasurementService service, CancellationToken cancellationToken) =>
                (await service.RecordAsync(kpiId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Record);

        group.MapPut("/{kpiId:guid}/measurements/{id:guid}", async (Guid kpiId, Guid id, UpdateKpiMeasurementRequest request, IKpiMeasurementService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(kpiId, id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Record);

        group.MapDelete("/{kpiId:guid}/measurements/{id:guid}", async (Guid kpiId, Guid id, IKpiMeasurementService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(kpiId, id, cancellationToken)).ToApiResult())
            .RequireAuthorization(KpiPermissions.Record);

        return app;
    }
}
