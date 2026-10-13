using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Infrastructure.Configurations;

internal static class OrgStructureSchema
{
    public const string Name = "org_structure";
}

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class CompanyUnitConfiguration : IEntityTypeConfiguration<CompanyUnit>
{
    public void Configure(EntityTypeBuilder<CompanyUnit> builder)
    {
        builder.ToTable("CompanyUnits", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        // A unit (and its branch) belongs to one company at most.
        builder.HasIndex(x => new { x.TenantId, x.UnitId }).IsUnique();
        builder.HasIndex(x => x.CompanyId);
    }
}

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ParentPositionId);
        builder.HasIndex(x => x.ParentPositionId);
        builder.HasIndex(x => new { x.TenantId, x.Title }).IsUnique();
    }
}

public sealed class UserAssignmentConfiguration : IEntityTypeConfiguration<UserAssignment>
{
    public void Configure(EntityTypeBuilder<UserAssignment> builder)
    {
        builder.ToTable("UserAssignments", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        // One place per user per tenant.
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.ManagerUserId);
    }
}
