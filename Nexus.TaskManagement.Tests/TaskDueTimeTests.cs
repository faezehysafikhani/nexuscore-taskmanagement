using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// The time of day a user picks for a task or subtask survives saving, reading back, editing and
/// saving again - against real SQL Server. Before DueTime existed the column was date-only and
/// every task came back at 00:00.
/// </summary>
[Collection("sqlserver")]
public sealed class TaskDueTimeTests(SqlServerFixture fixture)
{
    private bool Skip => !fixture.Available;

    private static UpdateTaskRequest Resave(TaskDto task, TimeOnly? dueTime) =>
        new(task.Title, task.DueDate, task.Priority, task.Description, DueTime: dueTime);

    [Fact]
    public async Task DueTime_IsStoredAndReadBack_AndKeptWhenTheTaskIsSavedAgain()
    {
        if (Skip) return;

        var created = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            new CreateTaskRequest("Meeting", new DateOnly(2026, 9, 23), DueTime: new TimeOnly(14, 30), AssignedUserId: fixture.OwnerUserId), default));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
        Assert.Equal(new TimeOnly(14, 30), created.Value!.DueTime);

        var read = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().GetAsync(created.Value!.Id, default));
        Assert.Equal(new DateOnly(2026, 9, 23), read.Value!.DueDate);
        Assert.Equal(new TimeOnly(14, 30), read.Value.DueTime);

        // Edit form opened and saved again without touching the date.
        var resaved = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .UpdateAsync(read.Value!.Id, Resave(read.Value, read.Value.DueTime), default));
        Assert.Equal(new TimeOnly(14, 30), resaved.Value!.DueTime);

        // Other writes that rebuild the details (priority, status) must not reset it either.
        var priority = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .ChangePriorityAsync(read.Value!.Id, new ChangeTaskPriorityRequest(TaskPriority.High), default));
        Assert.Equal(new TimeOnly(14, 30), priority.Value!.DueTime);

        var listed = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .ListAsync(new ListTasksRequest(fixture.TenantId, PageSize: 200), default));
        Assert.Equal(new TimeOnly(14, 30), listed.Value!.Items.Single(t => t.Id == read.Value!.Id).DueTime);
    }

    [Fact]
    public async Task MidnightIsARealTime_DistinctFromNoTime()
    {
        if (Skip) return;

        var midnight = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            new CreateTaskRequest("At midnight", new DateOnly(2026, 9, 24), DueTime: TimeOnly.MinValue, AssignedUserId: fixture.OwnerUserId), default));
        Assert.Equal(TimeOnly.MinValue, midnight.Value!.DueTime);

        var dateOnly = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            new CreateTaskRequest("Any time that day", new DateOnly(2026, 9, 24), AssignedUserId: fixture.OwnerUserId), default));
        Assert.Null(dateOnly.Value!.DueTime);

        var changed = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .UpdateAsync(midnight.Value!.Id, Resave(midnight.Value, new TimeOnly(8, 5)), default));
        Assert.Equal(new TimeOnly(8, 5), changed.Value!.DueTime);
    }

    [Fact]
    public async Task CharterTimes_AreStoredWithTheirDates_AndReplacedOnEdit()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            new CreateTaskRequest("Chartered", new DateOnly(2026, 11, 30), IsProject: true,
                CharterStartDate: new DateOnly(2026, 11, 1), CharterEndDate: new DateOnly(2026, 11, 30),
                CharterStartTime: new TimeOnly(8, 15), CharterEndTime: new TimeOnly(17, 45),
                SubTasks: [new SubTaskInput("Design", SubTaskImportance.Medium)], AssignedUserId: fixture.OwnerUserId), default));
        Assert.True(project.IsSuccess, project.IsFailure ? project.Error.Message : null);

        var read = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().GetAsync(project.Value!.Id, default));
        Assert.Equal(new DateOnly(2026, 11, 1), read.Value!.CharterStartDate);
        Assert.Equal(new TimeOnly(8, 15), read.Value.CharterStartTime);
        Assert.Equal(new TimeOnly(17, 45), read.Value.CharterEndTime);

        var edited = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().UpdateAsync(read.Value!.Id,
            new UpdateTaskRequest(read.Value.Title, read.Value.DueDate, read.Value.Priority, IsProject: true,
                CharterStartDate: new DateOnly(2026, 11, 2), CharterEndDate: new DateOnly(2026, 11, 30),
                CharterStartTime: TimeOnly.MinValue), default));
        Assert.Equal(TimeOnly.MinValue, edited.Value!.CharterStartTime);
        Assert.Null(edited.Value.CharterEndTime);
    }

    [Fact]
    public async Task SubTaskTimes_AreStoredWithTheirDates()
    {
        if (Skip) return;

        var project = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            new CreateTaskRequest("Project", new DateOnly(2026, 10, 5), IsProject: true, SubTasks:
            [
                new SubTaskInput("Design", StartDate: new DateOnly(2026, 10, 1), EndDate: new DateOnly(2026, 10, 2),
                    StartTime: new TimeOnly(9, 0), EndTime: new TimeOnly(17, 45)),
            ], AssignedUserId: fixture.OwnerUserId), default));
        Assert.True(project.IsSuccess, project.IsFailure ? project.Error.Message : null);
        var design = Assert.Single(project.Value!.SubTasks);
        Assert.Equal(new TimeOnly(9, 0), design.StartTime);
        Assert.Equal(new TimeOnly(17, 45), design.EndTime);

        var updated = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().UpdateSubTaskAsync(design.Id,
            new UpdateSubTaskRequest("Design", SubTaskImportance.Medium, new DateOnly(2026, 10, 1), null, 0,
                StartTime: new TimeOnly(10, 15), EndTime: new TimeOnly(18, 0)), default));
        Assert.Equal(new TimeOnly(10, 15), updated.Value!.StartTime);
        Assert.Null(updated.Value.EndTime); // no end date, so no end time

        var added = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateSubTaskAsync(project.Value!.Id,
            new CreateSubTaskRequest("Build", StartDate: new DateOnly(2026, 10, 3), StartTime: new TimeOnly(0, 0)), default));
        Assert.Equal(TimeOnly.MinValue, added.Value!.StartTime);

        var reread = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().GetSubTaskAsync(design.Id, default));
        Assert.Equal(new TimeOnly(10, 15), reread.Value!.StartTime);
    }
}
