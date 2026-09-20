using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Domain;

public enum ActionStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled
}

/// <summary>
/// Named ActionItem, not Action, to avoid colliding with System.Action. Required references:
/// OrganizationUnitId and WorkCalendarId (validated to exist by ActionService against the
/// Organization/Calendar modules this project hard-references). ProjectId is optional and
/// deliberately just a Guid - ProjectManagement.Core is never referenced from this module, so
/// Actions works standalone (see rule: "این Module باید بدون Project Management نیز قابل استفاده باشد").
/// </summary>
public sealed class ActionItem : AuditableEntity<Guid>
{
    private ActionItem() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ActionItem(Guid id, Guid tenantId, string title, Guid organizationUnitId, Guid workCalendarId, Guid? projectId = null) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        OrganizationUnitId = organizationUnitId;
        WorkCalendarId = workCalendarId;
        ProjectId = projectId;
        Status = ActionStatus.Open;
        ApprovalStatus = ApprovalStatus.NotSubmitted;

        RaiseDomainEvent(new ActionCreated(Id, TenantId, Title, ProjectId));
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public ActionStatus Status { get; private set; }
    public Guid OrganizationUnitId { get; private set; }
    public Guid WorkCalendarId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDetails(
        string title, string? description, Guid? ownerUserId, Guid? responsibleUserId,
        Guid organizationUnitId, Guid workCalendarId, Guid? projectId,
        DateOnly? startDate, DateOnly? endDate)
    {
        Title = title.Trim();
        Description = description;
        OwnerUserId = ownerUserId;
        ResponsibleUserId = responsibleUserId;
        OrganizationUnitId = organizationUnitId;
        WorkCalendarId = workCalendarId;
        ProjectId = projectId;
        StartDate = startDate;
        EndDate = endDate;
    }

    public void ChangeStatus(ActionStatus status) => Status = status;

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
