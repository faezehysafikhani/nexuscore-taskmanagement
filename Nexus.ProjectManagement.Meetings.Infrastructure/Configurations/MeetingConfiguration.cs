using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Meetings.Domain;

namespace Nexus.ProjectManagement.Meetings.Infrastructure.Configurations;

public sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("Meetings", "meetings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Agenda).HasMaxLength(4000);
        builder.Property(x => x.Location).HasMaxLength(300);
        builder.Property(x => x.OnlineLink).HasMaxLength(500);
        builder.Property(x => x.Minutes).HasMaxLength(20000);
        builder.Navigation(x => x.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Participants)
            .WithOne()
            .HasForeignKey(x => x.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.MeetingDate });
    }
}

public sealed class MeetingParticipantConfiguration : IEntityTypeConfiguration<MeetingParticipant>
{
    public void Configure(EntityTypeBuilder<MeetingParticipant> builder)
    {
        builder.ToTable("MeetingParticipants", "meetings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.MeetingId, x.UserId }).IsUnique();
    }
}
