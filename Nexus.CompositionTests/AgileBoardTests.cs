using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.CompositionTests;

public sealed class AgileBoardTests
{
    // ------------------------------------------------------------- status & ranks

    [Fact]
    public void TheNewStatus_WasAppendedSoExistingValuesKeepTheirMeaning()
    {
        Assert.Equal((0, 1, 2, 3), ((int)AgileTaskStatus.ToDo, (int)AgileTaskStatus.InProgress, (int)AgileTaskStatus.Done, (int)AgileTaskStatus.UnderReview));
        Assert.Equal(
            [AgileTaskStatus.ToDo, AgileTaskStatus.InProgress, AgileTaskStatus.UnderReview, AgileTaskStatus.Done],
            AgileBoardService.ColumnOrder);
    }

    [Fact]
    public async Task NewTasks_GoToTheBottomOfTheirColumn()
    {
        var f = new AgileFixture();

        var a = await f.AddTaskAsync("A");
        var b = await f.AddTaskAsync("B");
        var c = await f.AddTaskAsync("C", status: AgileTaskStatus.InProgress);

        Assert.Equal([0, 1], new[] { a.Rank, b.Rank });
        Assert.Equal(0, c.Rank); // its own column
    }

    [Fact]
    public async Task ChangingStatus_DropsTheCardAtTheBottomOfTheNewColumn()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("First done", status: AgileTaskStatus.Done);
        var moved = await f.AddTaskAsync("Moves");

        var result = await f.TaskService.ChangeStatusAsync(moved.Id, new ChangeAgileTaskStatusRequest(AgileTaskStatus.Done), default);

