using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.EventHandlers;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;
using Nexus.ProjectManagement.Waterfall.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class WaterfallPlanningTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private sealed class FakeSettingsRepository : IWaterfallSettingsRepository
    {
        public List<WaterfallProjectSettings> Items { get; } = [];
        public Task<WaterfallProjectSettings?> GetAsync(Guid projectId, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.ProjectId == projectId));
        public Task<IReadOnlyList<WaterfallProjectSettings>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WaterfallProjectSettings>>(Items.Where(s => projectIds.Contains(s.ProjectId)).ToList());
        public Task AddAsync(WaterfallProjectSettings settings, CancellationToken ct) { Items.Add(settings); return Task.CompletedTask; }
    }

    private sealed record Fixture(
        ScheduleService Schedule, WaterfallSettingsService Settings, FakeSettingsRepository SettingsRepository,
        FakeActivityRepository Activities, FakeDependencyRepository Dependencies, Project Project);

    private static Fixture Build(WaterfallOptions? options = null, DateTimeOffset? now = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, "Tower", "TWR", ProjectType.Waterfall);
        project.UpdateDetails("Tower", "TWR", null, null, null, null, Monday, null, null, null, null, null, null, null, null);
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var settings = new FakeSettingsRepository();
        var unitOfWork = new FakeWaterfallUnitOfWork();
        var schedule = new ScheduleService(
            activities, dependencies, new FakeProjectRepository(project), new FakeCalendarProvider(null), unitOfWork,
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero)), settings, options);
        return new Fixture(schedule, new WaterfallSettingsService(settings, unitOfWork, options), settings, activities, dependencies, project);
    }

    private static Activity Add(Fixture f, string name, int days, decimal weight = 0, decimal? cost = null, ActivityLevel? complexity = null,
        Guid? parent = null, int position = 0, decimal actual = 0)
    {
        var activity = new Activity(Guid.NewGuid(), Tenant, f.Project.Id, name, parent);
        activity.UpdateDetails(name, null, parent, null, null, null, null, null, days, 2m, weight);
        activity.SetEstimates(cost, null, complexity);
        activity.SetPosition(position);
        if (actual > 0)
        {
            activity.UpdateProgress(0, actual);
        }

        f.Activities.Items.Add(activity);
        return activity;
    }

    private static async Task SaveAsync(Fixture f, WeightingMode weighting, int time = 34, int cost = 33, int complexity = 33,
        PlannedProgressMode planned = PlannedProgressMode.Stored)
    {
        var saved = await f.Settings.SaveAsync(new SaveWaterfallSettingsRequest(Tenant, f.Project.Id, weighting, time, cost, complexity, planned), default);
        Assert.True(saved.IsSuccess, saved.Error.Message);
    }

    // ------------------------------------------------------------ the pure weighting rule

    [Fact]
    public void Weighting_ByTime_GivesEachLeafItsShareOfTheTotalDuration()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var weights = ActivityWeighting.Compute([new(a, 3, 0, ActivityLevel.Medium), new(b, 1, 0, ActivityLevel.Medium)], WeightingMode.Time, 0, 0, 0);

        Assert.Equal((75m, 25m), (weights[a], weights[b]));
    }

    [Fact]
    public void Weighting_ByComplexity_UsesOneTwoThree()
    {
        var low = Guid.NewGuid();
        var high = Guid.NewGuid();

        var weights = ActivityWeighting.Compute([new(low, 1, 0, ActivityLevel.Low), new(high, 1, 0, ActivityLevel.High)], WeightingMode.Complexity, 0, 0, 0);

        Assert.Equal((25m, 75m), (weights[low], weights[high]));
    }

    [Fact]
    public void Weighting_Combined_BlendsTheThreeSharesByTheFactors_AndSumsToOneHundred()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var leaves = new WeightingInput[] { new(a, 3, 100, ActivityLevel.Low), new(b, 1, 300, ActivityLevel.High) };

        var weights = ActivityWeighting.Compute(leaves, WeightingMode.Combined, 50, 30, 20);

        // time 75/25, cost 25/75, complexity 25/75
        Assert.Equal(50m * 0.75m / 50 * 0.5m * 100 / 100 * 1m * 0 + (75m * 50 + 25m * 30 + 25m * 20) / 100m, weights[a]);
        Assert.Equal(100m, weights.Values.Sum());
    }

    [Fact]
    public void Weighting_Manual_LeavesEverythingToTheTypedWeights()
    {
        var a = Guid.NewGuid();

        Assert.Equal(0m, ActivityWeighting.Compute([new(a, 3, 0, ActivityLevel.Medium)], WeightingMode.Manual, 0, 0, 0)[a]);
    }

    // ------------------------------------------------------------ settings

    [Fact]
    public async Task Settings_DefaultToTheOriginalBehaviour_UnlessTheHostChoseOtherwise()
    {
        var plain = Build();
        var configured = Build(new WaterfallOptions { DefaultPlannedProgress = PlannedProgressMode.Continuous });

        var a = (await plain.Settings.GetAsync(plain.Project.Id, default)).Value!;
        var b = (await configured.Settings.GetAsync(configured.Project.Id, default)).Value!;

        Assert.True(a.IsDefault);
        Assert.Equal((WeightingMode.Manual, PlannedProgressMode.Stored), (a.Weighting, a.PlannedProgress));
        Assert.Equal(PlannedProgressMode.Continuous, b.PlannedProgress);
    }

    [Fact]
    public async Task Settings_CombinedNeedsFactorsThatAddUpToOneHundred()
    {
        var f = Build();

        var wrong = await f.Settings.SaveAsync(new SaveWaterfallSettingsRequest(Tenant, f.Project.Id, WeightingMode.Combined, 50, 30, 30, PlannedProgressMode.Stored), default);
        var outOfRange = await f.Settings.SaveAsync(new SaveWaterfallSettingsRequest(Tenant, f.Project.Id, WeightingMode.Time, 120, 0, 0, PlannedProgressMode.Stored), default);
        var fine = await f.Settings.SaveAsync(new SaveWaterfallSettingsRequest(Tenant, f.Project.Id, WeightingMode.Manual, 0, 0, 0, PlannedProgressMode.ZeroHundred), default);

        Assert.Equal("validation.error", wrong.Error.Code);
        Assert.Equal("validation.error", outOfRange.Error.Code);
        Assert.True(fine.IsSuccess);
        Assert.False(fine.Value!.IsDefault);
        Assert.Single(f.SettingsRepository.Items); // saved again later updates the same row
        await SaveAsync(f, WeightingMode.Time);
        Assert.Single(f.SettingsRepository.Items);
    }

    // ------------------------------------------------------------ the schedule applies them

    [Fact]
    public async Task WithoutSettingsOrOptions_TheScheduleIsAsItAlwaysWas()
    {
        var f = Build();
        var phase = Add(f, "Phase", 0);
        var a = Add(f, "A", 2, weight: 10, parent: phase.Id, actual: 100);
        Add(f, "B", 2, weight: 30, parent: phase.Id);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;

        Assert.Equal(WeightingMode.Manual, schedule.Weighting);
        Assert.Equal(25m, schedule.ActualProgress); // 10 of 40
        Assert.Equal(0m, schedule.Activities.Single(x => x.Id == a.Id).PlannedProgress); // stored value, never set
    }

    [Fact]
    public async Task CostBasedWeighting_WeighsLeavesByCost_AndRollsGroupsUp()
    {
        var f = Build();
        var phase = Add(f, "Phase", 0);
        var cheap = Add(f, "Cheap", 1, cost: 100, parent: phase.Id, actual: 100);
        var dear = Add(f, "Dear", 1, cost: 300, parent: phase.Id);
        await SaveAsync(f, WeightingMode.Cost);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;

        Assert.Equal(25m, schedule.Activities.Single(x => x.Id == cheap.Id).Weight);
        Assert.Equal(75m, schedule.Activities.Single(x => x.Id == dear.Id).Weight);
        Assert.Equal(100m, schedule.Activities.Single(x => x.Id == phase.Id).Weight); // a group is worth what is inside it
        Assert.Equal(400m, schedule.Activities.Single(x => x.Id == phase.Id).Cost);
        Assert.Equal(25m, schedule.ActualProgress);
        Assert.Equal(25m, schedule.Activities.Single(x => x.Id == phase.Id).ActualProgress);
    }

    [Fact]
    public async Task TimeBasedWeighting_UsesTheCalculatedDurations()
    {
        var f = Build();
        var long1 = Add(f, "Long", 3, actual: 100);
        Add(f, "Short", 1);
        await SaveAsync(f, WeightingMode.Time);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;

        Assert.Equal(75m, schedule.ActualProgress);
        Assert.Equal(75m, schedule.Activities.Single(x => x.Id == long1.Id).Weight);
    }

    [Fact]
    public async Task ContinuousPlannedProgress_FollowsTheWorkingDaysGoneBy()
    {
        // Monday 2 Mar start, two days; "now" is Wednesday 4 Mar noon: Monday and Tuesday are over.
        var f = Build();
        var a = Add(f, "A", 2);
        await SaveAsync(f, WeightingMode.Manual, planned: PlannedProgressMode.Continuous);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;

        Assert.Equal(100m, schedule.Activities.Single(x => x.Id == a.Id).PlannedProgress);
        Assert.Equal(100m, schedule.PlannedProgress);
    }

    [Fact]
    public async Task ZeroHundredPlannedProgress_IsZeroUntilTheLastDayIsOver()
    {
        // Four days from Monday (no calendar: every day works): Mon-Wed are over on Wednesday -> 0; one more day later -> 100.
        var early = Build(now: new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero));
        var late = Build(now: new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero));
        foreach (var f in new[] { early, late })
        {
            Add(f, "A", 4);
            await SaveAsync(f, WeightingMode.Manual, planned: PlannedProgressMode.ZeroHundred);
        }

        Assert.Equal(0m, (await early.Schedule.GetScheduleAsync(early.Project.Id, default)).Value!.PlannedProgress);
        Assert.Equal(100m, (await late.Schedule.GetScheduleAsync(late.Project.Id, default)).Value!.PlannedProgress);
    }

    [Fact]
    public async Task HostDefault_AppliesToProjectsWithoutSettings()
    {
        var f = Build(new WaterfallOptions { DefaultPlannedProgress = PlannedProgressMode.Continuous, RollUpSummaryWeights = true });
        var a = Add(f, "A", 2);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;

        Assert.Equal(PlannedProgressMode.Continuous, schedule.PlannedProgressMode);
        Assert.Equal(100m, schedule.Activities.Single(x => x.Id == a.Id).PlannedProgress);
    }

    [Fact]
    public async Task WbsCodes_FollowPlacementOrder_NotNameOrStartOrder()
    {
        var f = Build();
        var second = Add(f, "Alpha", 1, position: 2);
        var first = Add(f, "Zulu", 1, position: 1);
        var child2 = Add(f, "Child B", 1, parent: first.Id, position: 4);
        var child1 = Add(f, "Child A", 1, parent: first.Id, position: 3);

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;
        string Code(Activity a) => schedule.Activities.Single(x => x.Id == a.Id).WbsCode;
        int Level(Activity a) => schedule.Activities.Single(x => x.Id == a.Id).Level;

        Assert.Equal(["1", "1.1", "1.2", "2"], new[] { first, child1, child2, second }.Select(Code));
        Assert.Equal([1, 2, 2, 1], new[] { first, child1, child2, second }.Select(Level));
    }

    [Fact]
    public async Task FreeFloat_IsHowFarAnActivityCanSlipBeforeItDelaysASuccessor()
    {
        // A(2) -> B(1) runs beside C(4): B finishes a day before the project does; A can slip only if B can... but B follows A at once.
        var f = Build();
        var a = Add(f, "A", 2, position: 1);
        var b = Add(f, "B", 1, position: 2);
        Add(f, "C", 4, position: 3);
        f.Dependencies.Items.Add(new ActivityDependency(Guid.NewGuid(), Tenant, f.Project.Id, a.Id, b.Id, DependencyType.FinishToStart, 0));

        var schedule = (await f.Schedule.GetScheduleAsync(f.Project.Id, default)).Value!;
        ScheduledActivityDto Of(Activity x) => schedule.Activities.Single(s => s.Id == x.Id);

        Assert.Equal((1, 0), (Of(a).TotalFloatDays, Of(a).FreeFloatDays)); // slipping A delays B at once
        Assert.Equal((1, 1), (Of(b).TotalFloatDays, Of(b).FreeFloatDays)); // nothing follows B
    }

    [Fact]
    public async Task Plan_BundlesSettingsActivitiesDependenciesAndTheSchedule()
    {
        var f = Build();
        var a = Add(f, "A", 2, position: 1);
        var b = Add(f, "B", 1, position: 2);
        f.Dependencies.Items.Add(new ActivityDependency(Guid.NewGuid(), Tenant, f.Project.Id, a.Id, b.Id, DependencyType.FinishToStart, 0));

        var plan = (await f.Schedule.GetPlanAsync(f.Project.Id, default)).Value!;
        var plans = (await f.Schedule.GetPlansAsync([f.Project.Id, Guid.NewGuid()], default)).Value!;

        Assert.Equal(["A", "B"], plan.Activities.Select(x => x.Name));
        Assert.Single(plan.Dependencies);
        Assert.Equal(2, plan.Schedule.Activities.Count);
        Assert.True(plan.Settings.IsDefault);
        Assert.Single(plans); // the unknown project is left out
    }

    // ------------------------------------------------------------ the activity and dependency services

    private static (ActivityService Service, FakeActivityRepository Activities, FakeDependencyRepository Dependencies) BuildActivityService(Guid projectId)
    {
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var service = new ActivityService(activities, dependencies, new FakeWaterfallUnitOfWork(), new NotConfiguredApprovalRequester(), platformService: null!);
        return (service, activities, dependencies);
    }

    [Fact]
    public async Task NewActivities_GoAfterTheOnesThatAreThere_AndKeepTheirEstimates()
    {
        var projectId = Guid.NewGuid();
        var (service, activities, _) = BuildActivityService(projectId);

        var first = (await service.CreateAsync(new CreateActivityRequest(Tenant, projectId, null, "One", null, null, null, null, null, null, 2, null, 0,
            Cost: 500m, Importance: ActivityLevel.High), default)).Value!;
        var second = (await service.CreateAsync(new CreateActivityRequest(Tenant, projectId, null, "Two", null, null, null, null, null, null, 2, null, 0), default)).Value!;

        Assert.Equal((500m, ActivityLevel.High, ActivityLevel.Medium), (first.Cost, first.Importance, first.Complexity));
        Assert.Equal((null, ActivityLevel.Medium, ActivityLevel.Medium), (second.Cost, second.Importance, second.Complexity));
        Assert.True(activities.Items.Single(a => a.Id == second.Id).Position > activities.Items.Single(a => a.Id == first.Id).Position);

        // An update that does not mention the estimates leaves them alone.
        var updated = (await service.UpdateAsync(first.Id, new UpdateActivityRequest(null, "One!", null, null, null, null, null, null, 3, null, 0), default)).Value!;
        Assert.Equal((500m, ActivityLevel.High), (updated.Cost, updated.Importance));
    }

    [Fact]
    public async Task DeleteWithSubActivities_RemovesTheWholeBranchAndItsLinks()
    {
        var projectId = Guid.NewGuid();
        var (service, activities, dependencies) = BuildActivityService(projectId);
        var phase = new Activity(Guid.NewGuid(), Tenant, projectId, "Phase");
        var child = new Activity(Guid.NewGuid(), Tenant, projectId, "Child", phase.Id);
        var grandChild = new Activity(Guid.NewGuid(), Tenant, projectId, "Grand", child.Id);
        var other = new Activity(Guid.NewGuid(), Tenant, projectId, "Other");
        activities.Items.AddRange([phase, child, grandChild, other]);
        dependencies.Items.Add(new ActivityDependency(Guid.NewGuid(), Tenant, projectId, grandChild.Id, other.Id, DependencyType.FinishToStart, 0));

        var plain = await service.DeleteAsync(phase.Id, default);
        var cascade = await service.DeleteWithSubActivitiesAsync(phase.Id, default);

        Assert.Equal("conflict", plain.Error.Code);
        Assert.True(cascade.IsSuccess);
        Assert.Equal([other.Id], activities.Items.Select(a => a.Id));
        Assert.Empty(dependencies.Items);
    }

    [Fact]
    public async Task SetPredecessor_ReplacesTheLinksIntoAnActivity_AndRefusesCycles()
    {
        var projectId = Guid.NewGuid();
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var service = new ActivityDependencyService(dependencies, activities, new FakeWaterfallUnitOfWork());
        var a = new Activity(Guid.NewGuid(), Tenant, projectId, "A");
        var b = new Activity(Guid.NewGuid(), Tenant, projectId, "B");
        var c = new Activity(Guid.NewGuid(), Tenant, projectId, "C");
        activities.Items.AddRange([a, b, c]);

        var first = await service.SetPredecessorAsync(new SetActivityPredecessorRequest(Tenant, projectId, c.Id, a.Id), default);
        var moved = await service.SetPredecessorAsync(new SetActivityPredecessorRequest(Tenant, projectId, c.Id, b.Id, DependencyType.StartToStart, 2), default);
        var cycle = await service.SetPredecessorAsync(new SetActivityPredecessorRequest(Tenant, projectId, b.Id, c.Id), default);
        var self = await service.SetPredecessorAsync(new SetActivityPredecessorRequest(Tenant, projectId, c.Id, c.Id), default);

        Assert.Single(first.Value!);
        var link = Assert.Single(moved.Value!);
        Assert.Equal((b.Id, DependencyType.StartToStart, 2), (link.PredecessorActivityId, link.Type, link.LagDays));
        Assert.Single(dependencies.Items);
        Assert.Equal("conflict", cycle.Error.Code);
        Assert.Equal("validation.error", self.Error.Code);

        var cleared = await service.SetPredecessorAsync(new SetActivityPredecessorRequest(Tenant, projectId, c.Id, null), default);
        Assert.Empty(cleared.Value!);
        Assert.Empty(dependencies.Items);
    }


    // ------------------------------------------------------------ the optional "weights must total 100" rule

    [Fact]
    public async Task SubmissionCheck_NeedsManualWeightsToAddUpToOneHundred_OnlyWhenTheHostAsksForIt()
    {
        var f = Build(new WaterfallOptions { RequireWeightsTotalOnSubmit = true });
        Add(f, "A", 2, weight: 60);
        Add(f, "B", 2, weight: 30);
        var strict = new ScheduleWeightsSubmissionCheck(f.Schedule, f.SettingsRepository, new WaterfallOptions { RequireWeightsTotalOnSubmit = true });
        var relaxed = new ScheduleWeightsSubmissionCheck(f.Schedule, f.SettingsRepository, new WaterfallOptions());

        var objection = await strict.CheckAsync(f.Project, default);
        Assert.Equal("project.schedule_weights_not_100", objection!.Code);
        Assert.Contains("90", objection.Message);
        Assert.Null(await relaxed.CheckAsync(f.Project, default)); // the original behaviour

        f.Activities.Items.Single(a => a.Name == "B").UpdateDetails("B", null, null, null, null, null, null, null, 2, 2m, 40);
        Assert.Null(await strict.CheckAsync(f.Project, default));

        // A project that weighs by something else always adds up by construction.
        f.Activities.Items.Single(a => a.Name == "B").UpdateDetails("B", null, null, null, null, null, null, null, 2, 2m, 5);
        await SaveAsync(f, WeightingMode.Time);
        Assert.Null(await strict.CheckAsync(f.Project, default));
    }

    [Fact]
    public async Task ProjectService_RefusesToSendAProjectWhenACheckObjects_AndSendsItOtherwise()
    {
        var dbOptions = new DbContextOptionsBuilder<Nexus.ProjectManagement.Core.Infrastructure.ProjectManagementCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new Nexus.ProjectManagement.Core.Infrastructure.ProjectManagementCoreDbContext(dbOptions);
        var project = new Project(Guid.NewGuid(), Tenant, "P", "P1", ProjectType.Waterfall);
        db.Add(project);
        await db.SaveChangesAsync();

        var objecting = new Nexus.ProjectManagement.Core.Application.ProjectService(
            new Nexus.ProjectManagement.Core.Infrastructure.ProjectRepository(db), db, new NotConfiguredApprovalRequester(), null, null, [new Objecting()]);
        var plain = new Nexus.ProjectManagement.Core.Application.ProjectService(
            new Nexus.ProjectManagement.Core.Infrastructure.ProjectRepository(db), db, new NotConfiguredApprovalRequester());

        var refused = await objecting.SubmitForApprovalAsync(project.Id, default);
        Assert.Equal("project.no", refused.Error.Code);
        Assert.NotEqual(ApprovalStatus.Approved, project.ApprovalStatus);

        Assert.True((await plain.SubmitForApprovalAsync(project.Id, default)).IsSuccess);
    }

    private sealed class Objecting : Nexus.ProjectManagement.Core.Application.IProjectSubmissionCheck
    {
        public Task<NexusCore.SharedKernel.Results.Error?> CheckAsync(Project project, CancellationToken ct) =>
            Task.FromResult<NexusCore.SharedKernel.Results.Error?>(new NexusCore.SharedKernel.Results.Error("project.no", "not yet"));
    }

    // ------------------------------------------------------------ progress reports

    private sealed class FakeSubmissionRepository : IActivityProgressSubmissionRepository
    {
        public List<ActivityProgressSubmission> Items { get; } = [];
        public Task<ActivityProgressSubmission?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<ActivityProgressSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActivityProgressSubmission>>(Items.Where(s => projectIds.Contains(s.ProjectId)).ToList());
        public Task<IReadOnlyList<ActivityProgressSubmission>> ListByActivityAsync(Guid activityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActivityProgressSubmission>>(Items.Where(s => s.ActivityId == activityId).ToList());
        public Task AddAsync(ActivityProgressSubmission submission, CancellationToken ct) { Items.Add(submission); return Task.CompletedTask; }
    }

    private sealed class SubmittingRequester : IApprovalRequester
    {
        public ApprovalSubject? Subject { get; private set; }
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct)
        {
            Subject = subject;
            return Task.FromResult(ApprovalRequestOutcome.Submitted);
        }
    }

    private static (ActivityProgressSubmissionService Service, FakeSubmissionRepository Submissions, FakeActivityRepository Activities, Activity Leaf, Activity Group, Guid ProjectId)
        BuildSubmissions(IApprovalRequester requester)
    {
        var projectId = Guid.NewGuid();
        var activities = new FakeActivityRepository();
        var group = new Activity(Guid.NewGuid(), Tenant, projectId, "Group");
        var leaf = new Activity(Guid.NewGuid(), Tenant, projectId, "Leaf", group.Id);
        activities.Items.AddRange([group, leaf]);
        var submissions = new FakeSubmissionRepository();
        var service = new ActivityProgressSubmissionService(submissions, activities, new FakeWaterfallUnitOfWork(), requester);
        return (service, submissions, activities, leaf, group, projectId);
    }

    [Fact]
    public async Task ProgressReport_WithoutAnApprovalBackend_IsConfirmedAtOnce()
    {
        var (service, _, _, leaf, _, projectId) = BuildSubmissions(new NotConfiguredApprovalRequester());

        var submitted = await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 40, "good week", "report.pdf"), default);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(ApprovalStatus.Approved, submitted.Value!.ApprovalStatus);
        Assert.Equal(40m, leaf.ActualProgress);
    }

    [Fact]
    public async Task ProgressReport_WithAWorkflow_WaitsAndTheActivityKeepsItsProgress_UntilItIsApproved()
    {
        var requester = new SubmittingRequester();
        var (service, submissions, activities, leaf, _, projectId) = BuildSubmissions(requester);
        leaf.UpdateProgress(0, 10);

        var submitted = (await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 60, null, null), default)).Value!;
        var second = await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 70, null, null), default);

        Assert.Equal(ApprovalStatus.PendingApproval, submitted.ApprovalStatus);
        Assert.Equal(("ActivityProgress", submitted.Id, projectId), (requester.Subject!.SubjectType, requester.Subject.SubjectId, requester.Subject.ScopeId));
        Assert.Equal(10m, leaf.ActualProgress);
        Assert.Equal("conflict", second.Error.Code); // one report at a time

        var granted = new ActivityProgressApprovalGrantedHandler(submissions, activities, new FakeWaterfallUnitOfWork());
        await granted.HandleAsync(new ApprovalGranted("ActivityProgress", submitted.Id, Tenant, null, null), default);

        Assert.Equal(60m, leaf.ActualProgress);
        Assert.Equal(ApprovalStatus.Approved, submissions.Items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task ApproverMayConfirmADifferentFigure_AndAPartialRejectionStillAppliesIt()
    {
        var (service, submissions, activities, leaf, _, projectId) = BuildSubmissions(new SubmittingRequester());
        var submitted = (await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 90, null, null), default)).Value!;

        var confirmed = await service.SetConfirmedProgressAsync(submitted.Id, new SetConfirmedProgressRequest(55), default);
        var rejected = new ActivityProgressApprovalRejectedHandler(submissions, activities, new FakeWaterfallUnitOfWork());
        await rejected.HandleAsync(new ApprovalRejected("ActivityProgress", submitted.Id, Tenant, null, "too optimistic"), default);

        Assert.Equal(55m, confirmed.Value!.ApprovedProgress);
        var stored = submissions.Items.Single();
        Assert.Equal((ApprovalStatus.Rejected, true), (stored.ApprovalStatus, stored.IsPartialRejection));
        Assert.Equal(55m, leaf.ActualProgress);

        // Once decided, the confirmed figure can no longer be changed.
        var late = await service.SetConfirmedProgressAsync(submitted.Id, new SetConfirmedProgressRequest(70), default);
        Assert.Equal("conflict", late.Error.Code);
    }

    [Fact]
    public async Task APlainRejection_ChangesNothingOnTheActivity()
    {
        var (service, submissions, activities, leaf, _, projectId) = BuildSubmissions(new SubmittingRequester());
        leaf.UpdateProgress(0, 20);
        var submitted = (await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 90, null, null), default)).Value!;

        await new ActivityProgressApprovalRejectedHandler(submissions, activities, new FakeWaterfallUnitOfWork())
            .HandleAsync(new ApprovalRejected("ActivityProgress", submitted.Id, Tenant, null, null), default);

        Assert.Equal(20m, leaf.ActualProgress);
        Assert.False(submissions.Items.Single().IsPartialRejection);
    }

    [Fact]
    public async Task ProgressReport_IsRefusedForGroupsOutOfRangeFiguresAndOtherProjects()
    {
        var (service, _, _, leaf, group, projectId) = BuildSubmissions(new NotConfiguredApprovalRequester());

        var onGroup = await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, group.Id, 10, null, null), default);
        var tooMuch = await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, projectId, leaf.Id, 101, null, null), default);
        var elsewhere = await service.SubmitAsync(new SubmitActivityProgressRequest(Tenant, Guid.NewGuid(), leaf.Id, 10, null, null), default);

        Assert.Equal("validation.error", onGroup.Error.Code);
        Assert.Equal("validation.error", tooMuch.Error.Code);
        Assert.Equal("not_found", elsewhere.Error.Code);
    }

    [Fact]
    public async Task OtherSubjectTypes_AreIgnoredByTheProgressHandlers()
    {
        var (_, submissions, activities, leaf, _, _) = BuildSubmissions(new SubmittingRequester());

        await new ActivityProgressApprovalGrantedHandler(submissions, activities, new FakeWaterfallUnitOfWork())
            .HandleAsync(new ApprovalGranted("Risk", Guid.NewGuid(), Tenant, null, null), default);

        Assert.Equal(0m, leaf.ActualProgress);
    }

    // ------------------------------------------------------------ schema

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = new WaterfallDbContext(
            new DbContextOptionsBuilder<WaterfallDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "waterfall", "2026-10-13-add-waterfall-planning.sql", typeof(WaterfallPlanningSchemaUpgrade),
            ["WaterfallProjectSettings", "ActivityProgressSubmissions"],
            ("Activities", "[Cost] decimal(18,2) NULL"),
            ("Activities", "[Importance] int NOT NULL"),
            ("Activities", "[Complexity] int NOT NULL"),
            ("Activities", "[Position] int NOT NULL"),
            ("ScheduleBaselines", "[Kind] int NOT NULL"),
            ("ScheduleBaselines", "[PlannedProgress] decimal(5,2) NOT NULL"),
            ("ScheduleBaselines", "[ActualProgress] decimal(5,2) NOT NULL"),
            ("ScheduleBaselineActivities", "[Weight] decimal(9,4) NOT NULL"),
            ("ScheduleBaselineActivities", "[PlannedProgress] decimal(5,2) NOT NULL"),
            ("ScheduleBaselineActivities", "[ActualProgress] decimal(5,2) NOT NULL"),
            ("ScheduleBaselineActivities", "[ResponsibleUserId] uniqueidentifier NULL"),
            ("ScheduleBaselineActivities", "[WbsCode] nvarchar(50) NOT NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-13-add-waterfall-planning.sql", typeof(WaterfallPlanningSchemaUpgrade));
    }
}
