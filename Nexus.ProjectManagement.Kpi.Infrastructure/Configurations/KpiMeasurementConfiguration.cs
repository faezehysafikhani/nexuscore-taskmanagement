using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Infrastructure.Configurations;

public sealed class KpiMeasurementConfiguration : IEntityTypeConfiguration<KpiMeasurement>
{
    public void Configure(EntityTypeBuilder<KpiMeasurement> builder)
    {
        builder.ToTable("KpiMeasurements", "kpi");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Value).HasColumnType("decimal(18,4)");
        builder.Property(x => x.Note).HasMaxLength(1000);

        // No foreign key, like the module's other child tables: the KPI service removes a KPI's values with it.
        builder.HasIndex(x => new { x.KpiDefinitionId, x.MeasuredOn });
        builder.HasIndex(x => x.ProjectId);
    }
}
