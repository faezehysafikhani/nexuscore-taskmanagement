using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure.Configurations;

public sealed class AgileTaskStatusSubmissionConfiguration : IEntityTypeConfiguration<AgileTaskStatusSubmission>
{
    public void Configure(EntityTypeBuilder<AgileTaskStatusSubmission> builder)
    {
        builder.ToTable("AgileTaskStatusSubmissions", "agile_planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.TaskId);
    }
}
