using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum SprintEventType
{
    /// <summary>A task (with its points) became part of the sprint's scope.</summary>
    ScopeAdded = 0,
    /// <summary>A task left the sprint's scope (moved elsewhere, or re-estimated: removed at the old size, added at the new).</summary>
    ScopeRemoved = 1,
    /// <summary>A task in the sprint became Done.</summary>
    Completed = 2,
    /// <summary>A Done task in the sprint went back to an earlier status.</summary>
    Reopened = 3,
    /// <summary>The sprint was completed with this task unfinished and it was moved on; informational, scope is unchanged.</summary>
    CarriedOver = 4
}

/// <summary>
/// One change to a sprint's scope or progress, with the points involved and when it happened.
/// Nothing else records this history - a task only knows its current status and sprint - so the
/// burn-up/burn-down charts and velocity are built by replaying these. An event is never edited.
/// </summary>
public sealed class SprintEvent : AuditableEntity<Guid>
{
    private SprintEvent() : base(Guid.Empty)
    {
    }

    public SprintEvent(
        Guid id, Guid tenantId, Guid projectId, int sprintNumber, Guid taskId,
        SprintEventType type, int points, DateTimeOffset occurredAtUtc) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        SprintNumber = sprintNumber;
        TaskId = taskId;
        Type = type;
        Points = points;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int SprintNumber { get; private set; }
    public Guid TaskId { get; private set; }
    public SprintEventType Type { get; private set; }

    /// <summary>The task's story points when this happened (0 when unestimated); never negative.</summary>
    public int Points { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
