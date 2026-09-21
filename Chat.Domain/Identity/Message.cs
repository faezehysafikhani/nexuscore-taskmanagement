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

        /// <summary>Optional attachment, stored through the platform file storage.</summary>
        public string? AttachmentFileName { get; private set; }
        public string? AttachmentContentType { get; private set; }
        public long? AttachmentSizeBytes { get; private set; }
        public string? AttachmentStorageKey { get; private set; }

        public bool HasAttachment => AttachmentStorageKey is not null;

        public void AttachFile(string fileName, string contentType, long sizeBytes, string storageKey)
        {
            AttachmentFileName = fileName;
            AttachmentContentType = contentType;
            AttachmentSizeBytes = sizeBytes;
            AttachmentStorageKey = storageKey;
        }

        public void Edit(string text)
        {
            if (IsDeleted)
            {
                throw new InvalidOperationException("A deleted message cannot be edited.");
            }

            Text = text;
            EditedAt = DateTime.UtcNow;
        }

        public void Delete()
        {
            IsDeleted = true;
        }
    }
}
