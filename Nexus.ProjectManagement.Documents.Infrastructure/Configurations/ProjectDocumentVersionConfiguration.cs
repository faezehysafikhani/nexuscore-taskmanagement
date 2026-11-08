using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Infrastructure.Configurations;

public sealed class ProjectDocumentVersionConfiguration : IEntityTypeConfiguration<ProjectDocumentVersion>
{
    public void Configure(EntityTypeBuilder<ProjectDocumentVersion> builder)
    {
        builder.ToTable("ProjectDocumentVersions", "project_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(1000);

        // A document never has two versions with the same number.
        builder.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
    }
}
