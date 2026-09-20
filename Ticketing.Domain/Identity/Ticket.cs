using NexusCore.SharedKernel.Domain;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

public class Ticket : AuditableEntity<Guid>
{
    private Ticket() : base(Guid.Empty) { }

    public Ticket(
        Guid id,
        Guid? tenantId,
        string title,
        string description,
        TicketPriority priority,
        Guid? createdByUserId)
        : base(id)
    {
        TenantId = tenantId;
        Title = title;
        Description = description;
        Priority = priority;
        Status = TicketStatus.Open;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid? TenantId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public Guid? AssignedToUserId { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public void SetNumber(string number)
    {
        Number = number;
    }

    public void Assign(Guid userId)
    {
        AssignedToUserId = userId;

        if (Status == TicketStatus.Open)
            Status = TicketStatus.InProgress;
    }

    public void ChangeStatus(TicketStatus status)
    {
        Status = status;

        if (status == TicketStatus.Resolved)
            ResolvedAt = DateTime.UtcNow;

        if (status == TicketStatus.Open || status == TicketStatus.InProgress)
            ResolvedAt = null;
    }

    public void ChangePriority(TicketPriority priority)
    {
        Priority = priority;
    }
}