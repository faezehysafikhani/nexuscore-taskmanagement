using FluentValidation.TestHelper;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Application.Validators;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Tests;

public sealed class TaskFileAssetTests
{
    [Fact]
    public void MaxFileSize_Is200Kilobytes() =>
        Assert.Equal(204_800, TaskFileAsset.MaxFileSizeBytes);

    [Fact]
    public void AcceptsAFileOfExactlyTheLimit()
    {
        var asset = Create(TaskFileAsset.MaxFileSizeBytes);
        Assert.Equal(204_800, asset.FileSizeBytes);
    }

    [Fact]
    public void RejectsAFileOneByteOverTheLimit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(TaskFileAsset.MaxFileSizeBytes + 1));

    [Fact]
    public void RejectsAnEmptyFile() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(0));

    private static TaskFileAsset Create(int sizeBytes) => new(
        Guid.NewGuid(), Guid.NewGuid(), "report.pdf", "stored.pdf",
        "application/pdf", sizeBytes, "path/stored.pdf", Guid.NewGuid());
}

public sealed class TaskTagTests
{
    [Fact]
    public void DetachingOneOwner_KeepsTheRowAliveForTheOther()
    {
        var link = TaskTag.ForBoth(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        link.DetachTask();

        // The subtask link must survive - deleting the row would silently drop it.
        Assert.Null(link.TaskId);
        Assert.NotNull(link.SubTaskId);
        Assert.False(link.IsOrphaned);
    }

    [Fact]
    public void RowBecomesOrphaned_OnlyOnceBothOwnersAreGone()
    {
        var link = TaskTag.ForBoth(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        link.DetachTask();
        Assert.False(link.IsOrphaned);

        link.DetachSubTask();
        Assert.True(link.IsOrphaned);
    }

    [Fact]
    public void ForTask_SetsOnlyTheTaskSide()
    {
        var link = TaskTag.ForTask(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.NotNull(link.TaskId);
        Assert.Null(link.SubTaskId);
    }
}

public sealed class TaskFileLinkTests
{
    [Fact]
    public void ForTask_LeavesTheSubTaskSideNull()
    {
        var link = TaskFile.ForTask(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.NotNull(link.TaskId);
        Assert.Null(link.SubTaskId);
    }

    [Fact]
    public void ForSubTask_LeavesTheTaskSideNull()
    {
        var link = TaskFile.ForSubTask(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(link.TaskId);
        Assert.NotNull(link.SubTaskId);
    }
}

public sealed class TaskItemTests
{
    private static TaskItem NewTask(bool isProject = false) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Write the report",
        new DateOnly(2026, 3, 1), TaskPriority.Medium, isProject);

    [Fact]
    public void IsRecurring_FollowsTheSchedule_NotAStoredFlag()
    {
        var task = NewTask();
        Assert.False(task.IsRecurring);

        task.AttachRecurrence(new RepetitiveTask(
            Guid.NewGuid(), task.TenantId, task.Id, RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1)));

        Assert.True(task.IsRecurring);

        task.ClearRecurrence();
        Assert.False(task.IsRecurring);
    }

    [Fact]
    public void CompletingATask_StampsTheCompletionDate()
    {
        var task = NewTask();
        var now = DateTimeOffset.UtcNow;

        task.ChangeStatus(TaskItemStatus.Completed, now);

        Assert.Equal(now, task.ActualCompletionDateUtc);
    }

    [Fact]
    public void ReopeningATask_ClearsTheCompletionDate()
    {
        var task = NewTask();
        task.ChangeStatus(TaskItemStatus.Completed, DateTimeOffset.UtcNow);

        task.ChangeStatus(TaskItemStatus.InProgress, DateTimeOffset.UtcNow);

        Assert.Null(task.ActualCompletionDateUtc);
    }

    [Fact]
    public void CreatingATask_RaisesTheCreatedEvent() =>
        Assert.Contains(NewTask().DomainEvents, e => e is TaskItemCreated);

    [Fact]
    public void RaisingRecurrenceDue_CarriesTheTaskAndTenant()
    {
        var task = NewTask();
        var scheduleId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow;

        task.RaiseRecurrenceDue(scheduleId, dueAt);

        var raised = Assert.IsType<RepetitiveTaskDue>(
            task.DomainEvents.Single(e => e is RepetitiveTaskDue));
        Assert.Equal(scheduleId, raised.RepetitiveTaskId);
        Assert.Equal(task.Id, raised.TaskId);
        Assert.Equal(task.TenantId, raised.TenantId);
    }

    [Fact]
    public void AssigningUsers_DeduplicatesThem()
    {
        var task = NewTask();
        var userId = Guid.NewGuid();

        task.AssignUsers([userId, userId, Guid.NewGuid()]);

        Assert.Equal(2, task.Assignees.Count);
    }
}

public sealed class RepetitiveTaskTests
{
    private static RepetitiveTask NewSchedule() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1));

    [Fact]
    public void SwitchingToDaily_ClearsTheWeeklyOnlySettings()
    {
        var schedule = NewSchedule();
        schedule.UpdateSchedule(
            RecurrenceFrequency.Weekly, 2, null, null, [0, 3], null, null, null,
            new DateOnly(2026, 1, 1), null);
        Assert.NotEmpty(schedule.WeeklyDays);

        schedule.UpdateSchedule(
            RecurrenceFrequency.Daily, 2, null, null, [0, 3], null, null, null,
            new DateOnly(2026, 1, 1), null);

        // Leaving stale weekly settings behind would make the row lie about itself.
        Assert.Empty(schedule.WeeklyDays);
        Assert.Null(schedule.IntervalWeeks);
    }

    [Fact]
    public void MarkExecuted_WithNoFurtherRun_DeactivatesTheSchedule()
    {
        var schedule = NewSchedule();

        schedule.MarkExecuted(DateTimeOffset.UtcNow, null);

        Assert.False(schedule.IsActive);
        Assert.Null(schedule.NextExecutionAtUtc);
    }

    [Fact]
    public void MarkExecuted_WithAFurtherRun_StaysActive()
    {
        var schedule = NewSchedule();
        var next = DateTimeOffset.UtcNow.AddDays(1);

        schedule.MarkExecuted(DateTimeOffset.UtcNow, next);

        Assert.True(schedule.IsActive);
        Assert.Equal(next, schedule.NextExecutionAtUtc);
    }
}

public sealed class ValidatorTests
{
    [Fact]
    public void AProjectWithoutSubtasksIsRejected()
    {
        var result = new CreateTaskRequestValidator().TestValidate(new CreateTaskRequest(
            "Launch", new DateOnly(2026, 3, 1), IsProject: true, SubTasks: []));

        result.ShouldHaveValidationErrorFor(x => x.SubTasks);
    }

