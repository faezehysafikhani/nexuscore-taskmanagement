using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Delegation.Domain;

namespace Nexus.ProjectManagement.Delegation.Infrastructure.Configurations;

public sealed class ItemDelegationConfiguration : IEntityTypeConfiguration<ItemDelegation>
{
    public void Configure(EntityTypeBuilder<ItemDelegation> builder)
    {
        builder.ToTable("ItemDelegations", "item_delegation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => new { x.TenantId, x.ItemType, x.ItemId, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.DelegatorUserId });
        builder.HasIndex(x => new { x.TenantId, x.DelegateUserId });
    }
}
