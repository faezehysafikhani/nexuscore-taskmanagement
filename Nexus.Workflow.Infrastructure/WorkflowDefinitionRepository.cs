using Microsoft.EntityFrameworkCore;
using Nexus.Workflow.Application;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure;

public sealed class WorkflowDefinitionRepository(WorkflowDbContext dbContext) : IWorkflowDefinitionRepository
{
    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WorkflowDefinitions.Include(d => d.Steps).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<WorkflowDefinition?> FindApplicableAsync(Guid tenantId, string subjectType, string? scopeType, Guid? scopeId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(scopeType) && scopeType != WorkflowDefinition.GeneralScope)
        {
            var scoped = await dbContext.WorkflowDefinitions
                .Include(d => d.Steps)
                .SingleOrDefaultAsync(
                    d => d.TenantId == tenantId && d.SubjectType == subjectType && d.IsActive
                        && d.ScopeType == scopeType && d.ScopeId == scopeId,
                    cancellationToken);

            if (scoped is not null)
            {
                return scoped;
            }
        }

        return await dbContext.WorkflowDefinitions
            .Include(d => d.Steps)
            .SingleOrDefaultAsync(
                d => d.TenantId == tenantId && d.SubjectType == subjectType && d.IsActive
                    && d.ScopeType == WorkflowDefinition.GeneralScope,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(Guid tenantId, string? subjectType, CancellationToken cancellationToken)
    {
        var query = dbContext.WorkflowDefinitions.Include(d => d.Steps).Where(d => d.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(subjectType))
        {
            query = query.Where(d => d.SubjectType == subjectType);
        }

        return await query.OrderBy(d => d.SubjectType).ThenBy(d => d.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        await dbContext.WorkflowDefinitions.AddAsync(definition, cancellationToken);
    }
}
