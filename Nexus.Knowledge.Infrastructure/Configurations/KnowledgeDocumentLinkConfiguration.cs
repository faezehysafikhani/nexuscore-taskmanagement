using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Infrastructure.Configurations;

public sealed class KnowledgeDocumentLinkConfiguration
    : IEntityTypeConfiguration<KnowledgeDocumentLink>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocumentLink> builder)
    {
        builder.ToTable("KnowledgeDocumentLinks", "knowledge");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.DocumentId)
            .IsRequired();

        builder.Property(x => x.SubjectType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.SubjectId)
            .IsRequired();

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // A document cannot link to the same subject twice.
        builder.HasIndex(x => new
        {
            x.DocumentId,
            x.SubjectType,
            x.SubjectId
        })
            .IsUnique();

        // Supports finding documents by their linked subject.
        builder.HasIndex(x => new
        {
            x.SubjectType,
            x.SubjectId
        });
    }
}