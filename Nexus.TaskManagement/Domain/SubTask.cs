using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>
/// A step inside a task. Its own table with a real TaskId foreign key - deliberately not a
/// self-reference on TaskItem. Carries its own files and tags rather than borrowing its
/// parent's.
/// </summary>
public sealed class SubTask : AuditableEntity<Guid>
{
    private readonly List<TaskFile> _files = [];
    private readonly List<TaskTag> _tags = [];
    private readonly List<SubTaskAssignee> _assignees = [];

    private SubTask() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public SubTask(Guid id, Guid tenantId, Guid taskId, string title, SubTaskImportance importance, int sortOrder)
        : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        Title = title.Trim();
        Importance = importance;
        SortOrder = sortOrder;
        IsCompleted = false;
        Status = SubTaskStatus.NotStarted;
    }

    public Guid TenantId { get; private set; }

    public Guid TaskId { get; private set; }
    public TaskItem? Task { get; private set; }

    public string Title { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    /// <summary>Time of day on <see cref="StartDate"/>; null when only a date was given.</summary>
    public TimeOnly? StartTime { get; private set; }

    /// <summary>Time of day on <see cref="EndDate"/>; null when only a date was given.</summary>
    public TimeOnly? EndTime { get; private set; }

    /// <summary>The UI's importance weight, rendered as "وزن ۱/۲/۳".</summary>
    public SubTaskImportance Importance { get; private set; }

    public bool IsCompleted { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>The subtask's own three-step lifecycle: not started, in progress, completed.</summary>
    public SubTaskStatus Status { get; private set; }

    /// <summary>Set once, the first time the subtask enters <see cref="SubTaskStatus.InProgress"/>. Never cleared.</summary>
    public DateTimeOffset? ActualStartedAtUtc { get; private set; }

    /// <summary>Set once, the first time the subtask enters <see cref="SubTaskStatus.Completed"/>. Never cleared.</summary>
    public DateTimeOffset? ActualCompletedAtUtc { get; private set; }

    /// <summary>
    /// True for an occurrence the client generated from the task's recurrence schedule, as opposed
    /// to a subtask a person wrote. Lets the client replace its generated occurrences when the
    /// schedule is saved again, instead of adding a second set.
    /// </summary>
    public bool IsGeneratedOccurrence { get; private set; }

    public IReadOnlyCollection<TaskFile> Files => _files.AsReadOnly();
    public IReadOnlyCollection<TaskTag> Tags => _tags.AsReadOnly();
    public IReadOnlyCollection<SubTaskAssignee> Assignees => _assignees.AsReadOnly();

    /// <summary>Exactly these users are responsible for the subtask. Caller validates the list is non-empty.</summary>
    public void SetAssignees(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        _assignees.RemoveAll(a => !ids.Contains(a.UserId));
        foreach (var userId in ids.Where(id => _assignees.All(a => a.UserId != id)))
        {
            _assignees.Add(new SubTaskAssignee(Guid.NewGuid(), Id, userId));
        }
    }

    public void UpdateDetails(string title, SubTaskImportance importance, DateOnly? startDate, DateOnly? endDate, int sortOrder)
    {
        Title = title.Trim();
        Importance = importance;
        StartDate = startDate;
        EndDate = endDate;
        SortOrder = sortOrder;
    }

    /// <summary>A time without its date means nothing, so each is kept only alongside its date.</summary>
    public void SetTimes(TimeOnly? startTime, TimeOnly? endTime)
    {
        StartTime = StartDate is null ? null : startTime;
        EndTime = EndDate is null ? null : endTime;
    }

    public void SetCompleted(bool isCompleted) => SetStatus(isCompleted ? SubTaskStatus.Completed : SubTaskStatus.NotStarted);

    /// <summary>
    /// Moves to the given state, stamping whichever of <see cref="ActualStartedAtUtc"/> /
    /// <see cref="ActualCompletedAtUtc"/> is reached for the first time. Jumping straight to
    /// Completed still stamps a start time, so a completed subtask always has both.
    /// </summary>
    public void SetStatus(SubTaskStatus status)
    {
        Status = status;
        if (status is SubTaskStatus.InProgress or SubTaskStatus.Completed)
        {
            ActualStartedAtUtc ??= DateTimeOffset.UtcNow;
        }
        if (status is SubTaskStatus.Completed)
        {
            ActualCompletedAtUtc ??= DateTimeOffset.UtcNow;
        }
        IsCompleted = status == SubTaskStatus.Completed;
    }

    /// <summary>Cycles not-started → in-progress → completed → not-started, one click at a time.</summary>
    public void Advance()
    {
        var next = Status switch
        {
            SubTaskStatus.NotStarted => SubTaskStatus.InProgress,
            SubTaskStatus.InProgress => SubTaskStatus.Completed,
            _ => SubTaskStatus.NotStarted
        };
        SetStatus(next);
    }

    public void MarkGeneratedOccurrence(bool isGenerated) => IsGeneratedOccurrence = isGenerated;
}
