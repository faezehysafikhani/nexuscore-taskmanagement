using Nexus.ProjectManagement.Kpi.Application.Dtos;
using Nexus.ProjectManagement.Kpi.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Kpi.Application;

public sealed class KpiMeasurementService(
    IKpiRepository kpis,
    IKpiMeasurementRepository measurements,
    IKpiUnitOfWork unitOfWork) : IKpiMeasurementService
{
    public async Task<Result<IReadOnlyList<KpiMeasurementDto>>> ListAsync(Guid kpiDefinitionId, CancellationToken cancellationToken)
    {
        if (await kpis.GetByIdAsync(kpiDefinitionId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<KpiMeasurementDto>>(Error.NotFound("KPI not found."));
        }

        var list = await measurements.ListByKpiAsync(kpiDefinitionId, cancellationToken);
        return Result.Success<IReadOnlyList<KpiMeasurementDto>>(list.Select(ToDto).ToList());
    }

    public async Task<Result<KpiMeasurementDto>> RecordAsync(Guid kpiDefinitionId, RecordKpiMeasurementRequest request, CancellationToken cancellationToken)
    {
        var kpi = await kpis.GetByIdAsync(kpiDefinitionId, cancellationToken);
        if (kpi is null)
        {
            return Result.Failure<KpiMeasurementDto>(Error.NotFound("KPI not found."));
        }

        // Tenant and project come from the KPI itself, never from the caller.
        var measurement = new KpiMeasurement(Guid.NewGuid(), kpi.TenantId, kpi.ProjectId, kpi.Id, request.MeasuredOn, request.Value, request.Note);
        await measurements.AddAsync(measurement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(measurement));
    }

    public async Task<Result<KpiMeasurementDto>> UpdateAsync(Guid kpiDefinitionId, Guid id, UpdateKpiMeasurementRequest request, CancellationToken cancellationToken)
    {
        var measurement = await measurements.GetByIdAsync(id, cancellationToken);
        if (measurement is null || measurement.KpiDefinitionId != kpiDefinitionId)
        {
            return Result.Failure<KpiMeasurementDto>(Error.NotFound("Measurement not found."));
        }

        measurement.Update(request.MeasuredOn, request.Value, request.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(measurement));
    }

    public async Task<Result> DeleteAsync(Guid kpiDefinitionId, Guid id, CancellationToken cancellationToken)
    {
        var measurement = await measurements.GetByIdAsync(id, cancellationToken);
        if (measurement is null || measurement.KpiDefinitionId != kpiDefinitionId)
        {
            return Result.Failure(Error.NotFound("Measurement not found."));
        }

        await measurements.RemoveAsync(measurement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static KpiMeasurementDto ToDto(KpiMeasurement m) => new(
        m.Id, m.KpiDefinitionId, m.ProjectId, m.MeasuredOn, m.Value, m.Note, m.CreatedByUserId, m.CreatedAtUtc);
}
