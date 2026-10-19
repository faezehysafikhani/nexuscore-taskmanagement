using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public sealed class ChangeRequestRepository(ContractsDbContext dbContext) : IChangeRequestRepository
{
    public Task<ChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChangeRequests.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ChangeRequests.Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<int> MaxNumberAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ChangeRequests.Where(c => c.ProjectId == projectId).MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0;

    public async Task AddAsync(ChangeRequest changeRequest, CancellationToken cancellationToken) =>
        await dbContext.ChangeRequests.AddAsync(changeRequest, cancellationToken);

    public Task RemoveAsync(ChangeRequest changeRequest, CancellationToken cancellationToken)
    {
        dbContext.ChangeRequests.Remove(changeRequest);
        return Task.CompletedTask;
    }
}
