using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// The single home for every kind of task: a plain task, a project (IsProject) and a recurring
/// task (the one that owns a <see cref="RepetitiveTask"/> schedule) all live in this table.
///
/// Cross-module policy: User and UserGroup are NexusCore *shared* infrastructure, not another
/// business module, so this entity holds real foreign keys and navigation properties into the
/// identity schema. It holds no reference of any kind into another business module (Chat,
/// Ticketing, Notifications, Events, ProjectManagement, ...).
/// </summary>
public sealed class TaskItem : AuditableEntity<Guid>
{
    private readonly List<SubTask> _subTasks = [];
    private readonly List<TaskAssignee> _assignees = [];
    private readonly List<TaskFile> _files = [];
    private readonly List<TaskTag> _tags = [];

    private TaskItem() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public TaskItem(
        Guid id,
        Guid tenantId,
        string title,
        DateOnly dueDate,
        TaskPriority priority,
        bool isProject,
        Guid? ownerUserId = null,
        string? description = null) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        IsProject = isProject;
        OwnerUserId = ownerUserId;
        Status = TaskItemStatus.Todo;
        AllowAssigneeStatusUpdate = true;

        RaiseDomainEvent(new TaskItemCreated(Id, TenantId, Title, IsProject));
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }

    /// <summary>false = a plain task, true = a project. A project must always hold at least one SubTask.</summary>
    public bool IsProject { get; private set; }

    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }

    public DateOnly DueDate { get; private set; }

    /// <summary>
    /// The time of day on <see cref="DueDate"/> the task is due, as the user picked it (wall-clock
    /// time, like DueDate is a calendar date). Null when only a date was given; 00:00 is a real
    /// time, not "no time".
    /// </summary>
    public TimeOnly? DueTime { get; private set; }

    public DateTimeOffset? ActualCompletionDateUtc { get; private set; }

    public Guid? OwnerUserId { get; private set; }
    public User? OwnerUser { get; private set; }

    public Guid? AssignedUserId { get; private set; }
    public User? AssignedUser { get; private set; }

    /// <summary>The UI calls this a "Team"; in NexusCore it is a UserGroup.</summary>
    public Guid? AssignedUserGroupId { get; private set; }
    public UserGroup? AssignedUserGroup { get; private set; }

    public bool AllowAssigneeStatusUpdate { get; private set; }

    // Project charter - the UI's projectCharter object, flattened.
    public string? CharterDescription { get; private set; }
    public string? CharterProjectManager { get; private set; }
    public DateOnly? CharterStartDate { get; private set; }
    public DateOnly? CharterEndDate { get; private set; }

    /// <summary>Wall-clock times on the charter dates, like <see cref="DueTime"/>; null when only a date was given.</summary>
    public TimeOnly? CharterStartTime { get; private set; }
    public TimeOnly? CharterEndTime { get; private set; }

    /// <summary>
    /// The recurrence schedule, when this task repeats. Null for a one-off task, so
    /// "is this task recurring" is answered by this being non-null rather than by a
    /// separate flag that could drift out of step with it.
    /// </summary>
    public RepetitiveTask? Recurrence { get; private set; }

    public IReadOnlyCollection<SubTask> SubTasks => _subTasks.AsReadOnly();
    public IReadOnlyCollection<TaskAssignee> Assignees => _assignees.AsReadOnly();
    public IReadOnlyCollection<TaskFile> Files => _files.AsReadOnly();
    public IReadOnlyCollection<TaskTag> Tags => _tags.AsReadOnly();

    public bool IsRecurring => Recurrence is not null;

    public void UpdateDetails(
        string title,
        string? description,
        DateOnly dueDate,
        TaskPriority priority,
        Guid? assignedUserId,
        Guid? assignedUserGroupId,
        bool allowAssigneeStatusUpdate)
    {
        Title = title.Trim();
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        AssignedUserId = assignedUserId;
        AssignedUserGroupId = assignedUserGroupId;
        AllowAssigneeStatusUpdate = allowAssigneeStatusUpdate;
    }

    public void SetDueTime(TimeOnly? dueTime) => DueTime = dueTime;

    /// <summary>
    /// After an edit, announces what the responsible person needs to know: that the task is now
    /// theirs, or - when they keep it - that its due date or time moved. Unchanged values raise
    /// nothing, so edits of other fields stay silent.
    /// </summary>
    public void RecordAssignmentAndDueChanges(Guid? previousAssignedUserId, DateOnly previousDueDate, TimeOnly? previousDueTime, Guid? changedByUserId)
    {
        if (AssignedUserId is not { } assignee)
        {
            return;
        }

        if (assignee != previousAssignedUserId)
        {
            RaiseDomainEvent(new TaskItemAssigneeChanged(Id, TenantId, assignee, previousAssignedUserId, changedByUserId));
        }
        else if (DueDate != previousDueDate || DueTime != previousDueTime)
        {
            RaiseDomainEvent(new TaskItemDueChanged(Id, TenantId, changedByUserId));
        }
    }

    public void UpdateCharter(string? description, string? projectManager, DateOnly? startDate, DateOnly? endDate)
    {
        CharterDescription = description;
        CharterProjectManager = projectManager;
        CharterStartDate = startDate;
        CharterEndDate = endDate;
    }

    public void SetCharterTimes(TimeOnly? startTime, TimeOnly? endTime)
    {
        CharterStartTime = startTime;
        CharterEndTime = endTime;
    }

    public void ChangeStatus(TaskItemStatus status, DateTimeOffset nowUtc)
    {
        Status = status;
        ActualCompletionDateUtc = status == TaskItemStatus.Completed
            ? ActualCompletionDateUtc ?? nowUtc
            : null;
    }

    /// <summary>
    /// Promoting a plain task to a project is only valid once it has a subtask, which the
    /// service checks before calling this. Demoting to a plain task is always allowed.
    /// </summary>
    public void SetIsProject(bool isProject) => IsProject = isProject;

    public SubTask AddSubTask(Guid id, string title, SubTaskImportance importance, int sortOrder)
    {
        var subTask = new SubTask(id, TenantId, Id, title, importance, sortOrder);
        _subTasks.Add(subTask);
        return subTask;
    }

    public void AssignUsers(IEnumerable<Guid> userIds)
    {
        _assignees.Clear();
        foreach (var userId in userIds.Distinct())
        {
            _assignees.Add(new TaskAssignee(Guid.NewGuid(), Id, userId));
        }
    }

    public void AttachRecurrence(RepetitiveTask recurrence) => Recurrence = recurrence;

    public void ClearRecurrence() => Recurrence = null;

    /// <summary>
    /// Announces that this task's recurrence schedule has come due. Called by the scheduler
    /// after it has already claimed the occurrence, so the event only ever describes work
    /// that is committed.
    /// </summary>
    public void RaiseRecurrenceDue(Guid repetitiveTaskId, DateTimeOffset dueAtUtc) =>
        RaiseDomainEvent(new RepetitiveTaskDue(repetitiveTaskId, Id, TenantId, dueAtUtc));
}
