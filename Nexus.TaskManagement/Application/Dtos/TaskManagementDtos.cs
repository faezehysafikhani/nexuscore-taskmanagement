using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Application.Dtos;

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

/// <summary>
/// A person as a task screen needs them: enough to render an avatar and a name, nothing more.
/// Projected from the shared identity tables - this module never stores a copy.
/// </summary>
public sealed record UserSummaryDto(Guid Id, string DisplayName, string? Email);

public sealed record UserGroupSummaryDto(Guid Id, string Name);

public sealed record TagDto(Guid Id, string Name, string? Color);

public sealed record TaskFileDto(
    Guid LinkId,
    Guid FileId,
    string FileName,
    string ContentType,
    int FileSizeBytes,
    Guid? UploadedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record SubTaskDto(
    Guid Id,
    Guid TaskId,
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    SubTaskImportance Importance,
    bool IsCompleted,
    int SortOrder,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<TaskFileDto> Files,
    DateTimeOffset CreatedAtUtc,
    bool IsGeneratedOccurrence = false,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

/// <summary>
/// The recurrence schedule. Carries no title, priority or assignee - those belong to the task
/// this hangs off, and duplicating them here is exactly what the design forbids.
/// </summary>
public sealed record RepetitiveTaskDto(
    Guid Id,
    Guid TaskId,
    RecurrenceFrequency Frequency,
    int? IntervalWeeks,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    IReadOnlyList<int> WeeklyDays,
    IReadOnlyList<int> MonthlyDays,
    OccurrenceNth? NthOccurrence,
    int? NthWeekday,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateTimeOffset? NextExecutionAtUtc,
    DateTimeOffset? LastExecutionAtUtc,
    bool IsActive);

public sealed record TaskCommentDto(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    string? UserDisplayName,
    string Text,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    IReadOnlyList<TaskFileDto>? Files = null);

/// <summary>
/// One entry of a task's history. Read back out of the shared AuditLog rather than a table of
/// this module's own - see TaskActivityService.
/// </summary>
public sealed record TaskActivityDto(
    Guid Id,
    Guid TaskId,
    Guid? UserId,
    string? UserDisplayName,
    string Action,
    string? Details,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// The combined shape a task screen needs: general fields from Tasks, schedule from
/// RepetitiveTasks. Combining them in the response is fine; combining them in the database is
/// not, which is why Recurrence is a nested object rather than flattened columns.
/// </summary>
public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsProject,
    bool IsRecurring,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly DueDate,
    DateTimeOffset? ActualCompletionDateUtc,
    UserSummaryDto? Owner,
    UserSummaryDto? AssignedUser,
    UserGroupSummaryDto? AssignedUserGroup,
    IReadOnlyList<UserSummaryDto> Assignees,
    bool AllowAssigneeStatusUpdate,
    string? CharterDescription,
    string? CharterProjectManager,
    DateOnly? CharterStartDate,
    DateOnly? CharterEndDate,
    IReadOnlyList<SubTaskDto> SubTasks,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<TaskFileDto> Files,
    RepetitiveTaskDto? Recurrence,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    TimeOnly? DueTime = null,
    TimeOnly? CharterStartTime = null,
    TimeOnly? CharterEndTime = null);

/// <summary>Trimmed shape for list and board views - no subtasks, files or charter.</summary>
public sealed record TaskListItemDto(
    Guid Id,
    string Title,
    bool IsProject,
    bool IsRecurring,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly DueDate,
    UserSummaryDto? AssignedUser,
    UserGroupSummaryDto? AssignedUserGroup,
    int SubTaskCount,
    int CompletedSubTaskCount,
    IReadOnlyList<TagDto> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    TimeOnly? DueTime = null);

public sealed record NoteDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Content,
    string? Color,
    bool IsPinned,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

/// <summary>
/// The subtasks supplied when a project is created. A project needs at least one, and the
/// project plus its first subtasks are written in a single transaction.
/// </summary>
public sealed record SubTaskInput(
    string Title,
    SubTaskImportance Importance = SubTaskImportance.Medium,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int SortOrder = 0,
    bool IsGeneratedOccurrence = false,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

public sealed record RecurrenceInput(
    RecurrenceFrequency Frequency,
    DateOnly StartDate,
    int? IntervalWeeks = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    IReadOnlyList<int>? WeeklyDays = null,
    IReadOnlyList<int>? MonthlyDays = null,
    OccurrenceNth? NthOccurrence = null,
    int? NthWeekday = null,
    DateOnly? EndDate = null);

public sealed record CreateTaskRequest(
    string Title,
    DateOnly DueDate,
    TaskPriority Priority = TaskPriority.Medium,
    string? Description = null,
    bool IsProject = false,
    Guid? AssignedUserId = null,
    Guid? AssignedUserGroupId = null,
    IReadOnlyList<Guid>? AssigneeUserIds = null,
    bool AllowAssigneeStatusUpdate = true,
    string? CharterDescription = null,
    string? CharterProjectManager = null,
    DateOnly? CharterStartDate = null,
    DateOnly? CharterEndDate = null,
    IReadOnlyList<SubTaskInput>? SubTasks = null,
    IReadOnlyList<string>? Tags = null,
    RecurrenceInput? Recurrence = null,
    TimeOnly? DueTime = null,
    TimeOnly? CharterStartTime = null,
    TimeOnly? CharterEndTime = null);

/// <summary>
/// Replaces the task's details. Like every other field here, DueTime is replaced as sent: null
/// means "no time of day", not "keep the current one".
/// </summary>
public sealed record UpdateTaskRequest(
    string Title,
    DateOnly DueDate,
    TaskPriority Priority,
    string? Description = null,
    bool? IsProject = null,
    Guid? AssignedUserId = null,
    Guid? AssignedUserGroupId = null,
    IReadOnlyList<Guid>? AssigneeUserIds = null,
    bool AllowAssigneeStatusUpdate = true,
    string? CharterDescription = null,
    string? CharterProjectManager = null,
    DateOnly? CharterStartDate = null,
    DateOnly? CharterEndDate = null,
    TimeOnly? DueTime = null,
    TimeOnly? CharterStartTime = null,
    TimeOnly? CharterEndTime = null);

public sealed record ChangeTaskStatusRequest(TaskItemStatus Status);

public sealed record ChangeTaskPriorityRequest(TaskPriority Priority);

public sealed record AssignUserRequest(Guid? AssignedUserId, IReadOnlyList<Guid>? AssigneeUserIds = null);

public sealed record AssignUserGroupRequest(Guid? AssignedUserGroupId);

public enum TaskSortBy
{
    CreatedAtUtc,
    ModifiedAtUtc,
    DueDate,
    Priority,
    Title
}

public sealed record ListTasksRequest(
    Guid TenantId,
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    bool? IsProject = null,
    bool? IsRecurring = null,
    Guid? AssignedUserId = null,
    Guid? AssignedUserGroupId = null,
    Guid? TagId = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    bool? Overdue = null,
    TaskSortBy SortBy = TaskSortBy.ModifiedAtUtc,
    bool SortDescending = true);

public sealed record CreateSubTaskRequest(
    string Title,
    SubTaskImportance Importance = SubTaskImportance.Medium,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int SortOrder = 0,
    bool IsGeneratedOccurrence = false,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

public sealed record UpdateSubTaskRequest(
    string Title,
    SubTaskImportance Importance,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int SortOrder = 0,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

public sealed record ChangeSubTaskStatusRequest(bool IsCompleted);

public sealed record CreateRepetitiveTaskRequest(Guid TaskId, RecurrenceInput Recurrence);

public sealed record UpdateRepetitiveTaskRequest(RecurrenceInput Recurrence);

public sealed record ListRepetitiveTasksRequest(
    Guid TenantId,
    int PageNumber = 1,
    int PageSize = 20,
    bool? IsActive = null);

public sealed record CreateTagRequest(string Name, string? Color = null);

public sealed record UpdateTagRequest(string Name, string? Color = null);

public sealed record AssignTagRequest(Guid TagId);

public sealed record CreateNoteRequest(string Title, string Content, string? Color = null, bool IsPinned = false);

public sealed record UpdateNoteRequest(string Title, string Content, string? Color = null, bool IsPinned = false);

public sealed record CreateTaskCommentRequest(string Text);

public sealed record UpdateTaskCommentRequest(string Text);

/// <summary>An entry the UI adds to a task's history, e.g. "edited 3 fields". The actor is the caller.</summary>
public sealed record CreateTaskActivityRequest(string Action, string? Details = null);

/// <summary>
/// An upload handed to the service. The service checks Content.Length itself rather than
/// trusting any client-reported size, and generates the stored name - the supplied
/// FileName is only ever kept for display.
/// </summary>
public sealed record UploadFileRequest(string FileName, string ContentType, byte[] Content);
