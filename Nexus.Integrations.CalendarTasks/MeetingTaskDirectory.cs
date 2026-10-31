using Microsoft.EntityFrameworkCore;
using Nexus.Calendar.Application.Meetings;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.CalendarTasks;

public sealed class MeetingTaskDirectory(
    TaskManagementDbContext db,
    ICurrentUserContext current,
    ITaskService tasks,
    ITaskAccessScope? access = null) : IMeetingTaskDirectory
{
    public async Task<Result<MeetingTaskPage>> SearchAsync(string? search, int pageNumber, int pageSize,
        string? ids, CancellationToken cancellationToken)
    {
        if (current.UserId is null || current.TenantId is null)
            return Result.Failure<MeetingTaskPage>(Error.Unauthorized());
        if (search?.Length > 200)
            return Result.Failure<MeetingTaskPage>(Error.Validation("عبارت جستجو بیش از حد طولانی است."));

        if (access is not null) await access.EnsureLoadedAsync(cancellationToken);
        var query = db.Tasks.AsNoTracking().Where(task => task.TenantId == current.TenantId);
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var values = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length > 100 || values.Any(value => !Guid.TryParse(value, out _)))
                return Result.Failure<MeetingTaskPage>(Error.Validation("شناسه وظایف معتبر نیست."));
            var requested = values.Select(Guid.Parse).Distinct().ToList();
            query = query.Where(task => requested.Contains(task.Id));
            pageNumber = 1;
            pageSize = 100;
        }
        else
        {
            query = query.Where(task => task.OwnerUserId == current.UserId || task.CreatedByUserId == current.UserId
                || task.AssignedUserId == current.UserId || task.Assignees.Any(item => item.UserId == current.UserId));
            pageNumber = Math.Clamp(pageNumber, 1, 10000);
            pageSize = Math.Clamp(pageSize, 1, 50);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = Normalize(search.Trim());
                query = query.Where(task => task.Title.Replace("ي", "ی").Replace("ك", "ک").Replace("‌", " ").Contains(term));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(task => task.CreatedAtUtc).ThenBy(task => task.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(task => new { task.Id, task.Title, task.Status, task.DueDate, task.IsProject })
            .ToListAsync(cancellationToken);
        return Result.Success(new MeetingTaskPage(rows.Select(task => new MeetingTaskOption(
            task.Id, task.Title, task.Status.ToString(), task.DueDate, task.IsProject)).ToList(),
            total, pageNumber, pageSize));
    }

    public async Task<bool> CanAccessAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (current.UserId is null || current.TenantId is null) return false;
        if (access is not null) await access.EnsureLoadedAsync(cancellationToken);
        return (await tasks.GetAsync(taskId, cancellationToken)).IsSuccess;
    }

    private static string Normalize(string value) => value.Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ');
}
