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
    }
}
