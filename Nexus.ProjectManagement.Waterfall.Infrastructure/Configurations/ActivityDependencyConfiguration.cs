using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class ActivityDependencyConfiguration : IEntityTypeConfiguration<ActivityDependency>
{
    public void Configure(EntityTypeBuilder<ActivityDependency> builder)
    {
        builder.ToTable("ActivityDependencies", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.SuccessorActivityId);

        // One link per ordered pair of activities; the service reports a clean Conflict before
        // this is ever hit, the index is the backstop against two concurrent requests.
        builder.HasIndex(x => new { x.PredecessorActivityId, x.SuccessorActivityId }).IsUnique();
    }
}
