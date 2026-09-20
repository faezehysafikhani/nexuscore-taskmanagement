using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Core.Domain;

/// <summary>
/// The one entity ProjectManagement.Core owns. Only Guid references to OrganizationUnit,
/// WorkCalendar, and Users - no navigation properties, no FK into other modules' tables.
/// Approval is an integration point only (see NexusCore.Application.Approvals): this entity
/// knows nothing about Workflow.
/// </summary>
public sealed class Project : AuditableEntity<Guid>
{
    private Project() : base(Guid.Empty)
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public Project(
        Guid id,
        Guid tenantId,
        string name,
        string code,
        ProjectType type,
        Guid? managerUserId = null,
        Guid? ownerUserId = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Code = code.Trim();
        Type = type;
        ManagerUserId = managerUserId;
        OwnerUserId = ownerUserId;
        Status = ProjectStatus.Draft;
        ApprovalStatus = ApprovalStatus.NotSubmitted;

        RaiseDomainEvent(new ProjectCreated(Id, TenantId, Name, Type));
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public ProjectType Type { get; private set; }
    public ProjectStatus Status { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public Guid? OwnerUserId { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public Guid? OrganizationUnitId { get; private set; }
    public Guid? WorkCalendarId { get; private set; }

    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public decimal? Cost { get; private set; }

    public string? Goal { get; private set; }
    public string? Requirements { get; private set; }
    public string? Constraints { get; private set; }
    public string? Assumptions { get; private set; }
    public string? Description { get; private set; }
    public string? Charter { get; private set; }

    public void UpdateDetails(
        string name,
        string code,
        Guid? managerUserId,
        Guid? ownerUserId,
        Guid? organizationUnitId,
        Guid? workCalendarId,
        DateOnly? startDate,
        DateOnly? endDate,
        decimal? cost,
        string? goal,
        string? requirements,
        string? constraints,
        string? assumptions,
        string? description,
        string? charter)
    {
        Name = name.Trim();
        Code = code.Trim();
        ManagerUserId = managerUserId;
        OwnerUserId = ownerUserId;
        OrganizationUnitId = organizationUnitId;
        WorkCalendarId = workCalendarId;
        StartDate = startDate;
        EndDate = endDate;
        Cost = cost;
        Goal = goal;
        Requirements = requirements;
        Constraints = constraints;
        Assumptions = assumptions;
        Description = description;
        Charter = charter;
    }

    public void ChangeStatus(ProjectStatus status) => Status = status;

    public void Archive() => Status = ProjectStatus.Archived;

    /// <summary>Called by the service once a Workflow-backed IApprovalRequester accepts the submission.</summary>
    public void MarkPendingApproval()
    {
        ApprovalStatus = ApprovalStatus.PendingApproval;
        RaiseDomainEvent(new ProjectSubmittedForApproval(Id, TenantId));
    }

    /// <summary>
    /// Reached either directly (business-rule default when no approval backend is installed)
    /// or via the ApprovalGranted event handler once Workflow decides.
    /// </summary>
    public void Approve()
    {
        ApprovalStatus = ApprovalStatus.Approved;
        if (Status == ProjectStatus.Draft)
        {
            Status = ProjectStatus.Active;
        }
    }

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
