using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class WaterfallDependencyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    private sealed record Fixture(
        ActivityService Activities, ActivityDependencyService Dependencies,
        FakeActivityRepository ActivityRepository, FakeDependencyRepository DependencyRepository);

    private static Fixture Build()
    {
        var activityRepository = new FakeActivityRepository();
        var dependencyRepository = new FakeDependencyRepository();
        var unitOfWork = new FakeWaterfallUnitOfWork();
        // Create/Update/Delete never reach the platform (audit) service, which only SubmitForApproval uses.
        var activities = new ActivityService(activityRepository, dependencyRepository, unitOfWork, new NotConfiguredApprovalRequester(), platformService: null!);
        var dependencies = new ActivityDependencyService(dependencyRepository, activityRepository, unitOfWork);
        return new Fixture(activities, dependencies, activityRepository, dependencyRepository);
    }

    private static CreateActivityRequest NewActivity(string name, Guid? parent = null, Guid? projectId = null, bool? milestone = null,
        DateOnly? start = null, DateOnly? end = null, int? duration = null) =>
        new(Tenant, projectId ?? Project, parent, name, null, null, null, null, start, end, duration, null, 0, milestone);

    private static async Task<Guid> AddAsync(Fixture fixture, string name, Guid? parent = null, bool? milestone = null)
    {
        var created = await fixture.Activities.CreateAsync(NewActivity(name, parent, milestone: milestone), default);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
        return created.Value!.Id;
    }

    private static CreateActivityDependencyRequest Link(Guid predecessor, Guid successor,
        DependencyType type = DependencyType.FinishToStart, int lag = 0, Guid? projectId = null) =>
        new(Tenant, projectId ?? Project, predecessor, successor, type, lag);

    private static UpdateActivityRequest Update(string name, Guid? parent, bool? milestone = null,
        DateOnly? start = null, DateOnly? end = null, int? duration = null) =>
        new(parent, name, null, null, null, null, start, end, duration, null, 0, milestone);

    // ------------------------------------------------------- dependencies

    [Fact]
    public async Task ALink_StoresItsTypeAndLag_AndCanBeEditedAndRemoved()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var b = await AddAsync(fixture, "B");

        var created = await fixture.Dependencies.CreateAsync(Link(a, b, DependencyType.StartToStart, lag: 2), default);
        Assert.True(created.IsSuccess);
        Assert.Equal((DependencyType.StartToStart, 2), (created.Value!.Type, created.Value.LagDays));

        var updated = await fixture.Dependencies.UpdateAsync(created.Value.Id, new UpdateActivityDependencyRequest(DependencyType.FinishToFinish, -1), default);
        Assert.Equal((DependencyType.FinishToFinish, -1), (updated.Value!.Type, updated.Value.LagDays));

        Assert.Single((await fixture.Dependencies.ListByProjectAsync(Project, default)).Value!);
        Assert.True((await fixture.Dependencies.DeleteAsync(created.Value.Id, default)).IsSuccess);
        Assert.Empty((await fixture.Dependencies.ListByProjectAsync(Project, default)).Value!);
        Assert.Equal("not_found", (await fixture.Dependencies.DeleteAsync(created.Value.Id, default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Dependencies.UpdateAsync(Guid.NewGuid(), new UpdateActivityDependencyRequest(DependencyType.FinishToStart, 0), default)).Error.Code);
    }

    [Fact]
    public async Task ALink_DefaultsToFinishToStartWithNoLag()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var b = await AddAsync(fixture, "B");

        var created = await fixture.Dependencies.CreateAsync(new CreateActivityDependencyRequest(Tenant, Project, a, b), default);

        Assert.Equal((DependencyType.FinishToStart, 0), (created.Value!.Type, created.Value.LagDays));
    }

    [Fact]
    public async Task ALink_RejectsSelfLinks_UnknownActivities_AndOtherProjectsActivities()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var elsewhere = (await fixture.Activities.CreateAsync(NewActivity("Z", projectId: Guid.NewGuid()), default)).Value!.Id;

        Assert.Equal("validation.error", (await fixture.Dependencies.CreateAsync(Link(a, a), default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Dependencies.CreateAsync(Link(a, Guid.NewGuid()), default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Dependencies.CreateAsync(Link(a, elsewhere), default)).Error.Code);
        Assert.Empty(fixture.DependencyRepository.Items);
    }

    [Fact]
    public async Task ALink_RejectsSummaryActivities_OnEitherEnd()
    {
        var fixture = Build();
        var phase = await AddAsync(fixture, "Phase");
        await AddAsync(fixture, "Task", parent: phase);
        var other = await AddAsync(fixture, "Other");

        Assert.Equal("validation.error", (await fixture.Dependencies.CreateAsync(Link(phase, other), default)).Error.Code);
        Assert.Equal("validation.error", (await fixture.Dependencies.CreateAsync(Link(other, phase), default)).Error.Code);
    }

    [Fact]
    public async Task ALink_RejectsDuplicatePairs_AndCycles()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var b = await AddAsync(fixture, "B");
        var c = await AddAsync(fixture, "C");
        Assert.True((await fixture.Dependencies.CreateAsync(Link(a, b), default)).IsSuccess);
        Assert.True((await fixture.Dependencies.CreateAsync(Link(b, c), default)).IsSuccess);

        Assert.Equal("conflict", (await fixture.Dependencies.CreateAsync(Link(a, b, DependencyType.StartToStart), default)).Error.Code);
        Assert.Equal("conflict", (await fixture.Dependencies.CreateAsync(Link(b, a), default)).Error.Code); // direct loop
        Assert.Equal("conflict", (await fixture.Dependencies.CreateAsync(Link(c, a), default)).Error.Code); // loop through B
        Assert.Equal(2, fixture.DependencyRepository.Items.Count);

        // Diamonds are not cycles.
        var d = await AddAsync(fixture, "D");
        Assert.True((await fixture.Dependencies.CreateAsync(Link(a, d), default)).IsSuccess);
        Assert.True((await fixture.Dependencies.CreateAsync(Link(d, c), default)).IsSuccess);
    }

    [Fact]
    public async Task DeletingAnActivity_RemovesItsLinks_AndOnlyItsLinks()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var b = await AddAsync(fixture, "B");
        var c = await AddAsync(fixture, "C");
        await fixture.Dependencies.CreateAsync(Link(a, b), default);
        await fixture.Dependencies.CreateAsync(Link(b, c), default);
        await fixture.Dependencies.CreateAsync(Link(a, c), default);

        Assert.True((await fixture.Activities.DeleteAsync(b, default)).IsSuccess);

        var left = Assert.Single(fixture.DependencyRepository.Items);
        Assert.Equal((a, c), (left.PredecessorActivityId, left.SuccessorActivityId));
    }

    [Fact]
    public void WouldCreateCycle_FindsLoopsOfAnyLength_AndIgnoresUnrelatedLinks()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var links = new[] { (a, b), (b, c) };

        Assert.True(DependencyGraph.WouldCreateCycle(links, a, a));
        Assert.True(DependencyGraph.WouldCreateCycle(links, c, a));
        Assert.True(DependencyGraph.WouldCreateCycle(links, b, a));
        Assert.False(DependencyGraph.WouldCreateCycle(links, a, c));
        Assert.False(DependencyGraph.WouldCreateCycle(links, d, a));
        Assert.False(DependencyGraph.WouldCreateCycle(links, c, d));
    }

    [Fact]
    public void TopologicalOrder_PutsPredecessorsFirst_AndReportsCyclesAsNull()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();

        var order = DependencyGraph.TopologicalOrder([d, c, b, a], [(a, b), (b, c), (a, c)])!.ToList();
        Assert.True(order.IndexOf(a) < order.IndexOf(b));
        Assert.True(order.IndexOf(b) < order.IndexOf(c));
        Assert.Equal(4, order.Count);

        Assert.Null(DependencyGraph.TopologicalOrder([a, b], [(a, b), (b, a)]));
        // A link to a node that is not in the set is ignored rather than failing.
        Assert.Equal(2, DependencyGraph.TopologicalOrder([a, b], [(a, b), (a, Guid.NewGuid())])!.Count);
    }

    // ---------------------------------------------------------- milestones

    [Fact]
    public async Task AMilestone_HasZeroDuration_AndItsEndOnItsStart()
    {
        var fixture = Build();

        var created = await fixture.Activities.CreateAsync(
            NewActivity("Hand-over", milestone: true, start: new DateOnly(2026, 3, 10), end: new DateOnly(2026, 3, 20), duration: 7), default);

        Assert.True(created.Value!.IsMilestone);
        Assert.Equal(0, created.Value.DurationDays);
        Assert.Equal(new DateOnly(2026, 3, 10), created.Value.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 10), created.Value.EndDate);
    }

    [Fact]
    public async Task AMilestone_StaysAMilestone_WhenUpdatedWithoutSayingSo()
    {
        var fixture = Build();
        var id = await AddAsync(fixture, "Gate", milestone: true);

        var updated = await fixture.Activities.UpdateAsync(id,
            Update("Gate renamed", null, milestone: null, start: new DateOnly(2026, 4, 1), end: new DateOnly(2026, 4, 9), duration: 5), default);

        Assert.True(updated.Value!.IsMilestone);
        Assert.Equal(0, updated.Value.DurationDays);
        Assert.Equal(updated.Value.StartDate, updated.Value.EndDate);

        var cleared = await fixture.Activities.UpdateAsync(id, Update("Gate renamed", null, milestone: false, duration: 3), default);
        Assert.False(cleared.Value!.IsMilestone);
        Assert.Equal(3, cleared.Value.DurationDays);
    }

    [Fact]
    public async Task OrdinaryActivities_AreUntouchedByTheMilestoneFlag()
    {
        var fixture = Build();
        var created = await fixture.Activities.CreateAsync(NewActivity("Dig", duration: 5, start: new DateOnly(2026, 3, 1), end: new DateOnly(2026, 3, 5)), default);

        Assert.False(created.Value!.IsMilestone);
        Assert.Equal(5, created.Value.DurationDays);

        var updated = await fixture.Activities.UpdateAsync(created.Value.Id, Update("Dig", null, duration: 6), default);
        Assert.False(updated.Value!.IsMilestone);
        Assert.Equal(6, updated.Value.DurationDays);
    }

    [Fact]
    public async Task AMilestone_CannotHaveSubActivities_AndAParentCannotBecomeOne()
    {
        var fixture = Build();
        var gate = await AddAsync(fixture, "Gate", milestone: true);
        var phase = await AddAsync(fixture, "Phase");
        await AddAsync(fixture, "Task", parent: phase);

        var underMilestone = await fixture.Activities.CreateAsync(NewActivity("Child", parent: gate), default);
        Assert.Equal("validation.error", underMilestone.Error.Code);

        var phaseAsMilestone = await fixture.Activities.UpdateAsync(phase, Update("Phase", null, milestone: true), default);
        Assert.Equal("validation.error", phaseAsMilestone.Error.Code);
        Assert.False(fixture.ActivityRepository.Items.Single(a => a.Id == phase).IsMilestone);
    }

    // -------------------------------------------------------------- parents

    [Fact]
    public async Task AParent_MustExistInTheSameProject()
    {
        var fixture = Build();
        var elsewhere = (await fixture.Activities.CreateAsync(NewActivity("Z", projectId: Guid.NewGuid()), default)).Value!.Id;

        Assert.Equal("validation.error", (await fixture.Activities.CreateAsync(NewActivity("Child", parent: Guid.NewGuid()), default)).Error.Code);
        Assert.Equal("validation.error", (await fixture.Activities.CreateAsync(NewActivity("Child", parent: elsewhere), default)).Error.Code);
    }

    [Fact]
    public async Task AnActivity_CannotBeMovedUnderItself_OrUnderItsOwnDescendants()
    {
        var fixture = Build();
        var root = await AddAsync(fixture, "Root");
        var child = await AddAsync(fixture, "Child", parent: root);
        var grandchild = await AddAsync(fixture, "Grandchild", parent: child);

        Assert.Equal("conflict", (await fixture.Activities.UpdateAsync(root, Update("Root", grandchild), default)).Error.Code);
        Assert.Equal("conflict", (await fixture.Activities.UpdateAsync(root, Update("Root", child), default)).Error.Code);
        Assert.Equal("conflict", (await fixture.Activities.UpdateAsync(child, Update("Child", grandchild), default)).Error.Code);
        Assert.Null(fixture.ActivityRepository.Items.Single(a => a.Id == root).ParentActivityId);

        // Moving to an unrelated place is fine.
        var other = await AddAsync(fixture, "Other");
        Assert.True((await fixture.Activities.UpdateAsync(grandchild, Update("Grandchild", other), default)).IsSuccess);
    }

    [Fact]
    public async Task AnActivityWithLinks_CannotBecomeAParent_ButItsChildrenCanStillBeEdited()
    {
        var fixture = Build();
        var a = await AddAsync(fixture, "A");
        var b = await AddAsync(fixture, "B");
        var movable = await AddAsync(fixture, "Movable");
        await fixture.Dependencies.CreateAsync(Link(a, b), default);

        Assert.Equal("conflict", (await fixture.Activities.UpdateAsync(movable, Update("Movable", a), default)).Error.Code);
        Assert.Equal("conflict", (await fixture.Activities.CreateAsync(NewActivity("Child", parent: b), default)).Error.Code);

        // Editing an activity without changing its parent never re-runs the parent rules.
        Assert.True((await fixture.Activities.UpdateAsync(movable, Update("Movable renamed", null), default)).IsSuccess);
    }
}
