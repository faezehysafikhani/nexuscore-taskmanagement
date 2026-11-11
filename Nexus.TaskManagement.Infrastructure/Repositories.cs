using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Infrastructure;

public sealed class TaskRepository(TaskManagementDbContext db) : ITaskRepository
{
    // One query per collection: in a single query the collections multiply each other's rows
    // (subtasks x their tags x files x task tags x files x assignees), and a recurring task with
    // its occurrences came back as thousands of rows - slow enough to time out.
    public Task<TaskItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tasks
            .Include(t => t.SubTasks).ThenInclude(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.SubTasks).ThenInclude(s => s.Files).ThenInclude(tf => tf.File)
            .Include(t => t.SubTasks).ThenInclude(s => s.Assignees)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Files).ThenInclude(tf => tf.File)
            .Include(t => t.Assignees)
            .Include(t => t.Recurrence)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public async Task<TaskItem?> GetArchivedAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        // The archive is read with the archive filter off and only archived rows asked for; the access rules still apply.
        db.IncludeArchived = true;
        try
        {
            return await db.Tasks
                .Include(t => t.SubTasks).ThenInclude(s => s.Assignees)
                .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
                .Include(t => t.Files).ThenInclude(tf => tf.File)
                .Include(t => t.Assignees)
                .Include(t => t.Recurrence)
                .AsSplitQuery()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id && t.ArchivedAtUtc != null, cancellationToken);
        }
        finally
        {
            db.IncludeArchived = false;
        }
    }

    public Task<TaskItem?> GetPeopleOfAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tasks.IgnoreQueryFilters()
            .Include(t => t.Assignees)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public Task<TaskItem?> GetForUpdateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<NexusCore.Application.Identity.Interfaces.TaskSummary>> GetSummariesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        var ids = taskIds.ToList();
        var rows = await db.Tasks.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.DueDate, t.Status })
            .ToListAsync(cancellationToken);
        return rows.Select(t => new NexusCore.Application.Identity.Interfaces.TaskSummary(
            t.Id, t.Title, t.DueDate, t.Status.ToString(), t.Status == Domain.TaskItemStatus.Completed)).ToList();
    }

    public async Task<IReadOnlyList<NexusCore.Application.Identity.Interfaces.TaskPeopleSnapshot>> ListPeopleAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.OwnerUserId, t.AssignedUserId }).ToListAsync(cancellationToken);
        var assignees = (await db.TaskAssignees.AsNoTracking().Where(a => !a.IsActing && db.Tasks.Any(t => t.Id == a.TaskId && t.TenantId == tenantId))
                .Select(a => new { a.TaskId, a.UserId, a.IsResponsible }).ToListAsync(cancellationToken))
            .ToLookup(a => a.TaskId);
        return tasks.Select(t =>
        {
            var responsible = new List<Guid>();
            if (t.AssignedUserId is { } primary)
            {
                responsible.Add(primary);
            }

            responsible.AddRange(assignees[t.Id].Where(a => a.IsResponsible && !responsible.Contains(a.UserId)).Select(a => a.UserId));
            var access = assignees[t.Id].Where(a => !responsible.Contains(a.UserId)).Select(a => a.UserId).Distinct().ToList();
            return new NexusCore.Application.Identity.Interfaces.TaskPeopleSnapshot(t.Id, t.OwnerUserId, responsible, access);
        }).ToList();
    }

    public async Task<PagedResult<TaskItem>> ListAsync(ListTasksRequest request, CancellationToken cancellationToken)
    {
        // The archive is a list of its own: the archive filter is lifted for this one read and only archived rows come back.
        db.IncludeArchived = request.Archived;
        try
        {
            return await ListCoreAsync(request, cancellationToken);
        }
        finally
        {
            db.IncludeArchived = false;
        }
    }

    private async Task<PagedResult<TaskItem>> ListCoreAsync(ListTasksRequest request, CancellationToken cancellationToken)
    {
        var query = db.Tasks
            .Include(t => t.SubTasks)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Recurrence)
            .Where(t => t.TenantId == request.TenantId);

        if (request.Archived)
        {
            query = query.Where(t => t.ArchivedAtUtc != null);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t => t.Title.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }

        if (request.Status is { } status) query = query.Where(t => t.Status == status);
        if (request.CustomStatusId is { } customStatusId) query = query.Where(t => t.CustomStatusId == customStatusId);
        if (request.Priority is { } priority) query = query.Where(t => t.Priority == priority);
        if (request.IsProject is { } isProject) query = query.Where(t => t.IsProject == isProject);
        // Any of the task's responsible people, not only the first.
        if (request.AssignedUserId is { } userId)
            query = query.Where(t => t.AssignedUserId == userId || t.Assignees.Any(a => a.UserId == userId && a.IsResponsible));
        if (request.AssignedUserGroupId is { } groupId) query = query.Where(t => t.AssignedUserGroupId == groupId);
        if (request.DueFrom is { } from) query = query.Where(t => t.DueDate >= from);
        if (request.DueTo is { } to) query = query.Where(t => t.DueDate <= to);
        if (request.TagId is { } tagId) query = query.Where(t => t.Tags.Any(tt => tt.TagId == tagId));

        // Recurring means "has a schedule" - there is no flag to filter on, by design.
        if (request.IsRecurring is { } isRecurring)
        {
            query = isRecurring
                ? query.Where(t => t.Recurrence != null)
                : query.Where(t => t.Recurrence == null);
        }

        if (request.Overdue is true)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(t => t.DueDate < today && t.Status != TaskItemStatus.Completed);
        }

        query = (request.SortBy, request.SortDescending) switch
        {
            (TaskSortBy.DueDate, true) => query.OrderByDescending(t => t.DueDate),
            (TaskSortBy.DueDate, false) => query.OrderBy(t => t.DueDate),
            (TaskSortBy.Priority, true) => query.OrderByDescending(t => t.Priority),
            (TaskSortBy.Priority, false) => query.OrderBy(t => t.Priority),
            (TaskSortBy.Title, true) => query.OrderByDescending(t => t.Title),
            (TaskSortBy.Title, false) => query.OrderBy(t => t.Title),
            (TaskSortBy.CreatedAtUtc, true) => query.OrderByDescending(t => t.CreatedAtUtc),
            (TaskSortBy.CreatedAtUtc, false) => query.OrderBy(t => t.CreatedAtUtc),
            (_, true) => query.OrderByDescending(t => t.ModifiedAtUtc ?? t.CreatedAtUtc),
            (_, false) => query.OrderBy(t => t.ModifiedAtUtc ?? t.CreatedAtUtc)
        };

        // A page is at most 200 rows, whatever the request asked for.
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskItem>(items, pageNumber, pageSize, total);
    }

    public Task<SubTask?> GetSubTaskAsync(Guid tenantId, Guid subTaskId, CancellationToken cancellationToken) =>
        db.SubTasks
            .Include(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(s => s.Files).ThenInclude(tf => tf.File)
            .Include(s => s.Assignees)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == subTaskId, cancellationToken);

    public async Task<IReadOnlyList<SubTask>> GetSubTasksAsync(
        Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.SubTasks
            .Include(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(s => s.Files).ThenInclude(tf => tf.File)
            .Include(s => s.Assignees)
            .Where(s => s.TenantId == tenantId && s.TaskId == taskId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

    public Task<int> CountSubTasksAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        db.SubTasks.CountAsync(s => s.TenantId == tenantId && s.TaskId == taskId, cancellationToken);

    public async Task AddAsync(TaskItem task, CancellationToken cancellationToken) =>
        await db.Tasks.AddAsync(task, cancellationToken);

    public void Remove(TaskItem task) => db.Tasks.Remove(task);

    public void RemoveSubTask(SubTask subTask) => db.SubTasks.Remove(subTask);

    /// <summary>
    /// Both junctions use NoAction on their task and subtask keys, so their rows have to be
    /// removed explicitly before the task goes. Subtask links are cleared too, because the
    /// subtasks themselves will cascade away with the task.
    /// </summary>
    public async Task<IReadOnlyList<string>> ClearLinksForTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var subTaskIds = await db.SubTasks
            .Where(s => s.TaskId == taskId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var commentIds = await db.TaskComments
            .Where(c => c.TaskId == taskId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // Comment attachments too: comments cascade with the task, but their file links do not.
        var fileLinks = await db.TaskFiles
            .Include(f => f.File)
            .Where(f => f.TaskId == taskId
                        || (f.SubTaskId != null && subTaskIds.Contains(f.SubTaskId.Value))
                        || (f.CommentId != null && commentIds.Contains(f.CommentId.Value)))
            .ToListAsync(cancellationToken);
        var storageKeys = RemoveLinksAndFiles(fileLinks);

        var tagLinks = await db.TaskTags
            .Where(t => t.TaskId == taskId || (t.SubTaskId != null && subTaskIds.Contains(t.SubTaskId.Value)))
            .ToListAsync(cancellationToken);
        db.TaskTags.RemoveRange(tagLinks);
        return storageKeys;
    }

    public async Task<IReadOnlyList<string>> ClearLinksForSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken)
    {
        var fileLinks = await db.TaskFiles.Include(f => f.File).Where(f => f.SubTaskId == subTaskId).ToListAsync(cancellationToken);
        var storageKeys = RemoveLinksAndFiles(fileLinks);

        var tagLinks = await db.TaskTags.Where(t => t.SubTaskId == subTaskId).ToListAsync(cancellationToken);

        // A row that also points at the task must survive with only its subtask side cleared.
        foreach (var link in tagLinks)
        {
            link.DetachSubTask();
            if (link.IsOrphaned)
            {
                db.TaskTags.Remove(link);
            }
        }

        return storageKeys;
    }

    /// <summary>Every file has exactly one link (upload creates both), so the file goes with it.</summary>
    private List<string> RemoveLinksAndFiles(List<TaskFile> links)
    {
        db.TaskFiles.RemoveRange(links);
        var files = links.Where(l => l.File is not null).Select(l => l.File!).DistinctBy(f => f.Id).ToList();
        db.Files.RemoveRange(files);
        return files.Select(f => f.StoragePath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    public Task<bool> UserExistsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);

    public Task<bool> ActiveUserExistsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, cancellationToken);

    public Task<bool> UserGroupExistsAsync(Guid tenantId, Guid userGroupId, CancellationToken cancellationToken) =>
        db.UserGroups.AnyAsync(g => g.Id == userGroupId && g.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> GetInReviewAsync(
        Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        await db.Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id) && t.ArchivedAtUtc == null
                && t.ReviewStatus == NexusCore.Application.Approvals.ApprovalStatus.PendingApproval)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetUserSummariesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummaryDto>();
        }

        return await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummaryDto(u.Id, u.DisplayName, u.Email))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserGroupSummaryDto>> GetUserGroupSummariesAsync(
        IReadOnlyCollection<Guid> userGroupIds, CancellationToken cancellationToken)
    {
        if (userGroupIds.Count == 0)
        {
            return new Dictionary<Guid, UserGroupSummaryDto>();
        }

        return await db.UserGroups
            .Where(g => userGroupIds.Contains(g.Id))
            .Select(g => new UserGroupSummaryDto(g.Id, g.Name))
            .ToDictionaryAsync(g => g.Id, cancellationToken);
    }
}

