using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class ActivityProgressSubmissionConfiguration : IEntityTypeConfiguration<ActivityProgressSubmission>
{
    public void Configure(EntityTypeBuilder<ActivityProgressSubmission> builder)
    {
        builder.ToTable("ActivityProgressSubmissions", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubmittedProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ApprovedProgress).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Comment).HasMaxLength(2000);
        builder.Property(x => x.DocumentName).HasMaxLength(260);

        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.ActivityId);
    }
}
