using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Progress.Domain;

namespace Nexus.ProjectManagement.Progress.Infrastructure.Configurations;

public sealed class DelayReasonConfiguration : IEntityTypeConfiguration<DelayReason>
{
    public void Configure(EntityTypeBuilder<DelayReason> builder)
    {
        builder.ToTable("DelayReasons", "progress");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CorrectiveAction).HasMaxLength(2000);
        builder.Property(x => x.CostImpact).HasColumnType("decimal(18,0)");

        builder.HasIndex(x => x.ProjectId);
    }
}
