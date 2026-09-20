using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Integrations.StrategyAlignment.Domain;

namespace Nexus.Integrations.StrategyAlignment.Infrastructure.Configurations;

public sealed class ProjectStrategyAlignmentConfiguration : IEntityTypeConfiguration<ProjectStrategyAlignment>
{
    public void Configure(EntityTypeBuilder<ProjectStrategyAlignment> builder)
    {
        builder.ToTable("ProjectStrategyAlignments", "integrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AlignmentPercentage).HasColumnType("decimal(5,2)");

        builder.HasIndex(x => new { x.ProjectId, x.StrategyId }).IsUnique();
    }
}
