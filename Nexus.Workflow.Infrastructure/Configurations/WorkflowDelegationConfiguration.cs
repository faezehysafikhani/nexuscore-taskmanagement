using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure.Configurations;

public sealed class WorkflowDelegationConfiguration : IEntityTypeConfiguration<WorkflowDelegation>
{
    public void Configure(EntityTypeBuilder<WorkflowDelegation> builder)
    {
        builder.ToTable("WorkflowDelegations", "workflow");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubjectType).HasMaxLength(80);
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.DelegateUserId });
        builder.HasIndex(x => new { x.TenantId, x.DelegatorUserId });
    }
}
