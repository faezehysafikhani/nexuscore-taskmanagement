using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chat.Domain.Identity
{
    public class ConversationParticipant
    {
        public Guid ConversationId { get; set; }
        public Guid? UserId { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool IsAdmin { get; set; }
    }
}
