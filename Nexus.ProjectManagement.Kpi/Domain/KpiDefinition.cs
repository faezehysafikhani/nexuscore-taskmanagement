using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Kpi.Domain;

/// <summary>Named KpiDefinition, not Kpi, to keep the module folder/namespace segment "Kpi"
/// free of a same-named type (see Risk's namespace note for why that matters).</summary>
public enum KpiType
{
    Lag,
    Lead
}

public sealed class KpiDefinition : AuditableEntity<Guid>
{
    private KpiDefinition() : base(Guid.Empty)
    {
        Description = string.Empty;
    }

    public KpiDefinition(Guid id, Guid tenantId, Guid projectId, Guid deliverableId, KpiType type, string description) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        DeliverableId = deliverableId;
        Type = type;
        Description = description.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DeliverableId { get; private set; }
    public KpiType Type { get; private set; }
    public string Description { get; private set; }
    public string? Formula { get; private set; }
    public decimal? TargetValue { get; private set; }

    public void UpdateDetails(string description, string? formula, decimal? targetValue)
    {
        Description = description.Trim();
        Formula = formula;
        TargetValue = targetValue;
    }
}
