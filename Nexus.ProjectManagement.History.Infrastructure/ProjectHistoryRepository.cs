using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Domain;

namespace Nexus.ProjectManagement.History.Infrastructure;

public sealed class ProjectHistoryRepository(ProjectHistoryDbContext dbContext) : IProjectHistoryRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<ProjectChange> changes, CancellationToken cancellationToken) =>
        await dbContext.ProjectChanges.AddRangeAsync(changes, cancellationToken);

    public async Task<(IReadOnlyList<ProjectChange> Items, int Total)> QueryAsync(ProjectHistoryQuery query, CancellationToken cancellationToken)
    {
        var changes = dbContext.ProjectChanges.Where(c => c.TenantId == query.TenantId && c.ProjectId == query.ProjectId);
        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            changes = changes.Where(c => c.EntityName == query.EntityName);
        }

        if (query.Kind is { } kind)
        {
            changes = changes.Where(c => c.Kind == kind);
        }

        if (query.UserId is { } userId)
        {
            changes = changes.Where(c => c.ChangedByUserId == userId);
        }

        if (query.From is { } from)
        {
            changes = changes.Where(c => c.ChangedAtUtc >= from);
        }

        if (query.To is { } to)
        {
            changes = changes.Where(c => c.ChangedAtUtc <= to);
        }

        var total = await changes.CountAsync(cancellationToken);
        var items = await changes.OrderByDescending(c => c.ChangedAtUtc).ThenBy(c => c.Id).Skip(query.Skip).Take(query.Take).ToListAsync(cancellationToken);
        return (items, total);
    }
}
