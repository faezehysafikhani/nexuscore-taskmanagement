using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class ScheduleBaselineConfiguration : IEntityTypeConfiguration<ScheduleBaseline>
{
    public void Configure(EntityTypeBuilder<ScheduleBaseline> builder)
    {
        builder.ToTable("ScheduleBaselines", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(2000);

        // A project never has two baselines with the same number.
        builder.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
    }
}

public sealed class ScheduleBaselineActivityConfiguration : IEntityTypeConfiguration<ScheduleBaselineActivity>
{
    public void Configure(EntityTypeBuilder<ScheduleBaselineActivity> builder)
    {
        builder.ToTable("ScheduleBaselineActivities", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.BaselineId);
    }
}
