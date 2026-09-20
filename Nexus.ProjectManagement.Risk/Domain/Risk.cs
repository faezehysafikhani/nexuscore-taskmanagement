using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

// Deliberately Nexus.ProjectManagement.RiskManagement, not ...Risk: nesting a type named Risk
// under a namespace segment also named Risk makes the unqualified identifier "Risk" resolve to
// the enclosing namespace itself (found while walking the enclosing-namespace chain, before
// using-directives are even consulted), not the type - a real CS0118 everywhere it's used
// unqualified. Physical folder/project names stay Nexus.ProjectManagement.Risk(.Infrastructure)
// to match the target tree; only the C# namespace differs.
namespace Nexus.ProjectManagement.RiskManagement.Domain;

/// <summary>
/// Required: ProjectManagement.Core (ProjectId). Optional: Workflow (via IApprovalRequester)
/// and AI (via IRiskAnalyzer) - this module is fully usable with neither installed: submitting
/// a risk for approval falls back to the direct-approve business rule (see RiskService).
/// </summary>
public sealed class Risk : AuditableEntity<Guid>
{
    private Risk() : base(Guid.Empty)
    {
        Description = string.Empty;
    }

    public Risk(Guid id, Guid tenantId, Guid projectId, string description, int probabilityScore, int severityScore, int impactScore) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Description = description.Trim();
        ProbabilityScore = probabilityScore;
        SeverityScore = severityScore;
        ImpactScore = impactScore;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Description { get; private set; }
    public int ProbabilityScore { get; private set; }
    public int SeverityScore { get; private set; }
    public int ImpactScore { get; private set; }
    public int Rpn => ProbabilityScore * SeverityScore * ImpactScore;
    public string? ResponsePlan { get; private set; }
    public Guid? RiskOwnerUserId { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(string description, int probabilityScore, int severityScore, int impactScore, string? responsePlan, Guid? riskOwnerUserId)
    {
        Description = description.Trim();
        ProbabilityScore = probabilityScore;
        SeverityScore = severityScore;
        ImpactScore = impactScore;
        ResponsePlan = responsePlan;
        RiskOwnerUserId = riskOwnerUserId;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
