using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.StrategyManagement.Domain;

namespace Nexus.StrategyManagement.Infrastructure.Configurations;

public sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.ToTable("Strategies", "strategy");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Weight).HasColumnType("decimal(5,2)");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.ParentStrategyId);
    }
}
