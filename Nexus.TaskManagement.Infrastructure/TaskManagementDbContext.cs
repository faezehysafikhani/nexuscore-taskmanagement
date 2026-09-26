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
public sealed class TaskManagementDbContext(
    DbContextOptions<TaskManagementDbContext> options,
    ITaskAccessScope? access = null)
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

    // Read per query from this context instance (EF parameterises them), so one cached model
    // serves every user. See ITaskAccessScope for the rule.
    private bool AccessRestricted => access?.IsRestricted ?? false;
    private Guid? AccessUserId => access?.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskManagementDbContext).Assembly);

        // Resource-level access, inside every query. A task the user may not reach is filtered
        // out, and everything hanging off it follows through its navigation: each of those
        // filters reads a non-key column of the task, so EF joins the (filtered) task instead of
        // short-circuiting to the foreign key.
        //
        // A task's team is its context, not a grant: the members who may see it are on its
        // access list (Assignees), so taking one off the list really takes the task away.
        modelBuilder.Entity<TaskItem>().HasQueryFilter(task =>
            !AccessRestricted
            || task.OwnerUserId == null
            || task.OwnerUserId == AccessUserId
            || task.AssignedUserId == AccessUserId
            || task.Assignees.Any(assignee => assignee.UserId == AccessUserId));

        modelBuilder.Entity<SubTask>().HasQueryFilter(subTask => subTask.Task!.Title != null);
        modelBuilder.Entity<RepetitiveTask>().HasQueryFilter(schedule => schedule.Task!.Title != null);
        modelBuilder.Entity<TaskComment>().HasQueryFilter(comment => comment.Task!.Title != null);
        modelBuilder.Entity<TaskFile>().HasQueryFilter(link =>
            (link.TaskId == null || link.Task!.Title != null)
            && (link.SubTaskId == null || link.SubTask!.Title != null)
            && (link.CommentId == null || link.Comment!.Text != null));
        modelBuilder.Entity<TaskTag>().HasQueryFilter(link =>
            (link.TaskId == null || link.Task!.Title != null)
            && (link.SubTaskId == null || link.SubTask!.Title != null));
    }
}
