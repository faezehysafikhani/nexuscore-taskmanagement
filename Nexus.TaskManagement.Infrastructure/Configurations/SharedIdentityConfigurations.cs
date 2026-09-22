using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusCore.Domain.Identity;

namespace Nexus.TaskManagement.Infrastructure.Configurations;

/// <summary>
/// Borrowed mappings for the shared identity tables.
///
/// TaskManagement needs real foreign keys into identity.Users and identity.UserGroups, and EF
/// Core will only emit a foreign key when the principal entity is part of the same model. So
/// these types are mapped here - but every one of them is ExcludeFromMigrations, which keeps
/// NexusCoreDbContext the single owner of those tables: this module's migrations reference
/// them and never emit CREATE, ALTER or DROP for them.
///
/// Only the columns needed to satisfy the key and to project a display name are mapped. The
/// rest of the identity model (roles, tokens, permissions, group membership) is ignored, so
/// nothing here can drift against the owner's schema.
///
/// This applies to shared infrastructure only. A business module's tables are never mapped
/// this way - see TaskManagementIsolationTests.
/// </summary>
internal sealed class SharedUserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "identity", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.DisplayName).HasMaxLength(200);

        builder.Ignore(x => x.Roles);
        builder.Ignore(x => x.RefreshTokens);
        builder.Ignore(x => x.Permissions);
        builder.Ignore(x => x.Tenant);
        builder.Ignore(x => x.PasswordHash);
        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class SharedUserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups", "identity", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.NormalizedName).HasMaxLength(200);

        builder.Ignore(x => x.Permissions);
        builder.Ignore(x => x.Members);
        builder.Ignore(x => x.Tenant);
        builder.Ignore(x => x.DomainEvents);
    }
}
