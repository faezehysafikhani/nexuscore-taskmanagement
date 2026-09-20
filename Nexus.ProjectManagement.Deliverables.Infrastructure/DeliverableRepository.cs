using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Deliverables.Domain;

namespace Nexus.ProjectManagement.Deliverables.Infrastructure;

public sealed class DeliverableRepository(DeliverablesDbContext dbContext) : IDeliverableRepository
{
    public Task<Deliverable?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Deliverables.SingleOrDefaultAsync(deliverable => deliverable.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Deliverable>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Deliverables.Where(deliverable => deliverable.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task AddAsync(Deliverable deliverable, CancellationToken cancellationToken)
    {
        await dbContext.Deliverables.AddAsync(deliverable, cancellationToken);
    }
}
