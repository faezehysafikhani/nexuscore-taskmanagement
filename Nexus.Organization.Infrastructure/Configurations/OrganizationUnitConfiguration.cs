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
        // Parent, node type and validity are optional: a unit with none of them is a flat unit, as before.
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ParentUnitId);
        builder.HasIndex(x => x.NodeTypeId);
        // Optional node code and details (phase 6): all nullable, empty for every existing unit.
        builder.Property(x => x.NodeCode).HasMaxLength(40);
        builder.Property(x => x.LatinName).HasMaxLength(200);
        builder.Property(x => x.CorrespondenceTitle).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Address).HasMaxLength(500);
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
