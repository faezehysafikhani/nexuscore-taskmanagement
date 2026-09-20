using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure.Configurations;

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances", "workflow");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubjectType).HasMaxLength(80).IsRequired();
        builder.Navigation(x => x.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Decisions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SubjectType, x.SubjectId });
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

public sealed class WorkflowDecisionConfiguration : IEntityTypeConfiguration<WorkflowDecision>
{
    public void Configure(EntityTypeBuilder<WorkflowDecision> builder)
    {
        builder.ToTable("WorkflowDecisions", "workflow");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(2000);
    }
}
