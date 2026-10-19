using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Kpi.Domain;

/// <summary>One measured value of a <see cref="KpiDefinition"/> on a date. The definition only says what is
/// measured and its target; the dashboards need the values, so a KPI can be compared with its target and followed over time.
/// Who recorded it is the audit field CreatedByUserId. Several values on one date are allowed (a correction is an edit).</summary>
public sealed class KpiMeasurement : AuditableEntity<Guid>
{
    private KpiMeasurement() : base(Guid.Empty)
    {
    }

    public KpiMeasurement(Guid id, Guid tenantId, Guid projectId, Guid kpiDefinitionId, DateOnly measuredOn, decimal value, string? note) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        KpiDefinitionId = kpiDefinitionId;
        Update(measuredOn, value, note);
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid KpiDefinitionId { get; private set; }
    public DateOnly MeasuredOn { get; private set; }
    public decimal Value { get; private set; }
    public string? Note { get; private set; }

    public void Update(DateOnly measuredOn, decimal value, string? note)
    {
        MeasuredOn = measuredOn;
        Value = value;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
