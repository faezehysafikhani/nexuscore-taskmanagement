using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

// ---------------------------------------------------------------------------
// Repositories - one per aggregate that needs querying, matching the shape
// Nexus.ProjectManagement.Core uses. They expose intent, not a generic wrapper
// around DbSet.
// ---------------------------------------------------------------------------

public interface ITaskRepository
{
    /// <summary>Full graph: subtasks, tags, files, assignees and recurrence.</summary>
    Task<TaskItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    /// <summary>No includes - for commands that only mutate scalar columns.</summary>
    Task<TaskItem?> GetForUpdateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<PagedResult<TaskItem>> ListAsync(ListTasksRequest request, CancellationToken cancellationToken);

    Task<SubTask?> GetSubTaskAsync(Guid tenantId, Guid subTaskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubTask>> GetSubTasksAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);

    Task<int> CountSubTasksAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);

    Task AddAsync(TaskItem task, CancellationToken cancellationToken);

    void Remove(TaskItem task);

    void RemoveSubTask(SubTask subTask);

    /// <summary>
    /// Clears the junction rows that point at this task or its subtasks. Both junctions use
    /// NoAction on purpose (cascading from Tasks and from SubTasks at once would give SQL
    /// Server two delete paths to the same row), so deleting a task has to do this first.
    /// </summary>
    /// <summary>Removes the task's file and tag links (and the files themselves); returns the storage keys to delete once saved.</summary>
    Task<IReadOnlyList<string>> ClearLinksForTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ClearLinksForSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken);

    Task<bool> UserExistsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<bool> UserGroupExistsAsync(Guid tenantId, Guid userGroupId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetUserSummariesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, UserGroupSummaryDto>> GetUserGroupSummariesAsync(
        IReadOnlyCollection<Guid> userGroupIds, CancellationToken cancellationToken);
}

public interface IRepetitiveTaskRepository
{
    Task<RepetitiveTask?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<RepetitiveTask?> GetByTaskIdAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);

    Task<PagedResult<RepetitiveTask>> ListAsync(ListRepetitiveTasksRequest request, CancellationToken cancellationToken);

    Task AddAsync(RepetitiveTask recurrence, CancellationToken cancellationToken);

    void Remove(RepetitiveTask recurrence);
}

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Tag?> GetByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> ListAsync(Guid tenantId, string? search, CancellationToken cancellationToken);

    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    void Remove(Tag tag);

    Task<TaskTag?> FindLinkAsync(Guid tagId, Guid? taskId, Guid? subTaskId, CancellationToken cancellationToken);

    Task AddLinkAsync(TaskTag link, CancellationToken cancellationToken);

    void RemoveLink(TaskTag link);
}

public interface ITaskFileRepository
{
    Task<TaskFileAsset?> GetAssetAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);

    Task<TaskFile?> GetLinkAsync(Guid linkId, CancellationToken cancellationToken);

    /// <summary>Whether the file hangs off a task, subtask or comment the caller may reach.</summary>
    Task<bool> IsReachableAsync(Guid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskFile>> ListForTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskFile>> ListForSubTaskAsync(Guid tenantId, Guid subTaskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskFile>> ListForCommentsAsync(Guid tenantId, IReadOnlyCollection<Guid> commentIds, CancellationToken cancellationToken);

    /// <summary>Removes a comment's file links and files; returns the storage keys to delete once saved.</summary>
    Task<IReadOnlyList<string>> ClearForCommentAsync(Guid commentId, CancellationToken cancellationToken);

    Task AddAsync(TaskFileAsset asset, TaskFile link, CancellationToken cancellationToken);

    void Remove(TaskFileAsset asset, TaskFile link);
}

public interface INoteRepository
{
    Task<Note?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Note>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task AddAsync(Note note, CancellationToken cancellationToken);

    void Remove(Note note);
}

public interface ITaskCommentRepository
{
    Task<TaskComment?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskComment>> ListForTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);

    Task AddAsync(TaskComment comment, CancellationToken cancellationToken);

    void Remove(TaskComment comment);
}

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

public interface ITaskService
{
    Task<Result<PagedResult<TaskListItemDto>>> ListAsync(ListTasksRequest request, CancellationToken cancellationToken);
    Task<Result<TaskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<TaskDto>> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<Result<TaskDto>> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<TaskDto>> ChangeStatusAsync(Guid id, ChangeTaskStatusRequest request, CancellationToken cancellationToken);
    Task<Result> AddActivityEntryAsync(Guid taskId, CreateTaskActivityRequest request, CancellationToken cancellationToken);
    Task<Result<TaskDto>> ChangePriorityAsync(Guid id, ChangeTaskPriorityRequest request, CancellationToken cancellationToken);
    Task<Result<TaskDto>> AssignUserAsync(Guid id, AssignUserRequest request, CancellationToken cancellationToken);
    Task<Result<TaskDto>> AssignUserGroupAsync(Guid id, AssignUserGroupRequest request, CancellationToken cancellationToken);

