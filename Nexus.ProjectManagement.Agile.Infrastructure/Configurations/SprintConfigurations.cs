using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Infrastructure.Configurations;

public sealed class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprints", "agile_planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Goal).HasMaxLength(1000);

        // A project never has two sprints with the same number.
        builder.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
    }
}

public sealed class SprintEventConfiguration : IEntityTypeConfiguration<SprintEvent>
{
    public void Configure(EntityTypeBuilder<SprintEvent> builder)
    {
        builder.ToTable("SprintEvents", "agile_planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => new { x.ProjectId, x.SprintNumber });
    }
}

public sealed class AgileChecklistItemConfiguration : IEntityTypeConfiguration<AgileChecklistItem>
{
    public void Configure(EntityTypeBuilder<AgileChecklistItem> builder)
    {
        builder.ToTable("ChecklistItems", "agile_planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Text).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => x.TaskId);
    }
}
