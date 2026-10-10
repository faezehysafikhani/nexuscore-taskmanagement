using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Organization.Domain;

namespace Nexus.Organization.Infrastructure.Configurations;

public sealed class OrganizationUnitConfiguration : IEntityTypeConfiguration<OrganizationUnit>
{
    public void Configure(EntityTypeBuilder<OrganizationUnit> builder)
    {
        builder.ToTable("OrganizationUnits", "organization");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.ParentId);
    }
}

public sealed class OrganizationUnitMemberConfiguration : IEntityTypeConfiguration<OrganizationUnitMember>
{
    public void Configure(EntityTypeBuilder<OrganizationUnitMember> builder)
    {
        builder.ToTable("OrganizationUnitMembers", "organization");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // A person is in at most one unit of their tenant.
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UnitId);
    }
}
