using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure.Configurations;

public sealed class RiskProjectSettingsConfiguration : IEntityTypeConfiguration<RiskProjectSettings>
{
    public void Configure(EntityTypeBuilder<RiskProjectSettings> builder)
    {
        builder.ToTable("RiskProjectSettings", "risk_management");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.ProjectId).IsUnique();
    }
}
