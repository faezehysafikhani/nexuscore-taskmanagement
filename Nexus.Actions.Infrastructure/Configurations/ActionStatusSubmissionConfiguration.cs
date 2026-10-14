using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Actions.Domain;

namespace Nexus.Actions.Infrastructure.Configurations;

public sealed class ActionStatusSubmissionConfiguration : IEntityTypeConfiguration<ActionStatusSubmission>
{
    public void Configure(EntityTypeBuilder<ActionStatusSubmission> builder)
    {
        builder.ToTable("ActionStatusSubmissions", "actions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.HasIndex(x => x.ActionId);
        builder.HasIndex(x => x.ProjectId);
    }
}
