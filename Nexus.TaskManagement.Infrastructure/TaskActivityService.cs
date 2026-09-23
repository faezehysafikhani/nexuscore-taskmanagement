using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using NexusCore.Domain.Auditing;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// The UI's "task history" panel, served from NexusCore's own AuditLog instead of a table of
/// this module's own.
///
/// AuditLog already stores everything that panel shows - who, what, when, free-text detail,
/// and the entity the action was against - so a TaskLog table would have been a second copy
/// of an existing concept. EntityName is pinned to <see cref="TaskEntityName"/> and EntityId
/// to the task id, which is what makes the history readable back out.
///
/// This is the one place the module writes through NexusCoreDbContext. It is the owner of
/// AuditLog and audit is shared infrastructure, not another business module's data.
/// </summary>
public sealed class TaskActivityService(
    NexusCoreDbContext coreDb,
    TaskManagementDbContext taskDb,
    ICurrentUserContext currentUser) : ITaskActivityService
{
    public const string TaskEntityName = "TaskManagement.Task";

    public async Task<Result<IReadOnlyList<TaskActivityDto>>> GetForTaskAsync(
        Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TaskActivityDto>>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var key = taskId.ToString();

        // The history is the task's: only for a task the caller may see (query filter).
        if (!await taskDb.Tasks.AnyAsync(task => task.Id == taskId && task.TenantId == tenantId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<TaskActivityDto>>(Error.NotFound("Task not found."));
        }

        var entries = await coreDb.AuditLogs
            .Where(a => a.TenantId == tenantId && a.EntityName == TaskEntityName && a.EntityId == key)
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        var userIds = entries.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await coreDb.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return Result.Success<IReadOnlyList<TaskActivityDto>>(entries
            .Select(a => new TaskActivityDto(
                a.Id,
                taskId,
                a.UserId,
                a.UserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                a.Action,
                a.Details,
                a.OccurredAtUtc))
            .ToList());
    }

    public async Task RecordAsync(Guid taskId, string action, string? details, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return;
        }

        var entry = new AuditLog(
            Guid.NewGuid(),
            currentUser.TenantId.Value,
            currentUser.UserId,
            action,
            TaskEntityName,
            taskId.ToString(),
            details,
            currentUser.IpAddress);

        await coreDb.AuditLogs.AddAsync(entry, cancellationToken);
        await coreDb.SaveChangesAsync(cancellationToken);
    }
}
