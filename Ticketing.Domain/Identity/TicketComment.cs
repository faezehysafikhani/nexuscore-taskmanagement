using NexusCore.SharedKernel.Domain;

namespace Ticketing.Domain.Entities;

public class TicketComment : AuditableEntity<Guid>
{
    private TicketComment() : base(Guid.Empty) { }

    public TicketComment(
        Guid id,
        Guid ticketId,
        Guid? userId,
        string text)
        : base(id)
    {
        TicketId = ticketId;
        UserId = userId;
        Text = text;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TicketId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Text { get; private set; } = string.Empty;
}