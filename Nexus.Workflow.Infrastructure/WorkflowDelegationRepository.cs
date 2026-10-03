using Microsoft.EntityFrameworkCore;
using Nexus.Workflow.Application;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure;

public sealed class WorkflowDelegationRepository(WorkflowDbContext dbContext) : IWorkflowDelegationRepository
{
    public Task<WorkflowDelegation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WorkflowDelegations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkflowDelegation>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDelegations
            .Where(d => d.TenantId == tenantId && (d.DelegatorUserId == userId || d.DelegateUserId == userId))
            .OrderByDescending(d => d.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowDelegation>> ListActiveForDelegateAsync(Guid tenantId, Guid delegateUserId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDelegations
            .Where(d => d.TenantId == tenantId && d.DelegateUserId == delegateUserId && !d.IsRevoked && d.StartDate <= date && d.EndDate >= date)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowDelegation>> ListLiveBetweenAsync(Guid tenantId, Guid delegatorUserId, Guid delegateUserId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDelegations
            .Where(d => d.TenantId == tenantId && d.DelegatorUserId == delegatorUserId && d.DelegateUserId == delegateUserId && !d.IsRevoked)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WorkflowDelegation delegation, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDelegations.AddAsync(delegation, cancellationToken);
}
