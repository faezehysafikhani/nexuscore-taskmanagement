using Chat.Domain.Enums;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chat.Domain.Entities
{
    public class Conversation : AuditableEntity<Guid>
    {
        public Conversation(
            Guid id,
            Guid? tenantId,
            string? title,
            ChatType type,
            Guid? createdBy)
            : base(id)
        {
            TenantId = tenantId;
            Title = title?.Trim();
            Type = type;
            CreatedBy = createdBy;
            CreatedAt = DateTime.UtcNow;
        }

        public Guid? TenantId { get; private set; }
        public string? Title { get; private set; }
        public ChatType Type { get; private set; }
        public Guid? CreatedBy { get; private set; }
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// For a direct conversation: both participant ids in a fixed order, so the pair has
        /// exactly one conversation (unique index). Null for group conversations.
        /// </summary>
        public string? DirectKey { get; private set; }

        public static Conversation CreateDirect(Guid id, Guid? tenantId, Guid firstUserId, Guid secondUserId)
        {
            var conversation = new Conversation(id, tenantId, null, ChatType.Direct, firstUserId);
            conversation.DirectKey = BuildDirectKey(firstUserId, secondUserId);
            return conversation;
        }

        public static string BuildDirectKey(Guid a, Guid b)
        {
            var (low, high) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            return $"{low:N}:{high:N}";
        }
    }
}
