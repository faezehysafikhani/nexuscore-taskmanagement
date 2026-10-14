using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure.Configurations;

public sealed class RiskResponseSubmissionConfiguration : IEntityTypeConfiguration<RiskResponseSubmission>
{
    public void Configure(EntityTypeBuilder<RiskResponseSubmission> builder)
    {
        builder.ToTable("RiskResponseSubmissions", "risk_management");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.RiskId);
    }
}
