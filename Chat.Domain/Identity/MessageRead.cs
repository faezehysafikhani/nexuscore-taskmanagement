using NexusCore.SharedKernel.Domain;
using static System.Net.Mime.MediaTypeNames;

namespace Chat.Domain.Entities;

public class MessageRead : AuditableEntity<Guid>
{
    public MessageRead(
        Guid id,
        Guid? messageId,
        Guid? userId)
        : base(id)
    {
        MessageId = messageId;
        UserId = userId;
    }
    public Guid? MessageId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime ReadAt { get; set; }
}