public sealed class RepetitiveTaskRepository(TaskManagementDbContext db) : IRepetitiveTaskRepository
{
    public Task<RepetitiveTask?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.RepetitiveTasks.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken);

    public Task<RepetitiveTask?> GetByTaskIdAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        db.RepetitiveTasks.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.TaskId == taskId, cancellationToken);

    public async Task<PagedResult<RepetitiveTask>> ListAsync(
        ListRepetitiveTasksRequest request, CancellationToken cancellationToken)
    {
        var query = db.RepetitiveTasks.Where(r => r.TenantId == request.TenantId);

        if (request.IsActive is { } isActive)
        {
            query = query.Where(r => r.IsActive == isActive);
        }

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.NextExecutionAtUtc ?? DateTimeOffset.MaxValue)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RepetitiveTask>(items, pageNumber, pageSize, total);
    }

    public async Task AddAsync(RepetitiveTask recurrence, CancellationToken cancellationToken) =>
        await db.RepetitiveTasks.AddAsync(recurrence, cancellationToken);

    public void Remove(RepetitiveTask recurrence) => db.RepetitiveTasks.Remove(recurrence);
}

public sealed class TaskStatusRepository(TaskManagementDbContext db) : ITaskStatusRepository
{
    public Task<TaskStatusDefinition?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TaskStatuses.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken);

    public Task<TaskStatusDefinition?> GetByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.TaskStatuses.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.NormalizedName == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskStatusDefinition>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.TaskStatuses.Where(s => s.TenantId == tenantId).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(TaskStatusDefinition status, CancellationToken cancellationToken) =>
        await db.TaskStatuses.AddAsync(status, cancellationToken);

    public async Task RemoveAsync(TaskStatusDefinition status, CancellationToken cancellationToken)
    {
        // Tasks keep their category; only the label goes. Archived ones too, hence IgnoreQueryFilters.
        var tasks = await db.Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == status.TenantId && t.CustomStatusId == status.Id)
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.ClearCustomStatus();
        }

        db.TaskStatuses.Remove(status);
    }
}

