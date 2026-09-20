using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Team.Domain;

namespace Nexus.ProjectManagement.Team.Infrastructure.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers", "project_team");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RoleTitle).HasMaxLength(120);
        builder.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
    }
}

public sealed class GovernanceRoleConfiguration : IEntityTypeConfiguration<GovernanceRole>
{
    public void Configure(EntityTypeBuilder<GovernanceRole> builder)
    {
        builder.ToTable("GovernanceRoles", "project_team");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PersonnelNumber).HasMaxLength(60);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.ServiceLocation).HasMaxLength(200);
        builder.HasIndex(x => x.ProjectId);
    }
}
