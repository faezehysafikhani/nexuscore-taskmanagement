using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure.Configurations;

public sealed class AgileTaskConfiguration : IEntityTypeConfiguration<AgileTask>
{
    public void Configure(EntityTypeBuilder<AgileTask> builder)
    {
        builder.ToTable("AgileTasks", "agile_planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => new { x.ProjectId, x.SprintNumber });
    }
}
