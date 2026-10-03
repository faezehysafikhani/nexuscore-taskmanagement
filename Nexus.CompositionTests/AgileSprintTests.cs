using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.CompositionTests;

public sealed class AgileSprintTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    // ---------------------------------------------------------------- sprints

    [Fact]
    public async Task Sprints_AreNumberedPerProject_AndDefaultTheirName()
    {
        var f = new AgileFixture();

        var first = await f.AddSprintAsync();
        var second = await f.AddSprintAsync(name: "  Beta  ");

        Assert.Equal((1, "Sprint 1", SprintStatus.Planned), (first.Number, first.Name, first.Status));
        Assert.Equal((2, "Beta"), (second.Number, second.Name));

        var elsewhere = await f.SprintService.CreateAsync(new CreateSprintRequest(AgileFixture.Tenant, Guid.NewGuid(), null, null, null, null), default);
        Assert.Equal(1, elsewhere.Value!.Number); // numbering is per project
    }

    [Fact]
    public async Task ASprint_RejectsAnEndBeforeItsStart_AndUnknownIds()
    {
        var f = new AgileFixture();

        var bad = await f.SprintService.CreateAsync(new CreateSprintRequest(AgileFixture.Tenant, f.ProjectId, null, null, Day(5), Day(1)), default);
        Assert.Equal("validation.error", bad.Error.Code);

        var sprint = await f.AddSprintAsync();
        Assert.Equal("validation.error", (await f.SprintService.UpdateAsync(sprint.Id, new UpdateSprintRequest("S", null, Day(5), Day(1)), default)).Error.Code);
        Assert.Equal("validation.error", (await f.SprintService.UpdateAsync(sprint.Id, new UpdateSprintRequest("  ", null, null, null), default)).Error.Code);
        Assert.Equal("not_found", (await f.SprintService.GetAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.Equal("not_found", (await f.SprintService.UpdateAsync(Guid.NewGuid(), new UpdateSprintRequest("S", null, null, null), default)).Error.Code);
    }

    [Fact]
    public async Task APlannedSprint_CanBeEdited_AndTheListSummarisesItsTasks()
    {
        var f = new AgileFixture();
        var sprint = await f.AddSprintAsync();
        await f.AddTaskAsync("A", points: 3, sprint: 1);
        await f.AddTaskAsync("B", points: 5, sprint: 1, status: AgileTaskStatus.Done);
        await f.AddTaskAsync("Elsewhere", points: 100);

        var updated = await f.SprintService.UpdateAsync(sprint.Id, new UpdateSprintRequest("Renamed", "ship it", Day(0), Day(13)), default);
        Assert.Equal(("Renamed", "ship it", Day(0), Day(13)), (updated.Value!.Name, updated.Value.Goal, updated.Value.StartDate, updated.Value.EndDate));

        var listed = (await f.SprintService.ListByProjectAsync(f.ProjectId, default)).Value!.Single();
        Assert.Equal((2, 8, 5), (listed.TaskCount, listed.TotalPoints, listed.DonePoints));
    }

    [Fact]
    public async Task Starting_NeedsDates_AndOnlyOneSprintCanBeActive()
    {
        var f = new AgileFixture();
        var undated = await f.AddSprintAsync();
        Assert.Equal("validation.error", (await f.SprintService.StartAsync(undated.Id, new StartSprintRequest(null, null), default)).Error.Code);

        // Dates can be given while starting.
        var started = await f.SprintService.StartAsync(undated.Id, new StartSprintRequest(Day(0), Day(13)), default);
        Assert.Equal((SprintStatus.Active, Day(0), Day(13)), (started.Value!.Status, started.Value.StartDate, started.Value.EndDate));

        var second = await f.AddSprintAsync(Day(14), Day(27));
        Assert.Equal("conflict", (await f.SprintService.StartAsync(second.Id, new StartSprintRequest(null, null), default)).Error.Code);
        Assert.Equal("conflict", (await f.SprintService.StartAsync(undated.Id, new StartSprintRequest(null, null), default)).Error.Code); // already active
        Assert.Equal("validation.error", (await f.SprintService.StartAsync(second.Id, new StartSprintRequest(Day(20), Day(10)), default)).Error.Code);
    }

    [Fact]
    public async Task OnlyAPlannedEmptySprint_CanBeDeleted()
    {
        var f = new AgileFixture();
        var withTask = await f.AddSprintAsync(Day(0), Day(13));
        await f.AddTaskAsync("T", sprint: 1);
        Assert.Equal("conflict", (await f.SprintService.DeleteAsync(withTask.Id, default)).Error.Code);

        var empty = await f.AddSprintAsync();
        Assert.True((await f.SprintService.DeleteAsync(empty.Id, default)).IsSuccess);
        Assert.Equal("not_found", (await f.SprintService.DeleteAsync(empty.Id, default)).Error.Code);

        await f.SprintService.StartAsync(withTask.Id, new StartSprintRequest(null, null), default);
        f.Tasks.Items.Clear();
        Assert.Equal("conflict", (await f.SprintService.DeleteAsync(withTask.Id, default)).Error.Code); // active: part of history
    }

    // -------------------------------------------------------- assigning tasks

    [Fact]
    public async Task Tasks_CanBeAssignedToASprintInBulk_AndRemovedBackToTheBacklog()
    {
        var f = new AgileFixture();
        var sprint = await f.AddSprintAsync();
        var a = await f.AddTaskAsync("A", points: 3);
        var b = await f.AddTaskAsync("B", points: 5);

        var assigned = await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([a.Id, b.Id, a.Id]), default);

        Assert.Equal((2, 8), (assigned.Value!.TaskCount, assigned.Value.TotalPoints));
        Assert.Equal(1, f.Stored(a.Id).SprintNumber);

        var removed = await f.SprintService.RemoveTaskAsync(sprint.Id, a.Id, default);
        Assert.Equal((1, 5), (removed.Value!.TaskCount, removed.Value.TotalPoints));
        Assert.Null(f.Stored(a.Id).SprintNumber);
        Assert.Equal("not_found", (await f.SprintService.RemoveTaskAsync(sprint.Id, a.Id, default)).Error.Code); // no longer in it
    }

    [Fact]
    public async Task Assigning_RejectsEmptyLists_UnknownTasks_AndOtherProjectsTasks()
    {
        var f = new AgileFixture();
        var sprint = await f.AddSprintAsync();
        var foreign = await f.TaskService.CreateAsync(
            new CreateAgileTaskRequest(AgileFixture.Tenant, Guid.NewGuid(), "Foreign", null, null, null, null, AgileTaskPriority.Low, null), default);
        var mine = await f.AddTaskAsync("Mine");

        Assert.Equal("validation.error", (await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([]), default)).Error.Code);
        Assert.Equal("not_found", (await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([mine.Id, Guid.NewGuid()]), default)).Error.Code);
        Assert.Equal("not_found", (await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([mine.Id, foreign.Value!.Id]), default)).Error.Code);
        Assert.Null(f.Stored(mine.Id).SprintNumber); // nothing was half-assigned
    }

    [Fact]
    public async Task ACompletedSprint_TakesNoMoreTasks_FromAnyDoor()
    {
        var f = new AgileFixture();
        var sprint = await f.StartedSprintAsync(Day(0), Day(13));
        await f.SprintService.CompleteAsync(sprint.Id, new CompleteSprintRequest(null), default);
        var task = await f.AddTaskAsync("Late");

        Assert.Equal("conflict", (await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([task.Id]), default)).Error.Code);
        Assert.Equal("conflict", (await f.TaskService.UpdateAsync(task.Id,
            new UpdateAgileTaskRequest("Late", null, null, null, null, AgileTaskPriority.Low, 1), default)).Error.Code);
        Assert.Equal("conflict", (await f.TaskService.CreateAsync(
            new CreateAgileTaskRequest(AgileFixture.Tenant, f.ProjectId, "New", null, null, null, null, AgileTaskPriority.Low, 1), default)).Error.Code);
        Assert.Null(f.Stored(task.Id).SprintNumber);
    }

    [Fact]
    public async Task EditingATaskThatAlreadySitsInACompletedSprint_StillWorks()
    {
        var f = new AgileFixture();
        var sprint = await f.StartedSprintAsync(Day(0), Day(13));
        var done = await f.AddTaskAsync("Finished", sprint: 1, status: AgileTaskStatus.Done);
        await f.SprintService.CompleteAsync(sprint.Id, new CompleteSprintRequest(null), default);

        var renamed = await f.TaskService.UpdateAsync(done.Id,
            new UpdateAgileTaskRequest("Finished, renamed", null, null, null, null, AgileTaskPriority.Low, 1), default);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(1, renamed.Value!.SprintNumber);
    }

    [Fact]
    public async Task LegacySprintNumbers_WithNoSprintBehindThem_StillWork()
    {
        var f = new AgileFixture();

        var task = await f.AddTaskAsync("Old style", points: 3, sprint: 7);

        Assert.Equal(7, task.SprintNumber);
        Assert.Empty(f.Events.Items); // no Sprint 7 exists, so nothing is recorded for it
        Assert.Single((await f.Board.GetBoardAsync(f.ProjectId, 7, null, null, default)).Value!.Columns[0].Cards);
    }

    // ------------------------------------------------------------- completing

    [Fact]
    public async Task Completing_KeepsFinishedTasks_AndMovesTheRestToTheBacklog()
    {
        var f = new AgileFixture();
        var sprint = await f.StartedSprintAsync(Day(0), Day(13));
        var done = await f.AddTaskAsync("Done", points: 5, sprint: 1, status: AgileTaskStatus.Done);
        var open = await f.AddTaskAsync("Open", points: 3, sprint: 1);
        var review = await f.AddTaskAsync("In review", points: 2, sprint: 1, status: AgileTaskStatus.UnderReview);

        var result = await f.SprintService.CompleteAsync(sprint.Id, new CompleteSprintRequest(null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((SprintStatus.Completed, 1, 2), (result.Value!.Sprint.Status, result.Value.CompletedTasks, result.Value.CarriedOverTasks));
        Assert.Equal(1, f.Stored(done.Id).SprintNumber);
        Assert.Null(f.Stored(open.Id).SprintNumber);
        Assert.Null(f.Stored(review.Id).SprintNumber);
        Assert.Equal(AgileTaskStatus.UnderReview, f.Stored(review.Id).Status); // their progress is not undone
    }

    [Fact]
    public async Task Completing_CanCarryUnfinishedTasksIntoTheNextSprint()
    {
        var f = new AgileFixture();
        var current = await f.StartedSprintAsync(Day(0), Day(13));
        var next = await f.AddSprintAsync(Day(14), Day(27));
        var open = await f.AddTaskAsync("Open", points: 3, sprint: 1);

        var result = await f.SprintService.CompleteAsync(current.Id, new CompleteSprintRequest(next.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, f.Stored(open.Id).SprintNumber);
        Assert.Equal((1, 3), ((await f.SprintService.GetAsync(next.Id, default)).Value!.TaskCount, (await f.SprintService.GetAsync(next.Id, default)).Value!.TotalPoints));
    }

    [Fact]
    public async Task Completing_ValidatesTheTargetAndTheSprintsState()
    {
        var f = new AgileFixture();
        var planned = await f.AddSprintAsync(Day(0), Day(13));
        Assert.Equal("conflict", (await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(null), default)).Error.Code); // not active

        await f.SprintService.StartAsync(planned.Id, new StartSprintRequest(null, null), default);
        var foreign = await f.SprintService.CreateAsync(new CreateSprintRequest(AgileFixture.Tenant, Guid.NewGuid(), null, null, null, null), default);

        Assert.Equal("validation.error", (await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(foreign.Value!.Id), default)).Error.Code);
        Assert.Equal("validation.error", (await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(Guid.NewGuid()), default)).Error.Code);
        Assert.Equal("conflict", (await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(planned.Id), default)).Error.Code);
        Assert.Equal("not_found", (await f.SprintService.CompleteAsync(Guid.NewGuid(), new CompleteSprintRequest(null), default)).Error.Code);

        // Nothing above changed the sprint.
        Assert.Equal(SprintStatus.Active, f.Sprints.Items.Single(s => s.Id == planned.Id).Status);

        Assert.True((await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(null), default)).IsSuccess);
        Assert.Equal("conflict", (await f.SprintService.CompleteAsync(planned.Id, new CompleteSprintRequest(null), default)).Error.Code); // already completed
        Assert.Equal("conflict", (await f.SprintService.UpdateAsync(planned.Id, new UpdateSprintRequest("x", null, null, null), default)).Error.Code);
    }

    [Fact]
    public async Task ACompletedSprint_CanBeFollowedByAnotherActiveOne()
    {
        var f = new AgileFixture();
        var first = await f.StartedSprintAsync(Day(0), Day(13));
        await f.SprintService.CompleteAsync(first.Id, new CompleteSprintRequest(null), default);

        var second = await f.AddSprintAsync(Day(14), Day(27));

        Assert.True((await f.SprintService.StartAsync(second.Id, new StartSprintRequest(null, null), default)).IsSuccess);
    }

    // ------------------------------------------------------------ sprint events

    [Fact]
    public async Task Events_RecordScopeAndCompletion_WithPointsAndTime()
    {
        var f = new AgileFixture();
        await f.StartedSprintAsync(Day(0), Day(13));
        f.Clock.Set(Day(1));
        var task = await f.AddTaskAsync("T", points: 5, sprint: 1);
        f.Clock.Set(Day(3));
        await f.TaskService.ChangeStatusAsync(task.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.Done), default);
        f.Clock.Set(Day(4));
        await f.TaskService.ChangeStatusAsync(task.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.InProgress), default);

        Assert.Equal(
            [(SprintEventType.ScopeAdded, 5, 1), (SprintEventType.Completed, 5, 1), (SprintEventType.Reopened, 5, 1)],
            f.EventsFor(task.Id));
        Assert.Equal(Day(3), DateOnly.FromDateTime(f.Events.Items.Single(e => e.Type == SprintEventType.Completed).OccurredAtUtc.UtcDateTime));
    }

    [Fact]
    public async Task Events_FollowATaskAcrossSprintsAndBackToTheBacklog()
    {
        var f = new AgileFixture();
        var a = await f.AddSprintAsync();
        var b = await f.AddSprintAsync();
        var task = await f.AddTaskAsync("T", points: 2, status: AgileTaskStatus.Done);

        await f.SprintService.AssignTasksAsync(a.Id, new AssignSprintTasksRequest([task.Id]), default);
        await f.SprintService.AssignTasksAsync(b.Id, new AssignSprintTasksRequest([task.Id]), default);
        await f.SprintService.RemoveTaskAsync(b.Id, task.Id, default);

        Assert.Equal(
            [
                (SprintEventType.ScopeAdded, 2, 1), (SprintEventType.Completed, 2, 1),
                (SprintEventType.Reopened, 2, 1), (SprintEventType.ScopeRemoved, 2, 1),
                (SprintEventType.ScopeAdded, 2, 2), (SprintEventType.Completed, 2, 2),
                (SprintEventType.Reopened, 2, 2), (SprintEventType.ScopeRemoved, 2, 2)
            ],
            f.EventsFor(task.Id));
    }

    [Fact]
    public async Task Events_ReEstimatingATaskInASprint_RemovesTheOldSizeAndAddsTheNew()
    {
        var f = new AgileFixture();
        await f.AddSprintAsync();
        var open = await f.AddTaskAsync("Open", points: 3, sprint: 1);
        var done = await f.AddTaskAsync("Done", points: 3, sprint: 1, status: AgileTaskStatus.Done);

        await f.TaskService.SetStoryPointsAsync(open.Id, new SetStoryPointsRequest(8), default);
        await f.TaskService.SetStoryPointsAsync(done.Id, new SetStoryPointsRequest(5), default);

        Assert.Equal(
            [(SprintEventType.ScopeAdded, 3, 1), (SprintEventType.ScopeRemoved, 3, 1), (SprintEventType.ScopeAdded, 8, 1)],
            f.EventsFor(open.Id));
        Assert.Equal(
            [
                (SprintEventType.ScopeAdded, 3, 1), (SprintEventType.Completed, 3, 1),
                (SprintEventType.ScopeRemoved, 3, 1), (SprintEventType.ScopeAdded, 5, 1),
                (SprintEventType.Reopened, 3, 1), (SprintEventType.Completed, 5, 1)
            ],
            f.EventsFor(done.Id));
    }

    [Fact]
    public async Task Events_AreNotWrittenForChangesThatDoNotAffectASprint()
    {
        var f = new AgileFixture();
        await f.AddSprintAsync();
        var inSprint = await f.AddTaskAsync("In sprint", points: 3, sprint: 1);
        var backlog = await f.AddTaskAsync("Backlog", points: 3);
        var before = f.Events.Items.Count;

        await f.TaskService.ChangeStatusAsync(backlog.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.Done), default);   // not in a sprint
        await f.TaskService.ChangeStatusAsync(inSprint.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.InProgress), default); // neither starts nor leaves Done
        await f.TaskService.UpdateAsync(inSprint.Id, new UpdateAgileTaskRequest("Renamed", "desc", null, null, null, AgileTaskPriority.High, 1), default);

        Assert.Equal(before, f.Events.Items.Count);
    }

    [Fact]
    public async Task Deleting_ATask_TakesItOutOfTheSprintsScope()
    {
        var f = new AgileFixture();
        await f.AddSprintAsync();
        var task = await f.AddTaskAsync("T", points: 4, sprint: 1, status: AgileTaskStatus.Done);

        await f.TaskService.DeleteAsync(task.Id, default);

        Assert.Equal(
            [(SprintEventType.ScopeAdded, 4, 1), (SprintEventType.Completed, 4, 1), (SprintEventType.Reopened, 4, 1), (SprintEventType.ScopeRemoved, 4, 1)],
            f.EventsFor(task.Id));
    }

    [Fact]
    public async Task ACompletedSprintsHistory_IsFrozen_AndCarryOverIsNotAScopeChange()
    {
        var f = new AgileFixture();
        var current = await f.StartedSprintAsync(Day(0), Day(13));
        var next = await f.AddSprintAsync(Day(14), Day(27));
        f.Clock.Set(Day(1));
        var kept = await f.AddTaskAsync("Done", points: 5, sprint: 1, status: AgileTaskStatus.Done);
        var carried = await f.AddTaskAsync("Carried", points: 3, sprint: 1);
        f.Clock.Set(Day(13));

        await f.SprintService.CompleteAsync(current.Id, new CompleteSprintRequest(next.Id), default);

        // The unfinished task is noted as carried over from sprint 1 - its scope there is NOT removed -
        // and counted into sprint 2.
        Assert.Equal(
            [(SprintEventType.ScopeAdded, 3, 1), (SprintEventType.CarriedOver, 3, 1), (SprintEventType.ScopeAdded, 3, 2)],
            f.EventsFor(carried.Id));

        // Later activity on tasks left in the completed sprint does not rewrite its history.
        var before = f.Events.Items.Count(e => e.SprintNumber == 1);
        f.Clock.Set(Day(20));
        await f.TaskService.ChangeStatusAsync(kept.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.InProgress), default);
        await f.TaskService.SetStoryPointsAsync(kept.Id, new SetStoryPointsRequest(13), default);
        Assert.Equal(before, f.Events.Items.Count(e => e.SprintNumber == 1));
    }

    [Fact]
    public async Task ASprintDeletedWhilePlanned_TakesItsEventsWithIt()
    {
        var f = new AgileFixture();
        var sprint = await f.AddSprintAsync();
        var task = await f.AddTaskAsync("T", points: 2);
        await f.SprintService.AssignTasksAsync(sprint.Id, new AssignSprintTasksRequest([task.Id]), default);
        await f.SprintService.RemoveTaskAsync(sprint.Id, task.Id, default);
        Assert.NotEmpty(f.Events.Items);

        Assert.True((await f.SprintService.DeleteAsync(sprint.Id, default)).IsSuccess);

        Assert.Empty(f.Events.Items);
    }
}
