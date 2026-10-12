using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

/// <summary>How the weight of each leaf activity is decided. Stored as its integer value; never renumber.</summary>
public enum WeightingMode
{
    /// <summary>The weights typed on the activities are used as they are (the original behaviour).</summary>
    Manual = 0,
    /// <summary>Each leaf weighs its share of the total duration.</summary>
    Time = 1,
    /// <summary>Each leaf weighs its share of the total cost.</summary>
    Cost = 2,
    /// <summary>Each leaf weighs its share of the total complexity score (low 1, medium 2, high 3).</summary>
    Complexity = 3,
    /// <summary>A blend of the three shares by the project's factors.</summary>
    Combined = 4
}

/// <summary>How an activity's planned progress is read from the schedule. Stored as its integer value; never renumber.</summary>
public enum PlannedProgressMode
{
    /// <summary>The planned progress stored on the activity is used as it is (the original behaviour).</summary>
    Stored = 0,
    /// <summary>Planned progress grows evenly over the activity's working days.</summary>
    Continuous = 1,
    /// <summary>Planned progress is 0 until the activity's last day has passed, then 100.</summary>
    ZeroHundred = 2
}

/// <summary>
/// How a project weighs and plans its activities. A project with no row behaves as it always did (manual weights, stored
/// planned progress) unless the host chose other defaults in <see cref="WaterfallOptions"/>.
/// </summary>
public sealed class WaterfallProjectSettings : AuditableEntity<Guid>
{
    private WaterfallProjectSettings() : base(Guid.Empty)
    {
    }

    public WaterfallProjectSettings(Guid id, Guid tenantId, Guid projectId) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public WeightingMode Weighting { get; private set; }

    /// <summary>Percent each share counts for in <see cref="WeightingMode.Combined"/>; the three add up to 100 then.</summary>
    public int TimeFactor { get; private set; }
    public int CostFactor { get; private set; }
    public int ComplexityFactor { get; private set; }

    public PlannedProgressMode PlannedProgress { get; private set; }

    public void Set(WeightingMode weighting, int timeFactor, int costFactor, int complexityFactor, PlannedProgressMode plannedProgress)
    {
        Weighting = weighting;
        TimeFactor = timeFactor;
        CostFactor = costFactor;
        ComplexityFactor = complexityFactor;
        PlannedProgress = plannedProgress;
    }
}
