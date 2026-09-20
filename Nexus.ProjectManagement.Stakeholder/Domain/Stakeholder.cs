using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

// Deliberately Nexus.ProjectManagement.StakeholderManagement, not ...Stakeholder - see
// Nexus.ProjectManagement.RiskManagement.Domain.Risk for why nesting a type under an
// identically-named namespace segment breaks unqualified references to it.
namespace Nexus.ProjectManagement.StakeholderManagement.Domain;

public enum PowerLevel { Low, Medium, High }
public enum InterestLevel { Low, Medium, High }

public sealed class Stakeholder : AuditableEntity<Guid>
{
    private Stakeholder() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public Stakeholder(Guid id, Guid tenantId, Guid projectId, string name, bool isInternal) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Name = name.Trim();
        IsInternal = isInternal;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; }
    public bool IsInternal { get; private set; }
    public string? Expectations { get; private set; }
    public string? Notes { get; private set; }
    public PowerLevel Power { get; private set; }
    public InterestLevel Interest { get; private set; }
    public string? EngagementStrategy { get; private set; }
    public string? Requirements { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(
        string name, bool isInternal, string? expectations, string? notes,
        PowerLevel power, InterestLevel interest, string? engagementStrategy, string? requirements)
    {
        Name = name.Trim();
        IsInternal = isInternal;
        Expectations = expectations;
        Notes = notes;
        Power = power;
        Interest = interest;
        EngagementStrategy = engagementStrategy;
        Requirements = requirements;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