        Assert.Equal((AgileTaskStatus.Done, 1), (result.Value!.Status, result.Value.Rank));
    }

    // ---------------------------------------------------------------- the board

    [Fact]
    public async Task TheBoard_HasFourColumnsInOrder_WithCardsAndPointsPerColumn()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("A", points: 3);
        await f.AddTaskAsync("B", points: 5);
        await f.AddTaskAsync("C", points: 8, status: AgileTaskStatus.UnderReview);
        await f.AddTaskAsync("D", status: AgileTaskStatus.Done);

        var board = (await f.Board.GetBoardAsync(f.ProjectId, null, null, null, default)).Value!;

        Assert.Equal(
            [AgileTaskStatus.ToDo, AgileTaskStatus.InProgress, AgileTaskStatus.UnderReview, AgileTaskStatus.Done],
            board.Columns.Select(c => c.Status));
        Assert.Equal([2, 0, 1, 1], board.Columns.Select(c => c.CardCount));
        Assert.Equal([8, 0, 8, 0], board.Columns.Select(c => c.Points));
        Assert.Equal(["A", "B"], board.Columns[0].Cards.Select(c => c.Title)); // by rank
        Assert.Null(board.SprintNumber);
    }

    [Fact]
    public async Task TheBoard_CanBeScopedToASprint_AndFilteredByAssigneeAndPriority()
    {
        var f = new AgileFixture();
        Guid ana = Guid.NewGuid(), bob = Guid.NewGuid();
        await f.AddTaskAsync("In sprint, Ana, High", sprint: 1, priority: AgileTaskPriority.High, responsible: ana);
        await f.AddTaskAsync("In sprint, Bob, Low", sprint: 1, priority: AgileTaskPriority.Low, responsible: bob);
        await f.AddTaskAsync("Backlog, Ana", priority: AgileTaskPriority.High, responsible: ana);

        async Task<string[]> Titles(int? sprint, Guid? who, AgileTaskPriority? priority) =>
            (await f.Board.GetBoardAsync(f.ProjectId, sprint, who, priority, default)).Value!
                .Columns.SelectMany(c => c.Cards).Select(c => c.Title).Order().ToArray();

        Assert.Equal(3, (await Titles(null, null, null)).Length);
        Assert.Equal(["In sprint, Ana, High", "In sprint, Bob, Low"], await Titles(1, null, null));
        Assert.Equal(["Backlog, Ana", "In sprint, Ana, High"], await Titles(null, ana, null));
        Assert.Equal(["In sprint, Bob, Low"], await Titles(null, null, AgileTaskPriority.Low));
        Assert.Equal(["In sprint, Ana, High"], await Titles(1, ana, AgileTaskPriority.High));
    }

    [Fact]
    public async Task Cards_ShowTheirChecklistProgress()
    {
        var f = new AgileFixture();
        var task = await f.AddTaskAsync("Card");
        var first = (await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("one"), default)).Value!;
        await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("two"), default);
        await f.ChecklistService.UpdateAsync(task.Id, first.Id, new UpdateChecklistItemRequest("one", true), default);

        var card = (await f.Board.GetBoardAsync(f.ProjectId, null, null, null, default)).Value!.Columns[0].Cards.Single();

        Assert.Equal((1, 2), (card.ChecklistDone, card.ChecklistTotal));
    }

    // ------------------------------------------------------------------ moving

    private static async Task<string[]> Column(AgileFixture f, AgileTaskStatus status) =>
        (await f.Board.GetBoardAsync(f.ProjectId, null, null, null, default)).Value!
            .Columns.Single(c => c.Status == status).Cards.Select(c => c.Title).ToArray();

    [Fact]
    public async Task Moving_WithinAColumn_ReordersAndRenumbers()
    {
        var f = new AgileFixture();
        var a = await f.AddTaskAsync("A");
        await f.AddTaskAsync("B");
        var c = await f.AddTaskAsync("C");

        var moved = await f.Board.MoveAsync(c.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, a.Id), default);

        Assert.True(moved.IsSuccess);
        Assert.Equal(["C", "A", "B"], await Column(f, AgileTaskStatus.ToDo));
        Assert.Equal([0, 1, 2], f.Tasks.Items.OrderBy(t => t.Rank).Select(t => t.Rank));
        Assert.Equal(0, moved.Value!.Rank);
    }

    [Fact]
    public async Task Moving_ToTheEndOfTheSameColumn_WithNoTarget()
    {
        var f = new AgileFixture();
        var a = await f.AddTaskAsync("A");
        await f.AddTaskAsync("B");

        await f.Board.MoveAsync(a.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, null), default);

        Assert.Equal(["B", "A"], await Column(f, AgileTaskStatus.ToDo));
    }

    [Fact]
    public async Task Moving_ToAnotherColumn_InsertsThereAndClosesTheGapBehind()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("A");
        var b = await f.AddTaskAsync("B");
        await f.AddTaskAsync("C");
        var x = await f.AddTaskAsync("X", status: AgileTaskStatus.InProgress);
        await f.AddTaskAsync("Y", status: AgileTaskStatus.InProgress);

        var moved = await f.Board.MoveAsync(b.Id, new MoveAgileTaskRequest(AgileTaskStatus.InProgress, x.Id), default);

        Assert.Equal((AgileTaskStatus.InProgress, 0), (moved.Value!.Status, moved.Value.Rank));
        Assert.Equal(["B", "X", "Y"], await Column(f, AgileTaskStatus.InProgress));
        Assert.Equal(["A", "C"], await Column(f, AgileTaskStatus.ToDo));
        Assert.Equal([0, 1], f.Tasks.Items.Where(t => t.Status == AgileTaskStatus.ToDo).OrderBy(t => t.Rank).Select(t => t.Rank));
        Assert.Equal([0, 1, 2], f.Tasks.Items.Where(t => t.Status == AgileTaskStatus.InProgress).OrderBy(t => t.Rank).Select(t => t.Rank));
    }

    [Fact]
    public async Task Moving_IntoAnEmptyColumn_PutsTheCardFirst()
    {
        var f = new AgileFixture();
        var a = await f.AddTaskAsync("A");

        var moved = await f.Board.MoveAsync(a.Id, new MoveAgileTaskRequest(AgileTaskStatus.UnderReview, null), default);

        Assert.Equal((AgileTaskStatus.UnderReview, 0), (moved.Value!.Status, moved.Value.Rank));
    }

    [Fact]
    public async Task Moving_RejectsATargetThatIsNotInTheDestinationColumn_AndUnknownTasksAndStatuses()
    {
        var f = new AgileFixture();
        var a = await f.AddTaskAsync("A");
        var inOtherColumn = await f.AddTaskAsync("B", status: AgileTaskStatus.Done);

        Assert.Equal("validation.error", (await f.Board.MoveAsync(a.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, inOtherColumn.Id), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Board.MoveAsync(a.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, Guid.NewGuid()), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Board.MoveAsync(a.Id, new MoveAgileTaskRequest((AgileTaskStatus)99, null), default)).Error.Code);
        Assert.Equal("not_found", (await f.Board.MoveAsync(Guid.NewGuid(), new MoveAgileTaskRequest(AgileTaskStatus.ToDo, null), default)).Error.Code);
        Assert.Equal(AgileTaskStatus.ToDo, f.Stored(a.Id).Status); // a rejected move changes nothing
    }

    [Fact]
    public async Task ADropOnItself_ChangesNothing()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("A");
        var b = await f.AddTaskAsync("B");

        var result = await f.Board.MoveAsync(b.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, b.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Rank);
        Assert.Equal(["A", "B"], await Column(f, AgileTaskStatus.ToDo));
    }

    // ----------------------------------------------------------------- backlog

    [Fact]
    public async Task TheBacklog_IsTheUnfinishedTasksInNoSprint_WithItsPointsAndUnestimatedCount()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("Estimated", points: 5);
        await f.AddTaskAsync("Not estimated");
        await f.AddTaskAsync("In a sprint", points: 8, sprint: 1);
        await f.AddTaskAsync("Done", points: 3, status: AgileTaskStatus.Done);

        var backlog = (await f.Board.GetBacklogAsync(f.ProjectId, default)).Value!;

        Assert.Equal(["Estimated", "Not estimated"], backlog.Cards.Select(c => c.Title));
        Assert.Equal((5, 1), (backlog.TotalPoints, backlog.UnestimatedCount));
    }

    [Fact]
    public async Task TheBacklog_CanBeReprioritisedByMoving()
    {
        var f = new AgileFixture();
        var a = await f.AddTaskAsync("A");
        await f.AddTaskAsync("B");
        var c = await f.AddTaskAsync("C");

        await f.Board.MoveAsync(c.Id, new MoveAgileTaskRequest(AgileTaskStatus.ToDo, a.Id), default);

        Assert.Equal(["C", "A", "B"], (await f.Board.GetBacklogAsync(f.ProjectId, default)).Value!.Cards.Select(x => x.Title));
    }

    // -------------------------------------------------------------- story points

    [Fact]
    public async Task StoryPoints_AreStoredOnCreate_KeptWhenAnUpdateOmitsThem_AndClearedOnlyExplicitly()
    {
        var f = new AgileFixture();
        var task = await f.AddTaskAsync("Sized", points: 5);
        Assert.Equal(5, task.StoryPoints);

        var renamed = await f.TaskService.UpdateAsync(task.Id,
            new UpdateAgileTaskRequest("Renamed", null, null, null, null, AgileTaskPriority.Medium, null), default);
        Assert.Equal(5, renamed.Value!.StoryPoints); // a client that predates the field must not wipe it

        var resized = await f.TaskService.UpdateAsync(task.Id,
            new UpdateAgileTaskRequest("Renamed", null, null, null, null, AgileTaskPriority.Medium, null, StoryPoints: 8), default);
        Assert.Equal(8, resized.Value!.StoryPoints);

        var zero = await f.TaskService.SetStoryPointsAsync(task.Id, new SetStoryPointsRequest(0), default);
        Assert.Equal(0, zero.Value!.StoryPoints);

        var cleared = await f.TaskService.SetStoryPointsAsync(task.Id, new SetStoryPointsRequest(null), default);
        Assert.Null(cleared.Value!.StoryPoints);
        Assert.Equal("not_found", (await f.TaskService.SetStoryPointsAsync(Guid.NewGuid(), new SetStoryPointsRequest(1), default)).Error.Code);
    }

    // ---------------------------------------------------------------- checklist

    [Fact]
    public async Task AChecklist_KeepsItsOrder_CanBeEditedAndRemoved_AndBelongsToItsTask()
    {
        var f = new AgileFixture();
        var task = await f.AddTaskAsync("T");
        var other = await f.AddTaskAsync("Other");
        var one = (await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("  one  "), default)).Value!;
        var two = (await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("two"), default)).Value!;

        Assert.Equal(("one", 0, 1), (one.Text, one.Order, two.Order));
        Assert.Equal(["one", "two"], (await f.ChecklistService.ListAsync(task.Id, default)).Value!.Select(i => i.Text));

        var done = await f.ChecklistService.UpdateAsync(task.Id, two.Id, new UpdateChecklistItemRequest("two!", true), default);
        Assert.Equal(("two!", true), (done.Value!.Text, done.Value.IsDone));

        // Another task's id cannot reach this task's items.
        Assert.Equal("not_found", (await f.ChecklistService.UpdateAsync(other.Id, two.Id, new UpdateChecklistItemRequest("x", false), default)).Error.Code);
        Assert.Equal("not_found", (await f.ChecklistService.DeleteAsync(other.Id, two.Id, default)).Error.Code);

        Assert.True((await f.ChecklistService.DeleteAsync(task.Id, one.Id, default)).IsSuccess);
        Assert.Single((await f.ChecklistService.ListAsync(task.Id, default)).Value!);
        Assert.Equal("not_found", (await f.ChecklistService.ListAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.Equal("validation.error", (await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("  "), default)).Error.Code);
    }

    [Fact]
    public async Task AChecklist_IsCapped_AndGoesWhenItsTaskIsDeleted()
    {
        var f = new AgileFixture();
        var task = await f.AddTaskAsync("T");
        for (var i = 0; i < AgileChecklistService.MaxItemsPerTask; i++)
        {
            Assert.True((await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest($"step {i}"), default)).IsSuccess);
        }

        Assert.Equal("conflict", (await f.ChecklistService.AddAsync(task.Id, new CreateChecklistItemRequest("one too many"), default)).Error.Code);

        Assert.True((await f.TaskService.DeleteAsync(task.Id, default)).IsSuccess);
        Assert.Empty(f.Checklist.Items);
    }
}
