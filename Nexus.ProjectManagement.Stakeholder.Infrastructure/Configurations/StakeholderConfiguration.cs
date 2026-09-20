using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.StakeholderManagement.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure.Configurations;

public sealed class StakeholderConfiguration : IEntityTypeConfiguration<Stakeholder>
{
    public void Configure(EntityTypeBuilder<Stakeholder> builder)
    {
        builder.ToTable("Stakeholders", "stakeholder_management");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Expectations).HasMaxLength(2000);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.EngagementStrategy).HasMaxLength(2000);
        builder.Property(x => x.Requirements).HasMaxLength(2000);

        builder.HasIndex(x => x.ProjectId);
    }
}
