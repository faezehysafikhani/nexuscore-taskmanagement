using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.History.Domain;

namespace Nexus.ProjectManagement.History.Infrastructure.Configurations;

public sealed class ProjectChangeConfiguration : IEntityTypeConfiguration<ProjectChange>
{
    public void Configure(EntityTypeBuilder<ProjectChange> builder)
    {
        builder.ToTable("ProjectChanges", "project_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ChangesJson); // nvarchar(max)

        // The one query: a project's changes, newest first.
        builder.HasIndex(x => new { x.ProjectId, x.ChangedAtUtc });
    }
}
