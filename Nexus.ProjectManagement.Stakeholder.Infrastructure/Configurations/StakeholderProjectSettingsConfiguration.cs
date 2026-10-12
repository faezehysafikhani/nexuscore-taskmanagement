using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure.Configurations;

public sealed class StakeholderProjectSettingsConfiguration : IEntityTypeConfiguration<StakeholderProjectSettings>
{
    public void Configure(EntityTypeBuilder<StakeholderProjectSettings> builder)
    {
        builder.ToTable("StakeholderProjectSettings", "stakeholder_management");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ManageClosely).HasMaxLength(500);
        builder.Property(x => x.KeepSatisfied).HasMaxLength(500);
        builder.Property(x => x.KeepInformed).HasMaxLength(500);
        builder.Property(x => x.Monitor).HasMaxLength(500);
        builder.HasIndex(x => x.ProjectId).IsUnique();
    }
}
