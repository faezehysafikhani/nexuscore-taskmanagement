using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure.Configurations;

public sealed class WaterfallProjectSettingsConfiguration : IEntityTypeConfiguration<WaterfallProjectSettings>
{
    public void Configure(EntityTypeBuilder<WaterfallProjectSettings> builder)
    {
        builder.ToTable("WaterfallProjectSettings", "waterfall");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.ProjectId).IsUnique();
    }
}