public sealed class RetentionRepository(TaskManagementDbContext db) : IRetentionRepository
{
    public Task<TaskRetentionSetting?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.TaskRetentionSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(TaskRetentionSetting setting, CancellationToken cancellationToken) => await db.TaskRetentionSettings.AddAsync(setting, cancellationToken);
}

public sealed class MembershipRepository(TaskManagementDbContext db) : IMembershipRepository
{
    public Task<TenantMembership?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.TenantMemberships.FirstOrDefaultAsync(m => m.TenantId == tenantId, cancellationToken);

    public async Task AddAsync(TenantMembership membership, CancellationToken cancellationToken) => await db.TenantMemberships.AddAsync(membership, cancellationToken);

    public void Remove(TenantMembership membership) => db.TenantMemberships.Remove(membership);

    public async Task<long> StorageBytesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Files.Where(f => f.TenantId == tenantId).SumAsync(f => (long?)f.FileSizeBytes, cancellationToken) ?? 0;

    public Task<int> TaskCountAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Tasks.IgnoreQueryFilters().CountAsync(t => t.TenantId == tenantId && t.ArchivedAtUtc == null, cancellationToken);
}

public sealed class WebhookRepository(TaskManagementDbContext db) : IWebhookRepository
{
    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.WebhookSubscriptions.Where(w => w.TenantId == tenantId).OrderBy(w => w.Name).ToListAsync(cancellationToken);

