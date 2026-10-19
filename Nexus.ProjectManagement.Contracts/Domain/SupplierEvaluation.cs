using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>
/// One periodic assessment of a supplier - over all its contracts, or over one of them - for a period. The evaluator gives four scores
/// from 1 to 5 (quality, timeliness, compliance with obligations, cooperation); OverallScore is their average. Next to them it keeps what the
/// records said about that period when it was made (milestones due and late, obligations due and missed), so a later look sees the
/// evaluation with the facts it was based on, even after those records change.
/// </summary>
public sealed class SupplierEvaluation : AuditableEntity<Guid>
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    private SupplierEvaluation() : base(Guid.Empty)
    {
    }

    public SupplierEvaluation(Guid id, Guid tenantId, Guid supplierId) : base(id)
    {
        TenantId = tenantId;
        SupplierId = supplierId;
    }

    public Guid TenantId { get; private set; }
    public Guid SupplierId { get; private set; }

    /// <summary>The one contract assessed, or null for the supplier's work across its contracts.</summary>
    public Guid? ContractId { get; private set; }

    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }

    public int QualityScore { get; private set; }
    public int TimelinessScore { get; private set; }
    public int ComplianceScore { get; private set; }
    public int CooperationScore { get; private set; }

    /// <summary>The average of the four scores, to two decimals.</summary>
    public decimal OverallScore { get; private set; }

    // What the records said about the period when the evaluation was saved.
    public int MilestonesInPeriod { get; private set; }
    public int LateMilestones { get; private set; }
    public int ObligationsInPeriod { get; private set; }
    public int MissedObligations { get; private set; }

    public string? Notes { get; private set; }

    public void Update(
        Guid? contractId, DateOnly periodStart, DateOnly periodEnd, int quality, int timeliness, int compliance, int cooperation, string? notes)
    {
        ContractId = contractId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        QualityScore = quality;
        TimelinessScore = timeliness;
        ComplianceScore = compliance;
        CooperationScore = cooperation;
        OverallScore = Math.Round((quality + timeliness + compliance + cooperation) / 4m, 2, MidpointRounding.AwayFromZero);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetMetrics(int milestonesInPeriod, int lateMilestones, int obligationsInPeriod, int missedObligations)
    {
        MilestonesInPeriod = milestonesInPeriod;
        LateMilestones = lateMilestones;
        ObligationsInPeriod = obligationsInPeriod;
        MissedObligations = missedObligations;
    }
}
