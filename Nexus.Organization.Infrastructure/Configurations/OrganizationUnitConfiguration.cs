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
    }
}