    public Task<WebhookSubscription?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id, cancellationToken);

    public async Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken) => await db.WebhookSubscriptions.AddAsync(subscription, cancellationToken);

    public void Remove(WebhookSubscription subscription) => db.WebhookSubscriptions.Remove(subscription);

    public async Task<IReadOnlyList<WebhookSubscription>> ListActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.WebhookSubscriptions.Where(w => w.TenantId == tenantId && w.IsActive).ToListAsync(cancellationToken);

    public async Task AddDeliveriesAsync(IEnumerable<WebhookDelivery> deliveries, CancellationToken cancellationToken) =>
        await db.WebhookDeliveries.AddRangeAsync(deliveries, cancellationToken);

    public async Task<IReadOnlyList<WebhookDelivery>> ListDeliveriesAsync(Guid tenantId, Guid subscriptionId, int take, CancellationToken cancellationToken) =>
        await db.WebhookDeliveries.Where(d => d.TenantId == tenantId && d.SubscriptionId == subscriptionId).OrderByDescending(d => d.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);

    public async Task RemoveDeliveriesOfAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var rows = await db.WebhookDeliveries.Where(d => d.SubscriptionId == subscriptionId).ToListAsync(cancellationToken);
        db.WebhookDeliveries.RemoveRange(rows);
    }
}

