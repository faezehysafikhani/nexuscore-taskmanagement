using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class ProgressSnapshotConfiguration : IEntityTypeConfiguration<ProgressSnapshot>
{
    public void Configure(EntityTypeBuilder<ProgressSnapshot> builder)
    {
        builder.ToTable("ProgressSnapshots", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PlannedProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ActualProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Note).HasMaxLength(1000);

        // One snapshot per project per date; a second one for the same date refreshes the first.
        builder.HasIndex(x => new { x.ProjectId, x.SnapshotDate }).IsUnique();
    }
}