    Task<Result<SubTaskDto>> CreateSubTaskAsync(Guid taskId, CreateSubTaskRequest request, CancellationToken cancellationToken);
    Task<Result<SubTaskDto>> UpdateSubTaskAsync(Guid subTaskId, UpdateSubTaskRequest request, CancellationToken cancellationToken);
    Task<Result<SubTaskDto>> ChangeSubTaskStatusAsync(Guid subTaskId, ChangeSubTaskStatusRequest request, CancellationToken cancellationToken);
    Task<Result<SubTaskDto>> GetSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SubTaskDto>>> GetSubTasksAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result> DeleteSubTaskAsync(Guid subTaskId, CancellationToken cancellationToken);
}

public interface IRepetitiveTaskService
{
    Task<Result<PagedResult<RepetitiveTaskDto>>> ListAsync(ListRepetitiveTasksRequest request, CancellationToken cancellationToken);
    Task<Result<RepetitiveTaskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RepetitiveTaskDto>> CreateAsync(CreateRepetitiveTaskRequest request, CancellationToken cancellationToken);
    Task<Result<RepetitiveTaskDto>> UpdateAsync(Guid id, UpdateRepetitiveTaskRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RepetitiveTaskDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}

public interface ITagService
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? search, CancellationToken cancellationToken);
    Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);
    Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result> AssignToTaskAsync(Guid taskId, AssignTagRequest request, CancellationToken cancellationToken);
    Task<Result> AssignToSubTaskAsync(Guid subTaskId, AssignTagRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveFromTaskAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken);
    Task<Result> RemoveFromSubTaskAsync(Guid subTaskId, Guid tagId, CancellationToken cancellationToken);
}

public sealed record FileDownload(string FileName, string ContentType, byte[] Content);

public interface ITaskFileService
{
    Task<Result<TaskFileDto>> UploadToTaskAsync(Guid taskId, UploadFileRequest request, CancellationToken cancellationToken);
    Task<Result<TaskFileDto>> UploadToSubTaskAsync(Guid subTaskId, UploadFileRequest request, CancellationToken cancellationToken);
    Task<Result<TaskFileDto>> UploadToCommentAsync(Guid commentId, UploadFileRequest request, CancellationToken cancellationToken);
    Task<Result<FileDownload>> DownloadAsync(Guid fileId, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid linkId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TaskFileDto>>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TaskFileDto>>> GetBySubTaskIdAsync(Guid subTaskId, CancellationToken cancellationToken);
}

public interface INoteService
{
    Task<Result<IReadOnlyList<NoteDto>>> GetMyNotesAsync(CancellationToken cancellationToken);
    Task<Result<NoteDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<NoteDto>> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken);
    Task<Result<NoteDto>> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITaskCommentService
{
    Task<Result<IReadOnlyList<TaskCommentDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result<TaskCommentDto>> CreateAsync(Guid taskId, CreateTaskCommentRequest request, CancellationToken cancellationToken);
    Task<Result<TaskCommentDto>> UpdateAsync(Guid id, UpdateTaskCommentRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// A task's history, read back out of the shared AuditLog. No table of this module's own -
/// AuditLog already stores exactly what the UI's TaskLog shows.
/// </summary>
public interface ITaskActivityService
{
    Task<Result<IReadOnlyList<TaskActivityDto>>> GetForTaskAsync(Guid taskId, CancellationToken cancellationToken);

    Task RecordAsync(Guid taskId, string action, string? details, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Outbound integration contracts
// ---------------------------------------------------------------------------

/// <summary>
/// Works out when a schedule next fires. Separated from the entity so it can be unit tested
/// against the UI's Jalali week numbering (0 = Saturday) without a database.
/// </summary>
public interface IRecurrenceCalculator
{
    DateTimeOffset? CalculateNextExecution(RepetitiveTask schedule, DateTimeOffset afterUtc);

    /// <summary>A UTC moment as the wall-clock time users entered the schedule in.</summary>
    DateTimeOffset ToLocalTime(DateTimeOffset utc);
}

/// <summary>
/// TaskManagement:Recurrence. Schedules are entered in the users' wall-clock time (a date and a
/// time of day in the UI); this is the time zone they mean.
/// </summary>
public sealed class RecurrenceOptions
{
    public const string SectionName = "TaskManagement:Recurrence";

    /// <summary>IANA or Windows id. Iran has no daylight saving time since 2022.</summary>
    public string TimeZone { get; set; } = "Asia/Tehran";
}

/// <summary>
/// Sending an SMS, as this module needs it.
///
/// The module's own seam, so TaskManagement never references a gateway. The shipped
/// implementation only logs; Nexus.Integrations.TaskNotifications replaces it with one that
/// sends through the platform's SMS gateway, whose settings are per tenant.
/// </summary>
public interface ITaskSmsSender
{
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result> SendAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken);
}
