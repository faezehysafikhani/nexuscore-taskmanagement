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

    /// <summary>
    /// Everyone responsible for doing the task, each once, <see cref="AssignedUserId"/> first.
    /// Tasks saved before responsibility could be shared have only AssignedUserId.
    /// </summary>
    public IReadOnlyList<Guid> ResponsibleUserIds
    {
        get
        {
            var ids = new List<Guid>();
            if (AssignedUserId is { } primary)
            {
                ids.Add(primary);
            }

            ids.AddRange(_assignees.Where(a => a.IsResponsible && !ids.Contains(a.UserId)).Select(a => a.UserId));
            return ids;
        }
    }

    public bool IsResponsible(Guid userId) => ResponsibleUserIds.Contains(userId);

    /// <summary>
    /// Makes exactly these users responsible (none leaves the task without one - only older
    /// tasks may be like that). The first becomes <see cref="AssignedUserId"/>. Someone no
    /// longer responsible loses the access that came with it; <see cref="AssignUsers"/> decides
    /// who else may see the task.
    /// </summary>
    public void SetResponsibleUsers(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        AssignedUserId = ids.Count > 0 ? ids[0] : null;

        _assignees.RemoveAll(a => a.IsResponsible && !ids.Contains(a.UserId));
        foreach (var id in ids)
        {
            var row = _assignees.FirstOrDefault(a => a.UserId == id);
            if (row is null)
            {
                _assignees.Add(new TaskAssignee(Guid.NewGuid(), Id, id, isResponsible: true));
            }
            else
            {
                row.SetResponsible(true);
            }
        }
    }

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
        AssignedUserGroupId = assignedUserGroupId;
        AllowAssigneeStatusUpdate = allowAssigneeStatusUpdate;

        // The single-responsible form older callers use: naming one of the current responsible
        // people keeps them all (that one first); naming someone else makes them the only one.
        if (assignedUserId != AssignedUserId)
        {
            var current = ResponsibleUserIds;
            if (assignedUserId is { } named && current.Contains(named))
            {
                SetResponsibleUsers(current.Where(id => id != named).Prepend(named));
            }
            else
            {
                SetResponsibleUsers(assignedUserId is { } single ? [single] : []);
            }
        }
    }

    public void SetDueTime(TimeOnly? dueTime) => DueTime = dueTime;

    /// <summary>
    /// After an edit, announces what the responsible people need to know: each newly responsible
    /// person that the task is now theirs, and - when the due date or time moved - those who
    /// stayed responsible (a new one is told the due date anyway). Unchanged values raise
    /// nothing, so edits of other fields stay silent.
    /// </summary>
    public void RecordAssignmentAndDueChanges(IReadOnlyCollection<Guid> previousResponsible, DateOnly previousDueDate, TimeOnly? previousDueTime, Guid? changedByUserId)
    {
        var current = ResponsibleUserIds;
        var previousPrimary = previousResponsible.Count > 0 ? previousResponsible.First() : (Guid?)null;
        foreach (var added in current.Where(id => !previousResponsible.Contains(id)))
        {
            RaiseDomainEvent(new TaskItemAssigneeChanged(Id, TenantId, added, previousPrimary, changedByUserId));
        }

        var kept = current.Where(previousResponsible.Contains).ToList();
        if (kept.Count > 0 && (DueDate != previousDueDate || DueTime != previousDueTime))
        {
            RaiseDomainEvent(new TaskItemDueChanged(Id, TenantId, changedByUserId, kept));
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

    /// <summary>
    /// Sets the task's access list: exactly these users may see and work on it, besides its
    /// owner and the responsible people, who always keep access.
    /// </summary>
    public void AssignUsers(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        _assignees.RemoveAll(a => !a.IsResponsible && !ids.Contains(a.UserId));
        foreach (var userId in ids.Where(id => _assignees.All(a => a.UserId != id)))
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
