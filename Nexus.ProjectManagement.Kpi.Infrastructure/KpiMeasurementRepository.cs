using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Infrastructure;

public sealed class KpiMeasurementRepository(KpiDbContext dbContext) : IKpiMeasurementRepository
{
    public Task<KpiMeasurement?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.KpiMeasurements.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KpiMeasurement>> ListByKpiAsync(Guid kpiDefinitionId, CancellationToken cancellationToken) =>
        await dbContext.KpiMeasurements
            .Where(m => m.KpiDefinitionId == kpiDefinitionId)
            .OrderBy(m => m.MeasuredOn).ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KpiMeasurement>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.KpiMeasurements
            .Where(m => projectIds.Contains(m.ProjectId))
            .OrderBy(m => m.MeasuredOn).ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(KpiMeasurement measurement, CancellationToken cancellationToken) =>
        await dbContext.KpiMeasurements.AddAsync(measurement, cancellationToken);

    public Task RemoveAsync(KpiMeasurement measurement, CancellationToken cancellationToken)
    {
        dbContext.KpiMeasurements.Remove(measurement);
        return Task.CompletedTask;
    }

    public async Task RemoveByKpiAsync(Guid kpiDefinitionId, CancellationToken cancellationToken) =>
        dbContext.KpiMeasurements.RemoveRange(
            await dbContext.KpiMeasurements.Where(m => m.KpiDefinitionId == kpiDefinitionId).ToListAsync(cancellationToken));
}
