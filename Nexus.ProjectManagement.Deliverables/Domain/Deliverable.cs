using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Deliverables.Domain;

public enum DeliverableStatus
{
    Planned,
    InProgress,
    Delivered,
    Accepted,
    Rejected
}

public sealed class Deliverable : AuditableEntity<Guid>
{
    private Deliverable() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Deliverable(Guid id, Guid tenantId, Guid projectId, string title) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Title = title.Trim();
        Status = DeliverableStatus.Planned;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public string? AcceptanceCriteria { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public DeliverableStatus Status { get; private set; }

    public void UpdateDetails(string title, string? description, string? acceptanceCriteria, Guid? responsibleUserId, DateOnly? targetDate)
    {
        Title = title.Trim();
        Description = description;
        AcceptanceCriteria = acceptanceCriteria;
        ResponsibleUserId = responsibleUserId;
        TargetDate = targetDate;
    }

    public void ChangeStatus(DeliverableStatus status) => Status = status;
}
