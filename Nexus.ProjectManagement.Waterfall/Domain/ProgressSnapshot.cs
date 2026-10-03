using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>
/// The project's overall progress as it stood on one date: what the schedule said should be done
/// (planned) and what the entered progress rolled up to (actual). A project's progress history
/// is not stored anywhere else, so these snapshots - taken periodically - are what give the
/// S-curve its "actual" line. One per project per date; taking another for the same date
/// refreshes it.
/// </summary>
public sealed class ProgressSnapshot : AuditableEntity<Guid>
{
    private ProgressSnapshot() : base(Guid.Empty)
    {
    }

    public ProgressSnapshot(
        Guid id, Guid tenantId, Guid projectId, DateOnly snapshotDate,
        decimal plannedProgress, decimal actualProgress, string? note) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        SnapshotDate = snapshotDate;
        PlannedProgress = plannedProgress;
        ActualProgress = actualProgress;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly SnapshotDate { get; private set; }

    /// <summary>Percent the schedule says should be complete by the end of SnapshotDate.</summary>
    public decimal PlannedProgress { get; private set; }

    /// <summary>Percent complete according to the entered activity progress, rolled up.</summary>
    public decimal ActualProgress { get; private set; }

    public string? Note { get; private set; }

    public void Refresh(decimal plannedProgress, decimal actualProgress, string? note)
    {
        PlannedProgress = plannedProgress;
        ActualProgress = actualProgress;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
