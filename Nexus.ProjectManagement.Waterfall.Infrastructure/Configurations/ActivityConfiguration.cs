using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ManHours).HasColumnType("decimal(9,2)");
        builder.Property(x => x.Weight).HasColumnType("decimal(5,2)");
        builder.Property(x => x.PlannedProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ActualProgress).HasColumnType("decimal(5,2)");

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.ParentActivityId);
    }
}
