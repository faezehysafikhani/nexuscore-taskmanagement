using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Domain;
using NexusCore.Domain.Identity;
using Nexus.TaskManagement.Application;
using NexusCore.Application.Security;
using NexusCore.SharedKernel.Interfaces;

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
    ITaskAccessScope? access = null,
    ITenantScope? tenantScope = null)
    : DbContext(options), ITaskManagementUnitOfWork
{
    // The organization of the signed-in user, or null when nothing confines the work (ITenantScope).
    private Guid? TenantFilterId => tenantScope?.FilterTenantIdFor(nameof(TaskManagementDbContext));

    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SubTask> SubTasks => Set<SubTask>();
    public DbSet<RepetitiveTask> RepetitiveTasks => Set<RepetitiveTask>();
    public DbSet<TaskFileAsset> Files => Set<TaskFileAsset>();
    public DbSet<TaskFile> TaskFiles => Set<TaskFile>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<SubTaskAssignee> SubTaskAssignees => Set<SubTaskAssignee>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<PendingTaskSms> PendingSms => Set<PendingTaskSms>();

    // Read-only views onto the shared identity tables, for joins and FK targets only.
    public DbSet<User> Users => Set<User>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    // Read per query from this context instance (EF parameterises them), so one cached model
    // serves every user. See ITaskAccessScope for the rule.
    private bool AccessRestricted => access?.IsRestricted ?? false;
    private Guid? AccessUserId => access?.UserId;
    private bool AccessReachLimited => access?.ReachUserIds is not null;
    private List<Guid> AccessReach => access?.ReachUserIds?.ToList() ?? [];

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
        //
        // A Tasks.ManageAll holder kept to their own company sees the tasks of its people.
        modelBuilder.Entity<TaskItem>().HasQueryFilter(task =>
            (TenantFilterId == null || task.TenantId == TenantFilterId)
            && ((!AccessRestricted
                && (!AccessReachLimited
                    || (task.OwnerUserId != null && AccessReach.Contains(task.OwnerUserId.Value))
                    || (task.AssignedUserId != null && AccessReach.Contains(task.AssignedUserId.Value))
                    || task.Assignees.Any(assignee => AccessReach.Contains(assignee.UserId))))
            || (task.OwnerUserId == null && !task.OwnerHeldByPost)
            || task.OwnerUserId == AccessUserId
            || task.AssignedUserId == AccessUserId
            || task.Assignees.Any(assignee => assignee.UserId == AccessUserId)));

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

        // The organization condition on everything else that belongs to one (tags, notes, files,
        // pending SMS...). What hangs off a task follows the task's own filter above.
        modelBuilder.ApplyTenantFilters(() => TenantFilterId);
    }
}
