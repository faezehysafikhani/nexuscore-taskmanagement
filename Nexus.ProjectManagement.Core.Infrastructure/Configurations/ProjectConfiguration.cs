using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Core.Domain;

namespace Nexus.ProjectManagement.Core.Infrastructure.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", "project_management");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Cost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Goal).HasMaxLength(2000);
        builder.Property(x => x.Requirements).HasMaxLength(4000);
        builder.Property(x => x.Constraints).HasMaxLength(4000);
        builder.Property(x => x.Assumptions).HasMaxLength(4000);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Charter).HasMaxLength(4000);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.OrganizationUnitId });
        builder.HasIndex(x => new { x.TenantId, x.ManagerUserId });
    }
}
