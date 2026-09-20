using Chat.Domain.Entities;
using Chat.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;

namespace Chat.Application.Abstractions;

public interface IChatDbContext
{
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationParticipant> ConversationParticipants { get; }
    DbSet<Message> Messages { get; }
    DbSet<MessageRead> MessageReads { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}