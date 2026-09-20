using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Domain;
using NexusCore.Domain.Identity;
using Nexus.TaskManagement.Application;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// This module's own DbContext, owning the task_management schema on the shared
/// DefaultConnection database.
///
/// It also *maps* - but does not own - User, UserGroup and Tenant from the identity schema.
/// They are NexusCore shared infrastructure rather than another business module, so real
/// foreign keys into them are wanted, and EF needs the principal entity in this model to emit
/// those constraints. Every one of them is marked ExcludeFromMigrations in
/// SharedIdentityConfigurations, so this context references those tables and never creates,
/// alters or drops them - NexusCoreDbContext remains their single owner.
/// </summary>
public sealed class TaskManagementDbContext(DbContextOptions<TaskManagementDbContext> options)
    : DbContext(options), ITaskManagementUnitOfWork
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SubTask> SubTasks => Set<SubTask>();
    public DbSet<RepetitiveTask> RepetitiveTasks => Set<RepetitiveTask>();
    public DbSet<TaskFileAsset> Files => Set<TaskFileAsset>();
    public DbSet<TaskFile> TaskFiles => Set<TaskFile>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    // Read-only views onto the shared identity tables, for joins and FK targets only.
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskManagementDbContext).Assembly);
    }
}
