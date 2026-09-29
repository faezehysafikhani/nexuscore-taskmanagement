using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Files;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// The business rules, run against a real SQL Server database through the real services.
///
/// Skips cleanly when no SQL Server is reachable - a skipped test is honest, a green
/// in-memory one would not be.
/// </summary>
[Collection("sqlserver")]
public sealed class TaskManagementIntegrationTests(SqlServerFixture fixture)
{
    private bool Skip => !fixture.Available;

    private static CreateTaskRequest PlainTask(string title = "Write the report") =>
        new(title, new DateOnly(2026, 6, 1));

    private static CreateTaskRequest Project(string title, params string[] subTaskTitles) =>
        new(title, new DateOnly(2026, 6, 1), IsProject: true,
            SubTasks: subTaskTitles.Select(t => new SubTaskInput(t)).ToList());

    // -----------------------------------------------------------------------
    // Task and project rules
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlainTaskIsCreatedWithIsProjectFalse()
    {
        if (Skip) return;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask(), default));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsProject);
        Assert.False(result.Value.IsRecurring);
        Assert.Empty(result.Value.SubTasks);
    }

    [Fact]
    public async Task ProjectIsCreatedWithItsSubTasks()
    {
        if (Skip) return;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>()
                .CreateAsync(Project("Launch", "Design", "Build", "Ship"), default));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsProject);
        Assert.Equal(3, result.Value.SubTasks.Count);
    }

    [Fact]
    public async Task ProjectWithoutSubTasksIsRejected_AndNothingIsPersisted()
    {
        if (Skip) return;

        var title = $"Empty project {Guid.NewGuid():N}";
        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project(title), default));

        Assert.True(result.IsFailure);

        // The rollback that matters: no orphaned project row left behind.
        var persisted = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>()
                .Tasks.AnyAsync(t => t.Title == title));

        Assert.False(persisted);
    }

    [Fact]
    public async Task DeletingAProjectsLastSubTaskIsRefused()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Solo", "Only step"), default));

        var subTaskId = project.Value.SubTasks.Single().Id;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().DeleteSubTaskAsync(subTaskId, default));

        Assert.True(result.IsFailure);

        var stillThere = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>().SubTasks.AnyAsync(s => s.Id == subTaskId));
        Assert.True(stillThere);
    }

    [Fact]
    public async Task DeletingASubTaskIsAllowedWhenOthersRemain()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Pair", "One", "Two"), default));

        var subTaskId = project.Value.SubTasks.First().Id;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().DeleteSubTaskAsync(subTaskId, default));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeletingASubTaskOfAPlainTaskIsAlwaysAllowed()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Has one step"), default));

        var subTask = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>()
                .CreateSubTaskAsync(task.Value.Id, new CreateSubTaskRequest("Step"), default));

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().DeleteSubTaskAsync(subTask.Value.Id, default));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PromotingAPlainTaskToAProjectRequiresASubTask()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Not yet a project"), default));

        var refused = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().UpdateAsync(
                task.Value.Id,
                new UpdateTaskRequest("Not yet a project", new DateOnly(2026, 6, 1), TaskPriority.Medium, IsProject: true),
                default));

        Assert.True(refused.IsFailure);

        await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>()
                .CreateSubTaskAsync(task.Value.Id, new CreateSubTaskRequest("Now it has one"), default));

        var accepted = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().UpdateAsync(
                task.Value.Id,
                new UpdateTaskRequest("Now a project", new DateOnly(2026, 6, 1), TaskPriority.Medium, IsProject: true),
                default));

        Assert.True(accepted.IsSuccess);
        Assert.True(accepted.Value.IsProject);
    }

    [Fact]
    public async Task DeletingATaskRemovesItsSubTasks()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Doomed", "A", "B"), default));

        var taskId = project.Value.Id;

        var deleted = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().DeleteAsync(taskId, default));
        Assert.True(deleted.IsSuccess);

        var remaining = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>().SubTasks.CountAsync(s => s.TaskId == taskId));
        Assert.Equal(0, remaining);
    }

    // -----------------------------------------------------------------------
    // Task + RepetitiveTask
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecurringTaskStoresGeneralDataOnTaskAndScheduleOnRepetitiveTask()
    {
        if (Skip) return;

        var created = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Weekly report", new DateOnly(2026, 6, 1), TaskPriority.High,
                    Description: "Every Sunday",
                    Recurrence: new RecurrenceInput(
                        RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), WeeklyDays: [1]), AssignedUserId: fixture.OwnerUserId),
                default));

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.IsRecurring);

        await fixture.ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<TaskManagementDbContext>();

            // General data lives on Tasks.
            var task = await db.Tasks.SingleAsync(t => t.Id == created.Value.Id);
            Assert.Equal("Weekly report", task.Title);
            Assert.Equal(TaskPriority.High, task.Priority);

            // Schedule lives on RepetitiveTasks, pointing back with a real TaskId.
            var schedule = await db.RepetitiveTasks.SingleAsync(r => r.TaskId == created.Value.Id);
            Assert.Equal(RecurrenceFrequency.Weekly, schedule.Frequency);
            Assert.Equal([1], schedule.WeeklyDays);
            Assert.Equal(task.Id, schedule.TaskId);
        });
    }

    [Fact]
    public async Task ARecurringTaskIsOneTaskRow_NotTwo()
    {
        if (Skip) return;

        var title = $"Single row {Guid.NewGuid():N}";
        await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    title, new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        var rows = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>().Tasks.CountAsync(t => t.Title == title));

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task ASecondScheduleForTheSameTaskIsRefused()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Already recurring", new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        var second = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<IRepetitiveTaskService>().CreateAsync(
                new CreateRepetitiveTaskRequest(
                    task.Value.Id,
                    new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1))),
                default));

        Assert.True(second.IsFailure);
    }

    [Fact]
    public async Task AScheduleForANonExistentTaskIsRefused()
    {
        if (Skip) return;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<IRepetitiveTaskService>().CreateAsync(
                new CreateRepetitiveTaskRequest(
                    Guid.NewGuid(),
                    new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1))),
                default));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeletingTheScheduleLeavesTheTaskAlone()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Stop repeating me", new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        var scheduleId = task.Value.Recurrence!.Id;

        var deleted = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<IRepetitiveTaskService>().DeleteAsync(scheduleId, default));
        Assert.True(deleted.IsSuccess);

        await fixture.ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<TaskManagementDbContext>();
            Assert.True(await db.Tasks.AnyAsync(t => t.Id == task.Value.Id));
            Assert.False(await db.RepetitiveTasks.AnyAsync(r => r.Id == scheduleId));
        });
    }

    [Fact]
    public async Task DisablingAScheduleKeepsItButStopsIt()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Pause me", new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        var disabled = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<IRepetitiveTaskService>()
                .SetActiveAsync(task.Value.Recurrence!.Id, false, default));

        Assert.True(disabled.IsSuccess);
        Assert.False(disabled.Value.IsActive);
    }

    [Fact]
    public async Task DeletingTheTaskRemovesItsSchedule()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Delete everything", new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().DeleteAsync(task.Value.Id, default));

        // No schedule may survive without its task.
        var orphans = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>()
                .RepetitiveTasks.CountAsync(r => r.TaskId == task.Value.Id));

        Assert.Equal(0, orphans);
    }

    [Fact]
    public async Task RecurringFilterUsesTheScheduleNotIsProject()
    {
        if (Skip) return;

        await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Recurring plain task", new DateOnly(2026, 6, 1),
                    Recurrence: new RecurrenceInput(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)), AssignedUserId: fixture.OwnerUserId),
                default));

        var recurring = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().ListAsync(
                new ListTasksRequest(fixture.TenantId, PageSize: 200, IsRecurring: true), default));

        Assert.True(recurring.IsSuccess);
        Assert.NotEmpty(recurring.Value.Items);

        // A recurring task is not a project, so the two filters must not be the same thing.
        Assert.All(recurring.Value.Items, t => Assert.True(t.IsRecurring));
        Assert.Contains(recurring.Value.Items, t => !t.IsProject);
    }

    // -----------------------------------------------------------------------
    // TaskFile
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FileUploadedToATaskLandsOnTaskIdOnly()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Has a file"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                task.Value.Id, new UploadFileRequest("notes.txt", "text/plain", new byte[64]), default));

        Assert.True(upload.IsSuccess);

        await fixture.ScopedAsync(async sp =>
        {
            var link = await sp.GetRequiredService<TaskManagementDbContext>()
                .TaskFiles.SingleAsync(f => f.Id == upload.Value.LinkId);
            Assert.Equal(task.Value.Id, link.TaskId);
            Assert.Null(link.SubTaskId);
        });
    }

    [Fact]
    public async Task FileUploadedToASubTaskLandsOnSubTaskIdOnly()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Has subtask file", "Step"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToSubTaskAsync(
                project.Value.SubTasks.Single().Id,
                new UploadFileRequest("s.txt", "text/plain", new byte[32]), default));

        Assert.True(upload.IsSuccess);

        await fixture.ScopedAsync(async sp =>
        {
            var link = await sp.GetRequiredService<TaskManagementDbContext>()
                .TaskFiles.SingleAsync(f => f.Id == upload.Value.LinkId);
            Assert.Null(link.TaskId);
            Assert.NotNull(link.SubTaskId);
        });
    }

    [Fact]
    public async Task FileOfExactlyTheDefaultConfiguredLimitIsAccepted()
    {
        if (Skip) return;

        // No Uploads.MaxFileSizeKb setting configured for this tenant: the enforced limit is the
        // default (200 KB), well under TaskFileAsset's hard ceiling.
        var defaultLimitBytes = UploadPolicySettings.DefaultMaxFileSizeKb * 1024;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Boundary ok"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                task.Value.Id,
                new UploadFileRequest("big.pdf", "application/pdf",
                    new byte[defaultLimitBytes]),
                default));

        Assert.True(upload.IsSuccess);
        Assert.Equal(defaultLimitBytes, upload.Value.FileSizeBytes);
    }

    [Fact]
    public async Task FileOneByteOverTheDefaultConfiguredLimitIsRejected()
    {
        if (Skip) return;

        var defaultLimitBytes = UploadPolicySettings.DefaultMaxFileSizeKb * 1024;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Boundary fail"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                task.Value.Id,
                new UploadFileRequest("toobig.pdf", "application/pdf",
                    new byte[defaultLimitBytes + 1]),
                default));

        Assert.True(upload.IsFailure);
    }

    [Fact]
    public async Task DisallowedFileTypeIsRejected()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Type rejected"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                task.Value.Id,
                new UploadFileRequest("script.exe", "application/octet-stream", new byte[32]),
                default));

        Assert.True(upload.IsFailure);
    }

    [Fact]
    public async Task UploadingToANonExistentTaskIsRejected()
    {
        if (Skip) return;

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                Guid.NewGuid(), new UploadFileRequest("x.txt", "text/plain", new byte[8]), default));

        Assert.True(upload.IsFailure);
    }

    [Fact]
    public async Task ClientFileNameIsNotUsedAsTheStoredName()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Path traversal"), default));

        var upload = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskFileService>().UploadToTaskAsync(
                task.Value.Id,
                new UploadFileRequest("../../../etc/passwd", "text/plain", new byte[8]),
                default));

        Assert.True(upload.IsSuccess);

        await fixture.ScopedAsync(async sp =>
        {
            var asset = await sp.GetRequiredService<TaskManagementDbContext>()
                .Files.SingleAsync(f => f.Id == upload.Value.FileId);

            Assert.DoesNotContain("..", asset.StoredFileName);
            Assert.DoesNotContain("/", asset.StoredFileName);
            Assert.DoesNotContain("\\", asset.StoredFileName);
            Assert.DoesNotContain("..", asset.StoragePath);
        });
    }

    [Fact]
    public async Task DatabaseRefusesAFileLinkWithBothOwners()
    {
        if (Skip) return;

        // The service can never build this row; the constraint is the backstop if anything
        // ever writes to the table directly.
        await fixture.ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<TaskManagementDbContext>();
            var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO task_management.TaskFiles (Id, FileId, TaskId, SubTaskId) " +
                    "VALUES (NEWID(), NEWID(), NEWID(), NEWID())");
            });

            // Name the constraint, so this cannot pass because some other rule fired.
            Assert.Contains("CK_TaskFiles_ExactlyOneOwner", error.ToString());
        });
    }

    // -----------------------------------------------------------------------
    // TaskTag
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TagCanBeAttachedToATaskAndToASubTask()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Tagged", "Step"), default));

        var tag = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITagService>()
                .CreateAsync(new CreateTagRequest($"urgent-{Guid.NewGuid():N}"), default));

        var onTask = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITagService>()
                .AssignToTaskAsync(project.Value.Id, new AssignTagRequest(tag.Value.Id), default));
        var onSubTask = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITagService>()
                .AssignToSubTaskAsync(project.Value.SubTasks.Single().Id, new AssignTagRequest(tag.Value.Id), default));

        Assert.True(onTask.IsSuccess);
        Assert.True(onSubTask.IsSuccess);
    }

    [Fact]
    public async Task AttachingTheSameTagTwiceDoesNotDuplicateTheLink()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("No duplicates"), default));
        var tag = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITagService>()
                .CreateAsync(new CreateTagRequest($"dup-{Guid.NewGuid():N}"), default));

        await fixture.ScopedAsync(sp => sp.GetRequiredService<ITagService>()
            .AssignToTaskAsync(task.Value.Id, new AssignTagRequest(tag.Value.Id), default));
        await fixture.ScopedAsync(sp => sp.GetRequiredService<ITagService>()
            .AssignToTaskAsync(task.Value.Id, new AssignTagRequest(tag.Value.Id), default));

        var links = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<TaskManagementDbContext>()
                .TaskTags.CountAsync(t => t.TagId == tag.Value.Id && t.TaskId == task.Value.Id));

        Assert.Equal(1, links);
    }

    [Fact]
    public async Task DetachingATagFromATaskLeavesItsSubTaskLinkIntact()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(Project("Keep the other", "Step"), default));
        var subTaskId = project.Value.SubTasks.Single().Id;

        var tag = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITagService>()
                .CreateAsync(new CreateTagRequest($"shared-{Guid.NewGuid():N}"), default));

        await fixture.ScopedAsync(sp => sp.GetRequiredService<ITagService>()
            .AssignToTaskAsync(project.Value.Id, new AssignTagRequest(tag.Value.Id), default));
        await fixture.ScopedAsync(sp => sp.GetRequiredService<ITagService>()
            .AssignToSubTaskAsync(subTaskId, new AssignTagRequest(tag.Value.Id), default));

        await fixture.ScopedAsync(sp => sp.GetRequiredService<ITagService>()
            .RemoveFromTaskAsync(project.Value.Id, tag.Value.Id, default));

        await fixture.ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<TaskManagementDbContext>();

            // The task link is gone, the subtask link survives, and the tag itself is untouched.
            Assert.False(await db.TaskTags.AnyAsync(t => t.TagId == tag.Value.Id && t.TaskId == project.Value.Id));
            Assert.True(await db.TaskTags.AnyAsync(t => t.TagId == tag.Value.Id && t.SubTaskId == subTaskId));
            Assert.True(await db.Tags.AnyAsync(t => t.Id == tag.Value.Id));
        });
    }

    [Fact]
    public async Task DatabaseRefusesATagLinkWithNoOwner()
    {
        if (Skip) return;

        await fixture.ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<TaskManagementDbContext>();
            var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO task_management.TaskTags (Id, TagId, TaskId, SubTaskId) " +
                    "VALUES (NEWID(), NEWID(), NULL, NULL)");
            });

            Assert.Contains("CK_TaskTags_AtLeastOneOwner", error.ToString());
        });
    }

    // -----------------------------------------------------------------------
    // Notes - ownership
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ANoteIsInvisibleToAnotherUser()
    {
        if (Skip) return;

        var note = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<INoteService>()
                .CreateAsync(new CreateNoteRequest("Private", "secret"), default),
            fixture.OwnerUserId);

        Assert.True(note.IsSuccess);

        var asOther = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<INoteService>().GetAsync(note.Value.Id, default),
            fixture.OtherUserId);

        Assert.True(asOther.IsFailure);

        var editAttempt = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<INoteService>()
                .UpdateAsync(note.Value.Id, new UpdateNoteRequest("Hijacked", "x"), default),
            fixture.OtherUserId);
        Assert.True(editAttempt.IsFailure);

        var deleteAttempt = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<INoteService>().DeleteAsync(note.Value.Id, default),
            fixture.OtherUserId);
        Assert.True(deleteAttempt.IsFailure);

        // And it is still there, unchanged.
        var mine = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<INoteService>().GetAsync(note.Value.Id, default),
            fixture.OwnerUserId);
        Assert.True(mine.IsSuccess);
        Assert.Equal("Private", mine.Value.Title);
    }

    // -----------------------------------------------------------------------
    // Users and groups
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TaskCanBeAssignedToAUserAndAUserGroup()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Assigned", new DateOnly(2026, 6, 1),
                    AssignedUserId: fixture.OtherUserId,
                    AssignedUserGroupId: fixture.UserGroupId,
                    AssigneeUserIds: [fixture.OwnerUserId]),
                default));

        Assert.True(task.IsSuccess);
        Assert.Equal(fixture.OtherUserId, task.Value.AssignedUser?.Id);
        Assert.Equal(fixture.UserGroupId, task.Value.AssignedUserGroup?.Id);
        Assert.Single(task.Value.Assignees);
    }

    [Fact]
    public async Task AssigningToAUserThatDoesNotExistIsRejected()
    {
        if (Skip) return;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Bad assignee", new DateOnly(2026, 6, 1), AssignedUserId: Guid.NewGuid()),
                default));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AssigningToAUserGroupThatDoesNotExistIsRejected()
    {
        if (Skip) return;

        var result = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(
                new CreateTaskRequest(
                    "Bad team", new DateOnly(2026, 6, 1), AssignedUserGroupId: Guid.NewGuid(), AssignedUserId: fixture.OwnerUserId),
                default));

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Tenant isolation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ATaskFromAnotherTenantIsNotReadable()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Tenant scoped"), default));

        var stolen = await fixture.ScopedAsync(async sp =>
        {
            var currentUser = sp.GetRequiredService<TestUserContext>();
            currentUser.TenantId = Guid.NewGuid();     // a different tenant
            return await sp.GetRequiredService<ITaskService>().GetAsync(task.Value.Id, default);
        });

        Assert.True(stolen.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Status and history
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompletingATaskStampsTheCompletionDate()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Finish me"), default));

        var done = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().ChangeStatusAsync(
                task.Value.Id, new ChangeTaskStatusRequest(TaskItemStatus.Completed), default));

        Assert.True(done.IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, done.Value.Status);
        Assert.NotNull(done.Value.ActualCompletionDateUtc);
    }

    [Fact]
    public async Task TaskHistoryIsRecordedInTheSharedAuditLog()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Tracked"), default));

        await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().ChangeStatusAsync(
                task.Value.Id, new ChangeTaskStatusRequest(TaskItemStatus.InProgress), default));

        var history = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskActivityService>().GetForTaskAsync(task.Value.Id, default));

        Assert.True(history.IsSuccess);
        Assert.Contains(history.Value, e => e.Action == "Status changed");
    }

    [Fact]
    public async Task CommentsAreEditableOnlyByTheirAuthor()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Discussed"), default));

        var comment = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskCommentService>()
                .CreateAsync(task.Value.Id, new CreateTaskCommentRequest("Looks good"), default),
            fixture.OwnerUserId);

        Assert.True(comment.IsSuccess);

        var hijack = await fixture.ScopedAsync(sp =>
            sp.GetRequiredService<ITaskCommentService>()
                .UpdateAsync(comment.Value.Id, new UpdateTaskCommentRequest("Edited"), default),
            fixture.OtherUserId);

        Assert.True(hijack.IsFailure);
    }
}