public sealed class AutomationRepository(TaskManagementDbContext db) : IAutomationRepository
{
    public async Task<IReadOnlyList<AutomationRule>> ListRulesAsync(Guid tenantId, AutomationTrigger? trigger, bool activeOnly, CancellationToken cancellationToken)
    {
        var query = db.AutomationRules.Where(r => r.TenantId == tenantId);
        if (trigger is { } t) query = query.Where(r => r.Trigger == t);
        if (activeOnly) query = query.Where(r => r.IsActive);
        return await query.OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public Task<AutomationRule?> GetRuleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.AutomationRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken);

    public async Task AddRuleAsync(AutomationRule rule, CancellationToken cancellationToken) => await db.AutomationRules.AddAsync(rule, cancellationToken);

    public void RemoveRule(AutomationRule rule) => db.AutomationRules.Remove(rule);

    public async Task AddLogAsync(AutomationLogEntry entry, CancellationToken cancellationToken) => await db.AutomationLog.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<AutomationLogEntry>> ListLogAsync(Guid tenantId, Guid? taskId, int take, CancellationToken cancellationToken)
    {
        var query = db.AutomationLog.Where(l => l.TenantId == tenantId);
        if (taskId is { } id) query = query.Where(l => l.TaskId == id);
        return await query.OrderByDescending(l => l.AtUtc).Take(take).ToListAsync(cancellationToken);
    }
}

public sealed class TimesheetRepository(TaskManagementDbContext db) : ITimesheetRepository
{
    public Task<TimeEntry?> GetEntryAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TimeEntries.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, cancellationToken);

    public async Task AddEntryAsync(TimeEntry entry, CancellationToken cancellationToken) => await db.TimeEntries.AddAsync(entry, cancellationToken);

    public void RemoveEntry(TimeEntry entry) => db.TimeEntries.Remove(entry);

    public async Task<IReadOnlyList<TimeEntry>> ListEntriesAsync(Guid tenantId, Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.TimeEntries.Where(e => e.TenantId == tenantId && e.UserId == userId && e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeEntry>> ListEntriesForTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TimeEntries.Where(e => e.TenantId == tenantId && e.TaskId == taskId).OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeEntry>> ListEntriesOfSubmissionAsync(Guid tenantId, Guid submissionId, CancellationToken cancellationToken) =>
        await db.TimeEntries.Where(e => e.TenantId == tenantId && e.SubmissionId == submissionId).ToListAsync(cancellationToken);

    public async Task<int> SumMinutesOnDayAsync(Guid tenantId, Guid userId, DateOnly date, Guid? exceptEntryId, CancellationToken cancellationToken) =>
        await db.TimeEntries.Where(e => e.TenantId == tenantId && e.UserId == userId && e.Date == date && e.Id != exceptEntryId).SumAsync(e => (int?)e.Minutes, cancellationToken) ?? 0;

    public Task<TimesheetSubmission?> GetSubmissionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TimesheetSubmissions.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken);

    public Task<TimesheetSubmission?> FindSubmissionAsync(Guid tenantId, Guid userId, DateOnly weekStart, CancellationToken cancellationToken) =>
        db.TimesheetSubmissions.Where(s => s.TenantId == tenantId && s.UserId == userId && s.WeekStart == weekStart)
            .OrderByDescending(s => s.SubmittedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TimesheetSubmission>> GetPendingSubmissionsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.TimesheetSubmissions.Where(s => s.TenantId == tenantId && ids.Contains(s.Id) && s.Status == NexusCore.Application.Approvals.ApprovalStatus.PendingApproval).ToListAsync(cancellationToken);

    public async Task AddSubmissionAsync(TimesheetSubmission submission, CancellationToken cancellationToken) =>
        await db.TimesheetSubmissions.AddAsync(submission, cancellationToken);

    public Task<RunningTimer?> GetTimerAsync(Guid userId, CancellationToken cancellationToken) =>
        db.RunningTimers.FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

    public async Task AddTimerAsync(RunningTimer timer, CancellationToken cancellationToken) => await db.RunningTimers.AddAsync(timer, cancellationToken);

    public void RemoveTimer(RunningTimer timer) => db.RunningTimers.Remove(timer);

    public async Task<IReadOnlyList<(Guid UserId, int Logged, int OpenTasks, int RemainingEstimated)>> WorkloadAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var logged = await db.TimeEntries.Where(e => e.TenantId == tenantId && e.Date >= from && e.Date <= to)
            .GroupBy(e => e.UserId).Select(g => new { UserId = g.Key, Minutes = g.Sum(e => e.Minutes) }).ToDictionaryAsync(x => x.UserId, x => x.Minutes, cancellationToken);

        var open = await db.Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.ArchivedAtUtc == null && t.Status != TaskItemStatus.Completed && t.AssignedUserId != null)
            .Select(t => new { t.Id, User = t.AssignedUserId!.Value, t.EstimatedMinutes })
            .Take(20000).ToListAsync(cancellationToken);

        var estimatedIds = open.Where(t => t.EstimatedMinutes > 0).Select(t => t.Id).ToList();
        var actualByTask = estimatedIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.TimeEntries.Where(e => e.TenantId == tenantId && estimatedIds.Contains(e.TaskId))
                .GroupBy(e => e.TaskId).Select(g => new { TaskId = g.Key, Minutes = g.Sum(e => e.Minutes) }).ToDictionaryAsync(x => x.TaskId, x => x.Minutes, cancellationToken);

        var rows = new Dictionary<Guid, (int Logged, int Open, int Remaining)>();
        foreach (var (user, minutes) in logged)
        {
            rows[user] = (minutes, 0, 0);
        }

        foreach (var t in open)
        {
            rows.TryGetValue(t.User, out var row);
            var remaining = t.EstimatedMinutes is > 0 ? Math.Max(0, t.EstimatedMinutes.Value - actualByTask.GetValueOrDefault(t.Id)) : 0;
            rows[t.User] = (row.Logged, row.Open + 1, row.Remaining + remaining);
        }

        return rows.Select(r => (r.Key, r.Value.Logged, r.Value.Open, r.Value.Remaining)).ToList();
    }
}