    [Fact]
    public void APlainTaskWithoutSubtasksIsFine()
    {
        var result = new CreateTaskRequestValidator().TestValidate(new CreateTaskRequest(
            "Buy milk", new DateOnly(2026, 3, 1)));

        result.ShouldNotHaveValidationErrorFor(x => x.SubTasks);
    }

    [Fact]
    public void SomeoneResponsibleIsRequired()
    {
        var validator = new CreateTaskRequestValidator();

        validator.TestValidate(new CreateTaskRequest("Buy milk", new DateOnly(2026, 3, 1)))
            .ShouldHaveValidationErrorFor(x => x.AssignedUserId);
        validator.TestValidate(new CreateTaskRequest("Buy milk", new DateOnly(2026, 3, 1), AssignedUserId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.AssignedUserId);
        validator.TestValidate(new CreateTaskRequest("Buy milk", new DateOnly(2026, 3, 1), AssignedUserId: Guid.NewGuid()))
            .ShouldNotHaveValidationErrorFor(x => x.AssignedUserId);
    }

    [Fact]
    public void ATitleIsRequired()
    {
        var result = new CreateTaskRequestValidator().TestValidate(new CreateTaskRequest(
            "", new DateOnly(2026, 3, 1)));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void ATitleOver200CharactersIsRejected()
    {
        var result = new CreateTaskRequestValidator().TestValidate(new CreateTaskRequest(
            new string('x', 201), new DateOnly(2026, 3, 1)));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void AWeeklyScheduleWithNoDaysIsRejected()
    {
        var result = new CreateRepetitiveTaskRequestValidator().TestValidate(
            new CreateRepetitiveTaskRequest(Guid.NewGuid(), new RecurrenceInput(
                RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), WeeklyDays: [])));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AWeeklyScheduleWithADayIsAccepted()
    {
        var result = new CreateRepetitiveTaskRequestValidator().TestValidate(
            new CreateRepetitiveTaskRequest(Guid.NewGuid(), new RecurrenceInput(
                RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), WeeklyDays: [0, 2])));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ADayOfWeekAboveSixIsRejected()
    {
        var result = new CreateRepetitiveTaskRequestValidator().TestValidate(
            new CreateRepetitiveTaskRequest(Guid.NewGuid(), new RecurrenceInput(
                RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), WeeklyDays: [7])));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ARecurrenceEndingBeforeItStartsIsRejected()
    {
        var result = new UpdateRepetitiveTaskRequestValidator().TestValidate(
            new UpdateRepetitiveTaskRequest(new RecurrenceInput(
                RecurrenceFrequency.Daily,
                new DateOnly(2026, 5, 1),
                EndDate: new DateOnly(2026, 1, 1))));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AFileOfExactlyTheLimitPassesValidation()
    {
        var result = new UploadFileRequestValidator().TestValidate(new UploadFileRequest(
            "a.pdf", "application/pdf", new byte[TaskFileAsset.MaxFileSizeBytes]));

        result.ShouldNotHaveValidationErrorFor(x => x.Content);
    }

    [Fact]
    public void AFileOneByteOverTheLimitFailsValidation()
    {
        var result = new UploadFileRequestValidator().TestValidate(new UploadFileRequest(
            "a.pdf", "application/pdf", new byte[TaskFileAsset.MaxFileSizeBytes + 1]));

        result.ShouldHaveValidationErrorFor(x => x.Content);
    }

    [Fact]
    public void AnEmptyFileFailsValidation()
    {
        var result = new UploadFileRequestValidator().TestValidate(new UploadFileRequest(
            "a.pdf", "application/pdf", []));

        result.ShouldHaveValidationErrorFor(x => x.Content);
    }

    [Fact]
    public void ACommentMayHaveNoText_BecauseItCanBeAttachmentsOnly()
    {
        // The UI allows a comment that is only files; the files are uploaded right after it.
        new CreateTaskCommentRequestValidator().TestValidate(new CreateTaskCommentRequest(""))
            .ShouldNotHaveValidationErrorFor(x => x.Text);
    }

    [Fact]
    public void ACommentTextOverTheLimitIsRejected()
    {
        new CreateTaskCommentRequestValidator().TestValidate(new CreateTaskCommentRequest(new string('x', 4001)))
            .ShouldHaveValidationErrorFor(x => x.Text);
        new UpdateTaskCommentRequestValidator().TestValidate(new UpdateTaskCommentRequest(null!))
            .ShouldHaveValidationErrorFor(x => x.Text);
    }

    [Fact]
    public void ANoteWithoutATitleIsRejected()
    {
        var result = new CreateNoteRequestValidator().TestValidate(
            new CreateNoteRequest("", "body"));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }
}
