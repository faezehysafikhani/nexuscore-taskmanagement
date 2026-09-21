using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure.Configurations;

internal static class Schema
{
    public const string Name = "task_management";
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.IsProject).IsRequired();
        builder.Property(x => x.AllowAssigneeStatusUpdate).IsRequired();
        builder.Property(x => x.CharterDescription).HasMaxLength(4000);
        builder.Property(x => x.CharterProjectManager).HasMaxLength(200);

        builder.Ignore(x => x.IsRecurring);

        // Real foreign keys into the shared identity schema. Restrict, never cascade:
        // removing a person must not silently delete the work assigned to them.
        builder.HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedUserGroup)
            .WithMany()
            .HasForeignKey(x => x.AssignedUserGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.SubTasks)
            .WithOne(x => x.Task!)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Assignees)
            .WithOne(x => x.Task!)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // One task, at most one schedule.
        builder.HasOne(x => x.Recurrence)
            .WithOne(x => x.Task!)
            .HasForeignKey<RepetitiveTask>(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.DueDate });
        builder.HasIndex(x => new { x.TenantId, x.IsProject });
        builder.HasIndex(x => new { x.TenantId, x.AssignedUserId });
        builder.HasIndex(x => new { x.TenantId, x.AssignedUserGroupId });
    }
}

internal sealed class SubTaskConfiguration : IEntityTypeConfiguration<SubTask>
{
    public void Configure(EntityTypeBuilder<SubTask> builder)
    {
        builder.ToTable("SubTasks", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TaskId).IsRequired();

        builder.HasIndex(x => new { x.TaskId, x.SortOrder });
        builder.HasIndex(x => new { x.TenantId, x.IsCompleted });
        builder.Property(x => x.IsGeneratedOccurrence).HasDefaultValue(false);
    }
}

internal sealed class RepetitiveTaskConfiguration : IEntityTypeConfiguration<RepetitiveTask>
{
    public void Configure(EntityTypeBuilder<RepetitiveTask> builder)
    {
        builder.ToTable("RepetitiveTasks", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TaskId).IsRequired();

        // EF 8 primitive collections: stored as JSON in a single column. The day lists are
        // only ever read together with the row that owns them, so they need no table of
        // their own and no index.
        builder.PrimitiveCollection(x => x.WeeklyDays);
        builder.PrimitiveCollection(x => x.MonthlyDays);

        // One schedule per task.
        builder.HasIndex(x => x.TaskId).IsUnique();

        // Drives the background job's "what is due now" scan.
        builder.HasIndex(x => new { x.TenantId, x.IsActive, x.NextExecutionAtUtc });
    }
}

internal sealed class TaskFileAssetConfiguration : IEntityTypeConfiguration<TaskFileAsset>
{
    public void Configure(EntityTypeBuilder<TaskFileAsset> builder)
    {
        builder.ToTable("Files", Schema.Name, table =>
            table.HasCheckConstraint(
                "CK_Files_MaxSize",
                $"[FileSizeBytes] > 0 AND [FileSizeBytes] <= {TaskFileAsset.MaxFileSizeBytes}"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();

        builder.HasOne(x => x.UploadedByUser)
            .WithMany()
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.StoredFileName }).IsUnique();
    }
}

internal sealed class TaskFileConfiguration : IEntityTypeConfiguration<TaskFile>
{
    public void Configure(EntityTypeBuilder<TaskFile> builder)
    {
        // Exactly one owner: a file belongs to a task, a subtask or a comment - never to more
        // than one and never to none.
        builder.ToTable("TaskFiles", Schema.Name, table =>
            table.HasCheckConstraint(
                "CK_TaskFiles_ExactlyOneOwner",
                "(CASE WHEN [TaskId] IS NOT NULL THEN 1 ELSE 0 END + " +
                "CASE WHEN [SubTaskId] IS NOT NULL THEN 1 ELSE 0 END + " +
                "CASE WHEN [CommentId] IS NOT NULL THEN 1 ELSE 0 END) = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileId).IsRequired();

        builder.HasOne(x => x.File)
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tasks already cascade into SubTasks, so cascading from here as well would give
        // SQL Server two delete paths to the same row and it refuses the constraint. The
        // service clears these rows inside the same transaction instead.
        builder.HasOne(x => x.Task)
            .WithMany(x => x.Files)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.SubTask)
            .WithMany(x => x.Files)
            .HasForeignKey(x => x.SubTaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.FileId, x.TaskId })
            .IsUnique()
            .HasFilter("[TaskId] IS NOT NULL");

        builder.HasIndex(x => new { x.FileId, x.SubTaskId })
            .IsUnique()
            .HasFilter("[SubTaskId] IS NOT NULL");

        // Comments cascade from tasks, so this path is NoAction too; the services clear it.
        builder.HasOne(x => x.Comment)
            .WithMany()
            .HasForeignKey(x => x.CommentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.FileId, x.CommentId })
            .IsUnique()
            .HasFilter("[CommentId] IS NOT NULL");
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(30);

        builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
    }
}

internal sealed class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> builder)
    {
        // At least one owner - and unlike TaskFiles, both columns together are legal: that
        // row means the tag applies to the task and to the subtask.
        builder.ToTable("TaskTags", Schema.Name, table =>
            table.HasCheckConstraint(
                "CK_TaskTags_AtLeastOneOwner",
                "[TaskId] IS NOT NULL OR [SubTaskId] IS NOT NULL"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TagId).IsRequired();

        builder.HasOne(x => x.Tag)
            .WithMany()
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Task)
            .WithMany(x => x.Tags)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.SubTask)
            .WithMany(x => x.Tags)
            .HasForeignKey(x => x.SubTaskId)
            .OnDelete(DeleteBehavior.NoAction);

        // Filtered so the nullable columns do not collapse every unset row into one another.
        // Deliberately not a single unique index over both columns: that would forbid the
        // legal "tagged on the task, and separately on one of its subtasks" pair.
        builder.HasIndex(x => new { x.TagId, x.TaskId })
            .IsUnique()
            .HasFilter("[TaskId] IS NOT NULL AND [SubTaskId] IS NULL");

        builder.HasIndex(x => new { x.TagId, x.SubTaskId })
            .IsUnique()
            .HasFilter("[SubTaskId] IS NOT NULL AND [TaskId] IS NULL");
    }
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(30);
        builder.Property(x => x.UserId).IsRequired();

        // Real foreign key into the shared identity schema: every note has an owner.
        // Restrict rather than cascade - removing a user is an explicit operation that has
        // to deal with their notes deliberately.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.UserId, x.IsPinned });
    }
}

internal sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> builder)
    {
        builder.ToTable("TaskAssignees", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TaskId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId);
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("TaskComments", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Text).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.TaskId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();

        // Comments belong to their task and go with it.
        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // Real foreign key into the shared identity schema. Restrict, so removing a user is
        // a deliberate act rather than something that quietly erases a discussion.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.TaskId, x.CreatedAtUtc });
    }
}