public sealed class TaskPlanningRepository(TaskManagementDbContext db) : ITaskPlanningRepository
{
    public async Task<IReadOnlyList<TaskDependency>> ListDependenciesAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskDependencies.Where(d => d.TenantId == tenantId && d.TaskId == taskId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskDependency>> ListDependentsAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskDependencies.Where(d => d.TenantId == tenantId && d.BlockedByTaskId == taskId).ToListAsync(cancellationToken);

    public Task<TaskDependency?> GetDependencyAsync(Guid tenantId, Guid taskId, Guid blockedByTaskId, CancellationToken cancellationToken) =>
        db.TaskDependencies.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.TaskId == taskId && d.BlockedByTaskId == blockedByTaskId, cancellationToken);

    public async Task AddDependencyAsync(TaskDependency dependency, CancellationToken cancellationToken) =>
        await db.TaskDependencies.AddAsync(dependency, cancellationToken);

    public void RemoveDependency(TaskDependency dependency) => db.TaskDependencies.Remove(dependency);

    public async Task<IReadOnlySet<Guid>> BlockersClosureAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        // The graph of one tenant is small; walk it level by level.
        var seen = new HashSet<Guid>();
        var frontier = new List<Guid> { taskId };
        while (frontier.Count > 0)
        {
            var next = await db.TaskDependencies
                .Where(d => d.TenantId == tenantId && frontier.Contains(d.TaskId))
                .Select(d => d.BlockedByTaskId)
                .ToListAsync(cancellationToken);
            frontier = next.Where(seen.Add).ToList();
        }

