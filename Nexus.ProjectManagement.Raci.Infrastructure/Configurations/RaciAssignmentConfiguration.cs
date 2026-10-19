using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Raci.Domain;

namespace Nexus.ProjectManagement.Raci.Infrastructure.Configurations;

public sealed class RaciAssignmentConfiguration : IEntityTypeConfiguration<RaciAssignment>
{
    public void Configure(EntityTypeBuilder<RaciAssignment> builder)
    {
        builder.ToTable("RaciAssignments", "raci");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => new { x.TenantId, x.ProjectId });
        builder.HasIndex(x => new { x.ItemType, x.ItemId, x.UserId, x.Role }).IsUnique();
    }
}
