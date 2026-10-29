using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Infrastructure.Configurations;

public sealed class NodeTypeConfiguration : IEntityTypeConfiguration<NodeType>
{
    public void Configure(EntityTypeBuilder<NodeType> builder)
    {
        builder.ToTable("NodeTypes", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class NodeTypeRuleConfiguration : IEntityTypeConfiguration<NodeTypeRule>
{
    public void Configure(EntityTypeBuilder<NodeTypeRule> builder)
    {
        builder.ToTable("NodeTypeRules", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TenantId, x.ParentNodeTypeId, x.ChildNodeTypeId }).IsUnique();
        builder.HasIndex(x => x.ChildNodeTypeId);
    }
}

public sealed class NodeTypePostTemplateConfiguration : IEntityTypeConfiguration<NodeTypePostTemplate>
{
    public void Configure(EntityTypeBuilder<NodeTypePostTemplate> builder)
    {
        builder.ToTable("NodeTypePostTemplates", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.NodeTypeId);
    }
}

public sealed class LinkTypeConfiguration : IEntityTypeConfiguration<LinkType>
{
    public void Configure(EntityTypeBuilder<LinkType> builder)
    {
        builder.ToTable("LinkTypes", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.LineStyle).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class AutoLinkRuleConfiguration : IEntityTypeConfiguration<AutoLinkRule>
{
    public void Configure(EntityTypeBuilder<AutoLinkRule> builder)
    {
        builder.ToTable("AutoLinkRules", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TenantId, x.SourceNodeTypeId, x.TargetNodeTypeId, x.LinkTypeId }).IsUnique();
    }
}

public sealed class OrgLinkConfiguration : IEntityTypeConfiguration<OrgLink>
{
    public void Configure(EntityTypeBuilder<OrgLink> builder)
    {
        builder.ToTable("OrgLinks", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TenantId, x.FromUnitId, x.ToUnitId, x.LinkTypeId }).IsUnique();
        builder.HasIndex(x => x.ToUnitId);
    }
}

public sealed class StructureTemplateConfiguration : IEntityTypeConfiguration<StructureTemplate>
{
    public void Configure(EntityTypeBuilder<StructureTemplate> builder)
    {
        builder.ToTable("StructureTemplates", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class StructureTemplateItemConfiguration : IEntityTypeConfiguration<StructureTemplateItem>
{
    public void Configure(EntityTypeBuilder<StructureTemplateItem> builder)
    {
        builder.ToTable("StructureTemplateItems", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ParentKey).HasMaxLength(60);
        builder.Property(x => x.NodeTypeCode).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TemplateId, x.Key }).IsUnique();
    }
}

public sealed class OrgChangeLogConfiguration : IEntityTypeConfiguration<OrgChangeLog>
{
    public void Configure(EntityTypeBuilder<OrgChangeLog> builder)
    {
        builder.ToTable("OrgChangeLogs", OrgStructureSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EntityId, x.AtUtc });
    }
}