        return seen;
    }

    public async Task<IReadOnlyList<TaskBriefDto>> BriefsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        return await db.Tasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id) && t.ArchivedAtUtc == null)
            .Select(t => new TaskBriefDto(t.Id, t.Title, t.Status, t.Status == TaskItemStatus.Completed))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountOpenBlockersAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        var ids = await db.TaskDependencies.Where(d => d.TenantId == tenantId && d.TaskId == taskId).Select(d => d.BlockedByTaskId).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        // Archived blockers no longer count: they are out of the way.
        return await db.Tasks.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == tenantId && ids.Contains(t.Id) && t.ArchivedAtUtc == null && t.Status != TaskItemStatus.Completed, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskCriterion>> ListCriteriaAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskCriteria.Where(c => c.TenantId == tenantId && c.TaskId == taskId).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

    public Task<TaskCriterion?> GetCriterionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TaskCriteria.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, cancellationToken);

    public async Task AddCriterionAsync(TaskCriterion criterion, CancellationToken cancellationToken) =>
        await db.TaskCriteria.AddAsync(criterion, cancellationToken);

    public void RemoveCriterion(TaskCriterion criterion) => db.TaskCriteria.Remove(criterion);

    public Task<int> CountUnmetCriteriaAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        db.TaskCriteria.CountAsync(c => c.TenantId == tenantId && c.TaskId == taskId && !c.IsMet, cancellationToken);

    public async Task<int?> GetWipLimitAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await db.TaskWipSettings.FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken))?.LimitPerUser;

    public async Task SetWipLimitAsync(Guid tenantId, int? limit, CancellationToken cancellationToken)
    {
        var row = await db.TaskWipSettings.FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            await db.TaskWipSettings.AddAsync(new TaskWipSetting(tenantId, limit), cancellationToken);
        }
        else
        {
            row.Set(limit);
        }
    }

    public Task<int> CountInProgressAsync(Guid tenantId, Guid userId, Guid exceptTaskId, CancellationToken cancellationToken) =>
        db.Tasks.IgnoreQueryFilters().CountAsync(t => t.TenantId == tenantId && t.ArchivedAtUtc == null && t.Id != exceptTaskId
            && t.Status == TaskItemStatus.InProgress && t.AssignedUserId == userId, cancellationToken);

    public async Task<IReadOnlyList<TaskTemplate>> ListTemplatesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.TaskTemplates.Where(t => t.TenantId == tenantId).OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public Task<TaskTemplate?> GetTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TaskTemplates.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public Task<TaskTemplate?> GetTemplateByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.TaskTemplates.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.NormalizedName == normalized, cancellationToken);
    }

    public async Task AddTemplateAsync(TaskTemplate template, CancellationToken cancellationToken) =>
        await db.TaskTemplates.AddAsync(template, cancellationToken);

    public void RemoveTemplate(TaskTemplate template) => db.TaskTemplates.Remove(template);
}

public sealed class TaskRequestRepository(TaskManagementDbContext db) : ITaskRequestRepository
{
    public Task<RequestCategory?> GetCategoryAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.RequestCategories.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, cancellationToken);

    public Task<RequestCategory?> GetCategoryByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.RequestCategories.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.NormalizedName == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<RequestCategory>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.RequestCategories.Where(c => c.TenantId == tenantId).OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task AddCategoryAsync(RequestCategory category, CancellationToken cancellationToken) =>
        await db.RequestCategories.AddAsync(category, cancellationToken);

    public Task<TaskRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TaskRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskRequest>> ListAsync(Guid tenantId, Guid? requesterUserId, TaskRequestStatus? status, CancellationToken cancellationToken)
    {
        var query = db.TaskRequests.Where(r => r.TenantId == tenantId);
        if (requesterUserId is { } requester) query = query.Where(r => r.RequesterUserId == requester);
        if (status is { } s) query = query.Where(r => r.Status == s);
        return await query.OrderByDescending(r => r.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TaskRequest request, CancellationToken cancellationToken) =>
        await db.TaskRequests.AddAsync(request, cancellationToken);
}

public sealed class TagRepository(TaskManagementDbContext db) : ITagRepository
{
    public Task<Tag?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tags.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public Task<Tag?> GetByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.Tags.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.NormalizedName == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> ListAsync(
        Guid tenantId, string? search, CancellationToken cancellationToken)
    {
        var query = db.Tags.Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(t => t.NormalizedName.Contains(term));
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken) =>
        await db.Tags.AddAsync(tag, cancellationToken);

