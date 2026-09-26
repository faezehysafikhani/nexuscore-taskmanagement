using Chat.Application.Abstractions;
using Chat.Domain.Entities;
using Chat.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Persistence;

public class ChatDbContext : DbContext, IChatDbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options)
        : base(options)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationParticipant> ConversationParticipants
        => Set<ConversationParticipant>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageRead> MessageReads
        => Set<MessageRead>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ConversationParticipant>()
            .HasKey(x => new
            {
                x.ConversationId,
                x.UserId
            });

        // One conversation per pair of users.
        modelBuilder.Entity<Conversation>()
            .Property(x => x.DirectKey).HasMaxLength(65);
        modelBuilder.Entity<Conversation>()
            .HasIndex(x => x.DirectKey).IsUnique().HasFilter("[DirectKey] IS NOT NULL");

        // One conversation per team.
        modelBuilder.Entity<Conversation>()
            .HasIndex(x => x.TeamId).IsUnique().HasFilter("[TeamId] IS NOT NULL");

        modelBuilder.Entity<Message>(message =>
        {
            message.Property(x => x.AttachmentFileName).HasMaxLength(260);
            message.Property(x => x.AttachmentContentType).HasMaxLength(150);
            message.Property(x => x.AttachmentStorageKey).HasMaxLength(300);
            message.HasIndex(x => new { x.ConversationId, x.SentAt });
        });

        // A message is read at most once per user; also serves the unread-count lookups.
        modelBuilder.Entity<MessageRead>()
            .HasIndex(x => new { x.MessageId, x.UserId }).IsUnique().HasFilter("[MessageId] IS NOT NULL AND [UserId] IS NOT NULL");
    }
}