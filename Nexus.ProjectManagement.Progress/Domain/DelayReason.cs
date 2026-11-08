using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Progress.Domain;

/// <summary>Stored as its integer value; never reorder or renumber the members.</summary>
public enum DelayRootCause
{
    Funding = 0,
    Procurement = 1,
    Permits = 2,
    HumanResources = 3,
    Other = 4
}

/// <summary>
/// A structured reason a project is behind schedule: what caused it, how much time and money it
/// cost, and what is being done about it. This is the register behind the project's "delay
/// reasons" list; the free-text <see cref="ProgressUpdate.DelayReasons"/> on a progress update
/// is a separate, older field and is left as it was. Same optional-approval pattern as the
/// other progress records.
/// </summary>
public sealed class DelayReason : AuditableEntity<Guid>
{
    private DelayReason() : base(Guid.Empty)
    {
        Description = string.Empty;
    }

    public DelayReason(Guid id, Guid tenantId, Guid projectId, DateOnly registerDate, DelayRootCause rootCause, string description) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        RegisterDate = registerDate;
        RootCause = rootCause;
        Description = description.Trim();
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly RegisterDate { get; private set; }
    public DelayRootCause RootCause { get; private set; }
    public string Description { get; private set; }

    /// <summary>Working or calendar days lost to this cause - the caller decides which; the
    /// module only stores the number.</summary>
    public int? TimeImpactDays { get; private set; }

    /// <summary>Money lost to this cause, in rials.</summary>
    public decimal? CostImpact { get; private set; }

    public string? CorrectiveAction { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(
        DateOnly registerDate, DelayRootCause rootCause, string description,
        int? timeImpactDays, decimal? costImpact, string? correctiveAction)
    {
        RegisterDate = registerDate;
        RootCause = rootCause;
        Description = description.Trim();
        TimeImpactDays = timeImpactDays;
        CostImpact = costImpact;
        CorrectiveAction = correctiveAction;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