    public void Remove(Tag tag) => db.Tags.Remove(tag);

    public Task<TaskTag?> FindLinkAsync(
        Guid tagId, Guid? taskId, Guid? subTaskId, CancellationToken cancellationToken) =>
        db.TaskTags.FirstOrDefaultAsync(
            t => t.TagId == tagId
                 && (taskId == null || t.TaskId == taskId)
                 && (subTaskId == null || t.SubTaskId == subTaskId)
                 && (taskId != null || subTaskId != null),
            cancellationToken);

    public async Task AddLinkAsync(TaskTag link, CancellationToken cancellationToken) =>
        await db.TaskTags.AddAsync(link, cancellationToken);

    public void RemoveLink(TaskTag link) => db.TaskTags.Remove(link);
}

public sealed class TaskFileRepository(TaskManagementDbContext db) : ITaskFileRepository
{
    public Task<TaskFileAsset?> GetAssetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken) =>
        db.Files.FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == fileId, cancellationToken);

    public Task<TaskFile?> GetLinkAsync(Guid linkId, CancellationToken cancellationToken) =>
        db.TaskFiles.FirstOrDefaultAsync(f => f.Id == linkId, cancellationToken);

    // TaskFiles carries the access query filter, so only links to reachable owners count.
    public Task<bool> IsReachableAsync(Guid fileId, CancellationToken cancellationToken) =>
        db.TaskFiles.AnyAsync(f => f.FileId == fileId, cancellationToken);

    public async Task<IReadOnlyList<TaskFile>> ListForTaskAsync(
        Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskFiles
            .Include(f => f.File)
            .Where(f => f.TaskId == taskId && f.File!.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskFile>> ListForSubTaskAsync(
        Guid tenantId, Guid subTaskId, CancellationToken cancellationToken) =>
        await db.TaskFiles
            .Include(f => f.File)
            .Where(f => f.SubTaskId == subTaskId && f.File!.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskFile>> ListForCommentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> commentIds, CancellationToken cancellationToken) =>
        commentIds.Count == 0
            ? []
            : await db.TaskFiles
                .Include(f => f.File)
                .Where(f => f.CommentId != null && commentIds.Contains(f.CommentId.Value) && f.File!.TenantId == tenantId)
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ClearForCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var links = await db.TaskFiles.Include(f => f.File).Where(f => f.CommentId == commentId).ToListAsync(cancellationToken);
        db.TaskFiles.RemoveRange(links);
        var files = links.Where(l => l.File is not null).Select(l => l.File!).DistinctBy(f => f.Id).ToList();
        db.Files.RemoveRange(files);
        return files.Select(f => f.StoragePath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    public async Task AddAsync(TaskFileAsset asset, TaskFile link, CancellationToken cancellationToken)
    {
        await db.Files.AddAsync(asset, cancellationToken);
        await db.TaskFiles.AddAsync(link, cancellationToken);
    }

    public void Remove(TaskFileAsset asset, TaskFile link)
    {
        db.TaskFiles.Remove(link);
        db.Files.Remove(asset);
    }
}

public sealed class NoteRepository(TaskManagementDbContext db) : INoteRepository
{
    public Task<Note?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Notes.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Note>> ListForUserAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        await db.Notes
            .Where(n => n.TenantId == tenantId && n.UserId == userId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.ModifiedAtUtc ?? n.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Note note, CancellationToken cancellationToken) =>
        await db.Notes.AddAsync(note, cancellationToken);

    public void Remove(Note note) => db.Notes.Remove(note);
}

public sealed class TaskCommentRepository(TaskManagementDbContext db) : ITaskCommentRepository
{
    public Task<TaskComment?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.TaskComments.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskComment>> ListForTaskAsync(
        Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskComments
            .Where(c => c.TenantId == tenantId && c.TaskId == taskId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(TaskComment comment, CancellationToken cancellationToken) =>
        await db.TaskComments.AddAsync(comment, cancellationToken);

    public void Remove(TaskComment comment) => db.TaskComments.Remove(comment);
}
