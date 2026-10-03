using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>
/// A frozen copy of a project's calculated schedule, kept so the live plan can later be compared
/// with what was originally agreed. Numbered 1, 2, 3... per project; never edited after it is
/// taken, only deleted. The per-activity rows are <see cref="ScheduleBaselineActivity"/>.
/// </summary>
public sealed class ScheduleBaseline : AuditableEntity<Guid>
{
    private ScheduleBaseline() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public ScheduleBaseline(
        Guid id, Guid tenantId, Guid projectId, int number, string name, string? note,
        DateOnly projectStart, DateOnly projectFinish) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Number = number;
        Name = name.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ProjectStart = projectStart;
        ProjectFinish = projectFinish;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }

    /// <summary>1-based and unique within the project. A new baseline takes the highest existing
    /// number plus one, so a gap left by deleting a middle baseline is not refilled (only the
    /// number of the most recent one can come back).</summary>
    public int Number { get; private set; }

    public string Name { get; private set; }
    public string? Note { get; private set; }
    public DateOnly ProjectStart { get; private set; }
    public DateOnly ProjectFinish { get; private set; }
}

/// <summary>One activity's dates as they stood when the baseline was taken. ActivityId is a bare
/// Guid, not a navigation: the activity may later be renamed, moved or deleted, and the baseline
/// must still describe it as it was.</summary>
public sealed class ScheduleBaselineActivity : Entity<Guid>
{
    private ScheduleBaselineActivity() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public ScheduleBaselineActivity(
        Guid id, Guid baselineId, Guid activityId, Guid? parentActivityId, string name,
        bool isSummary, bool isMilestone, DateOnly startDate, DateOnly endDate, int durationDays) : base(id)
    {
        BaselineId = baselineId;
        ActivityId = activityId;
        ParentActivityId = parentActivityId;
        Name = name;
        IsSummary = isSummary;
        IsMilestone = isMilestone;
        StartDate = startDate;
        EndDate = endDate;
        DurationDays = durationDays;
    }

    public Guid BaselineId { get; private set; }
    public Guid ActivityId { get; private set; }
    public Guid? ParentActivityId { get; private set; }
    public string Name { get; private set; }
    public bool IsSummary { get; private set; }
    public bool IsMilestone { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>Working days of the calendar in force when the baseline was taken.</summary>
    public int DurationDays { get; private set; }
}
