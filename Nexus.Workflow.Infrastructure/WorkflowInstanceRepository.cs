using Microsoft.EntityFrameworkCore;
using Nexus.Workflow.Application;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure;

public sealed class WorkflowInstanceRepository(WorkflowDbContext dbContext) : IWorkflowInstanceRepository
{
    public Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WorkflowInstances.Include(i => i.Decisions).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<WorkflowInstance?> GetActiveForSubjectAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        dbContext.WorkflowInstances
            .Include(i => i.Decisions)
            .Where(i => i.SubjectType == subjectType && i.SubjectId == subjectId && i.Status == WorkflowInstanceStatus.InProgress)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowInstance>> ListPendingForApproverAsync(Guid tenantId, Guid approverUserId, CancellationToken cancellationToken)
    {
        var pending = await dbContext.WorkflowInstances
            .Include(i => i.Decisions)
            .Where(i => i.TenantId == tenantId && i.Status == WorkflowInstanceStatus.InProgress)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return pending;
        }

        var definitionIds = pending.Select(i => i.WorkflowDefinitionId).Distinct().ToList();
        var definitions = await dbContext.WorkflowDefinitions
            .Include(d => d.Steps)
            .Where(d => definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        // Filtered in memory: pending-approval volume per tenant is small, and this avoids a
        // fragile multi-table LINQ-to-SQL join over a computed "current step" concept.
        return pending.Where(instance =>
        {
            if (!definitions.TryGetValue(instance.WorkflowDefinitionId, out var definition))
            {
                return false;
            }

            var currentStep = definition.Steps.SingleOrDefault(step => step.Order == instance.CurrentStepOrder);
            return currentStep is not null && (currentStep.ApproverUserId is null || currentStep.ApproverUserId == approverUserId);
        }).ToList();
    }

    public async Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        await dbContext.WorkflowInstances.AddAsync(instance, cancellationToken);
    }
}
