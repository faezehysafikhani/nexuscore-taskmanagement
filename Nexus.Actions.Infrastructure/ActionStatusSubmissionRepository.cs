using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Infrastructure;

public sealed class ActionStatusSubmissionRepository(ActionsDbContext dbContext) : IActionStatusSubmissionRepository
{
    public Task<ActionStatusSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StatusSubmissions.SingleOrDefaultAsync(submission => submission.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActionStatusSubmission>> ListByActionsAsync(IReadOnlyCollection<Guid> actionIds, CancellationToken cancellationToken) =>
        await dbContext.StatusSubmissions
            .Where(submission => actionIds.Contains(submission.ActionId))
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActionStatusSubmission>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken)
    {
        var query = dbContext.StatusSubmissions.Where(submission => submission.TenantId == tenantId);
        if (projectId is { } id)
        {
            query = query.Where(submission => submission.ProjectId == id);
        }

        return await query.OrderByDescending(submission => submission.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActionStatusSubmission submission, CancellationToken cancellationToken) =>
        await dbContext.StatusSubmissions.AddAsync(submission, cancellationToken);
}
