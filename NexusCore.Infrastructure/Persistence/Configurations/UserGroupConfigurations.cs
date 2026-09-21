using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Persistence.Configurations;

public sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        // Organisational groups (OwnerUserId NULL) keep unique names per tenant - SQL Server treats
        // NULLs as equal in a unique index. Personal work teams are unique per owner, so two
        // people can each have a team with the same name.
        // HasFilter(null): EF would otherwise add "[OwnerUserId] IS NOT NULL" and drop the
        // uniqueness of organisational group names.
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.NormalizedName }).IsUnique().HasFilter(null);
        // NoAction: deleting a user removes their teams explicitly (IdentityRepository.RemoveUserAsync);
        // a cascade here would give SQL Server two cascade paths into UserGroupMembers.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class UserGroupPermissionConfiguration : IEntityTypeConfiguration<UserGroupPermission>
{
    public void Configure(EntityTypeBuilder<UserGroupPermission> builder)
    {
        builder.ToTable("UserGroupPermissions", "identity");
        builder.HasKey(x => new { x.UserGroupId, x.PermissionId });
        builder.HasOne(x => x.UserGroup).WithMany(x => x.Permissions).HasForeignKey(x => x.UserGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserGroupMemberConfiguration : IEntityTypeConfiguration<UserGroupMember>
{
    public void Configure(EntityTypeBuilder<UserGroupMember> builder)
    {
        builder.ToTable("UserGroupMembers", "identity");
        builder.HasKey(x => new { x.UserGroupId, x.UserId });
        builder.HasOne(x => x.UserGroup).WithMany(x => x.Members).HasForeignKey(x => x.UserGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.UserId);
    }
}
