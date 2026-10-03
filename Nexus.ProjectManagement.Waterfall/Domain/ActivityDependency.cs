using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum DependencyType
{
    /// <summary>The successor starts after the predecessor finishes (the usual case).</summary>
    FinishToStart = 0,
    /// <summary>The successor starts after the predecessor starts.</summary>
    StartToStart = 1,
    /// <summary>The successor finishes after the predecessor finishes.</summary>
    FinishToFinish = 2,
    /// <summary>The successor finishes after the predecessor starts.</summary>
    StartToFinish = 3
}

/// <summary>
/// "Successor cannot begin/end until predecessor begins/ends", plus an optional lag in working
/// days (negative = lead, the successor may overlap). Both ends are leaf activities of the same
/// project; a project never holds two links between the same pair, nor a cycle.
/// </summary>
public sealed class ActivityDependency : AuditableEntity<Guid>
{
    private ActivityDependency() : base(Guid.Empty)
    {
    }

    public ActivityDependency(
        Guid id, Guid tenantId, Guid projectId, Guid predecessorActivityId, Guid successorActivityId,
        DependencyType type, int lagDays) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        PredecessorActivityId = predecessorActivityId;
        SuccessorActivityId = successorActivityId;
        Type = type;
        LagDays = lagDays;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid PredecessorActivityId { get; private set; }
    public Guid SuccessorActivityId { get; private set; }
    public DependencyType Type { get; private set; }

    /// <summary>Working days between the two anchor points; negative means the successor may start earlier.</summary>
    public int LagDays { get; private set; }

    public void Update(DependencyType type, int lagDays)
    {
        Type = type;
        LagDays = lagDays;
    }
}
