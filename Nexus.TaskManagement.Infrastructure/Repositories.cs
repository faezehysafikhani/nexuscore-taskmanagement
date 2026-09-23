using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Infrastructure;

public sealed class TaskRepository(TaskManagementDbContext db) : ITaskRepository
{
    public Task<TaskItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tasks
            .Include(t => t.SubTasks).ThenInclude(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.SubTasks).ThenInclude(s => s.Files).ThenInclude(tf => tf.File)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Files).ThenInclude(tf => tf.File)
            .Include(t => t.Assignees)
            .Include(t => t.Recurrence)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public Task<TaskItem?> GetForUpdateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public async Task<PagedResult<TaskItem>> ListAsync(ListTasksRequest request, CancellationToken cancellationToken)
    {
        var query = db.Tasks
            .Include(t => t.SubTasks)
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Include(t => t.Recurrence)
            .Where(t => t.TenantId == request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t => t.Title.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }

        if (request.Status is { } status) query = query.Where(t => t.Status == status);
        if (request.Priority is { } priority) query = query.Where(t => t.Priority == priority);
        if (request.IsProject is { } isProject) query = query.Where(t => t.IsProject == isProject);
        if (request.AssignedUserId is { } userId) query = query.Where(t => t.AssignedUserId == userId);
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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskItem>(items, request.PageNumber, request.PageSize, total);
    }

    public Task<SubTask?> GetSubTaskAsync(Guid tenantId, Guid subTaskId, CancellationToken cancellationToken) =>
        db.SubTasks
            .Include(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(s => s.Files).ThenInclude(tf => tf.File)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == subTaskId, cancellationToken);

    public async Task<IReadOnlyList<SubTask>> GetSubTasksAsync(
        Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.SubTasks
            .Include(s => s.Tags).ThenInclude(tt => tt.Tag)
            .Include(s => s.Files).ThenInclude(tf => tf.File)
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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.NextExecutionAtUtc ?? DateTimeOffset.MaxValue)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RepetitiveTask>(items, request.PageNumber, request.PageSize, total);
    }

    public async Task AddAsync(RepetitiveTask recurrence, CancellationToken cancellationToken) =>
        await db.RepetitiveTasks.AddAsync(recurrence, cancellationToken);

    public void Remove(RepetitiveTask recurrence) => db.RepetitiveTasks.Remove(recurrence);
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
