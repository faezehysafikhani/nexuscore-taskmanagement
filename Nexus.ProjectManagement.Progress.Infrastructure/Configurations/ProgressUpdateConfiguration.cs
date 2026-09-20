using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure.Configurations;

public sealed class ProgressUpdateConfiguration : IEntityTypeConfiguration<ProgressUpdate>
{
    public void Configure(EntityTypeBuilder<ProgressUpdate> builder)
    {
        builder.ToTable("ProgressUpdates", "progress");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StatusDescription).HasMaxLength(2000);
        builder.Property(x => x.DelayReasons).HasMaxLength(2000);
        builder.Property(x => x.PlannedProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ActualProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ConfirmedProgress).HasColumnType("decimal(5,2)");
        builder.Ignore(x => x.Deviation);
        builder.Ignore(x => x.PerformanceClassification);

        builder.HasIndex(x => x.ProjectId);
    }
}
