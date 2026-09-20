using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Calendar.Domain;

namespace Nexus.Calendar.Infrastructure.Configurations;

public sealed class WorkCalendarConfiguration : IEntityTypeConfiguration<WorkCalendar>
{
    public void Configure(EntityTypeBuilder<WorkCalendar> builder)
    {
        builder.ToTable("WorkCalendars", "calendar");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Navigation(x => x.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Exceptions)
            .WithOne()
            .HasForeignKey(x => x.CalendarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WorkCalendarExceptionConfiguration : IEntityTypeConfiguration<WorkCalendarException>
{
    public void Configure(EntityTypeBuilder<WorkCalendarException> builder)
    {
        builder.ToTable("WorkCalendarExceptions", "calendar");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
    }
}
