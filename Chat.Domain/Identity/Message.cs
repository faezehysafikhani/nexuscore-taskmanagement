using Chat.Domain.Enums;
using NexusCore.SharedKernel.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chat.Domain.Identity
{
    public class Message : AuditableEntity<Guid>
    {
        public Message(
        Guid id,
        Guid conversationId,
        Guid? senderUserId,
        string text)
        : base(id)
        {
            ConversationId = conversationId;
            SenderUserId = senderUserId;
            Text = text;
            SentAt = DateTime.UtcNow;
            IsDeleted = false;
        }

        public Guid ConversationId { get; private set; }

        public Guid? SenderUserId { get; private set; }

        public string Text { get; private set; }

        public DateTime SentAt { get; private set; }

        public DateTime? EditedAt { get; private set; }

        public bool IsDeleted { get; private set; }
        public void Edit(string text)
        {
            Text = text;
            EditedAt = DateTime.UtcNow;
        }

        public void Delete()
        {
            IsDeleted = true;
        }
    }
}
