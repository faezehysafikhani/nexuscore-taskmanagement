using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Infrastructure.Configurations;

public sealed class KpiDefinitionConfiguration : IEntityTypeConfiguration<KpiDefinition>
{
    public void Configure(EntityTypeBuilder<KpiDefinition> builder)
    {
        builder.ToTable("KpiDefinitions", "kpi");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Formula).HasMaxLength(500);
        builder.Property(x => x.TargetValue).HasColumnType("decimal(18,4)");

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.DeliverableId);
    }
}
