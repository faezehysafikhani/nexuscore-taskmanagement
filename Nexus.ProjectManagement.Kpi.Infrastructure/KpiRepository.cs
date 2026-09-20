using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Infrastructure;

public sealed class KpiRepository(KpiDbContext dbContext) : IKpiRepository
{
    public Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.KpiDefinitions.SingleOrDefaultAsync(kpi => kpi.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken cancellationToken)
    {
        var query = dbContext.KpiDefinitions.Where(kpi => kpi.ProjectId == projectId);

        if (deliverableId is { } id)
        {
            query = query.Where(kpi => kpi.DeliverableId == id);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(KpiDefinition kpi, CancellationToken cancellationToken)
    {
        await dbContext.KpiDefinitions.AddAsync(kpi, cancellationToken);
    }
}
