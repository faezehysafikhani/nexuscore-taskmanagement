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

    /// <summary>
    /// True for an occurrence the client generated from the task's recurrence schedule, as opposed
    /// to a subtask a person wrote. Lets the client replace its generated occurrences when the
    /// schedule is saved again, instead of adding a second set.
    /// </summary>
    public bool IsGeneratedOccurrence { get; private set; }

    public IReadOnlyCollection<TaskFile> Files => _files.AsReadOnly();
    public IReadOnlyCollection<TaskTag> Tags => _tags.AsReadOnly();

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

    public void SetCompleted(bool isCompleted) => IsCompleted = isCompleted;

    public void MarkGeneratedOccurrence(bool isGenerated) => IsGeneratedOccurrence = isGenerated;
}
