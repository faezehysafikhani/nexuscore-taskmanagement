using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Domain;
using Nexus.Integrations.ProjectCalendar;
using Nexus.Integrations.ProjectCalendar.Application;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class ScheduleServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private sealed class Weekdays : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    private sealed record Fixture(
        ScheduleService Service, FakeActivityRepository Activities, FakeDependencyRepository Dependencies,
        Project Project, FakeCalendarProvider Calendars, FakeWaterfallUnitOfWork UnitOfWork);

    private static Fixture Build(IWorkingDayCalendar? calendar = null, Guid? calendarId = null, DateOnly? start = null)
    {
        var project = new Project(Guid.NewGuid(), Tenant, "Tower", "TWR", ProjectType.Waterfall);
        project.UpdateDetails("Tower", "TWR", null, null, null, calendarId, start ?? Monday, null, null, null, null, null, null, null, null);
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var calendars = new FakeCalendarProvider(calendar);
        var unitOfWork = new FakeWaterfallUnitOfWork();
        var service = new ScheduleService(activities, dependencies, new FakeProjectRepository(project), calendars, unitOfWork,
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        return new Fixture(service, activities, dependencies, project, calendars, unitOfWork);
    }

    private static Activity Add(Fixture f, string name, int? days = null, Guid? parent = null, bool milestone = false, DateOnly? start = null)
    {
        var activity = new Activity(Guid.NewGuid(), Tenant, f.Project.Id, name, parent);
        activity.UpdateDetails(name, null, parent, null, null, null, start, null, days, null, 0);
        if (milestone)
        {
            activity.SetMilestone(true);
        }

        f.Activities.Items.Add(activity);
        return activity;
    }

    private static void Link(Fixture f, Activity predecessor, Activity successor, DependencyType type = DependencyType.FinishToStart, int lag = 0) =>
        f.Dependencies.Items.Add(new ActivityDependency(Guid.NewGuid(), Tenant, f.Project.Id, predecessor.Id, successor.Id, type, lag));

    [Fact]
    public async Task UnknownProject_IsNotFound()
    {
        var result = await Build().Service.GetScheduleAsync(Guid.NewGuid(), default);

        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetSchedule_CalculatesButChangesNothing()
    {
        var f = Build();
        var a = Add(f, "A", 3);
        var b = Add(f, "B", 2);
        Link(f, a, b);

        var result = await f.Service.GetScheduleAsync(f.Project.Id, default);

        Assert.True(result.IsSuccess);
        var schedule = result.Value!;
        Assert.False(schedule.Applied);
        Assert.False(schedule.UsesWorkCalendar);
        Assert.Equal((Monday, Monday.AddDays(4), 5), (schedule.ProjectStart, schedule.ProjectFinish, schedule.ProjectDurationDays));
        Assert.Equal(new[] { a.Id, b.Id }, schedule.CriticalPath);
        Assert.Equal(Monday.AddDays(3), schedule.Activities.Single(x => x.Id == b.Id).Start);
        Assert.Null(a.StartDate); // untouched
        Assert.Null(b.StartDate);
    }

    [Fact]
    public async Task ApplySchedule_WritesDatesAndDurations_AndRecalculatingGivesTheSameAnswer()
    {
        var f = Build();
        var phase = Add(f, "Phase");
        var a = Add(f, "A", 3, parent: phase.Id);
        var b = Add(f, "B", 2, parent: phase.Id);
        var gate = Add(f, "Gate", milestone: true);
        Link(f, a, b);
        Link(f, b, gate);

        var applied = await f.Service.ApplyScheduleAsync(f.Project.Id, default);

        Assert.True(applied.IsSuccess);
        Assert.True(applied.Value!.Applied);
        Assert.Equal((Monday, Monday.AddDays(2), 3), (a.StartDate, a.EndDate, a.DurationDays));
        Assert.Equal((Monday.AddDays(3), Monday.AddDays(4), 2), (b.StartDate, b.EndDate, b.DurationDays));
        Assert.Equal((Monday, Monday.AddDays(4), 5), (phase.StartDate, phase.EndDate, phase.DurationDays));
        Assert.Equal((Monday.AddDays(4), Monday.AddDays(4), 0), (gate.StartDate, gate.EndDate, gate.DurationDays));
        Assert.True(gate.IsMilestone);

        // The stored dates must not become constraints: a second pass changes nothing.
        var again = await f.Service.GetScheduleAsync(f.Project.Id, default);
        Assert.Equal(applied.Value.Activities, again.Value!.Activities);
        Assert.Equal(applied.Value.CriticalPath, again.Value.CriticalPath);
    }

    [Fact]
    public async Task ApplyThenShorten_PullsTheSuccessorEarlier_EvenThoughItsOldDateIsStored()
    {
        var f = Build();
        var a = Add(f, "A", 5);
        var b = Add(f, "B", 2);
        Link(f, a, b);
        await f.Service.ApplyScheduleAsync(f.Project.Id, default);
        Assert.Equal(Monday.AddDays(5), b.StartDate);

        a.UpdateDetails("A", null, null, null, null, null, a.StartDate, a.EndDate, 3, null, 0);
        await f.Service.ApplyScheduleAsync(f.Project.Id, default);

        Assert.Equal(Monday.AddDays(3), b.StartDate);
    }

    [Fact]
    public async Task TheProjectsCalendar_IsAskedFor_UsingTheProjectsTenantAndCalendarId()
    {
        var calendarId = Guid.NewGuid();
        var f = Build(new Weekdays(), calendarId);
        Add(f, "A", 6);

        var result = await f.Service.GetScheduleAsync(f.Project.Id, default);

        Assert.True(result.Value!.UsesWorkCalendar);
        Assert.Equal((Tenant, calendarId), (f.Calendars.AskedForTenant, f.Calendars.AskedForCalendar));
        // Six working days from Monday end on the next Monday: the weekend is skipped.
        Assert.Equal(Monday.AddDays(7), result.Value.ProjectFinish);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task ACalendarThatCannotBeLoaded_FallsBackToCalendarDays_WithAWarning()
    {
        var f = Build(calendar: null, calendarId: Guid.NewGuid());
        Add(f, "A", 6);

        var result = await f.Service.GetScheduleAsync(f.Project.Id, default);

        Assert.False(result.Value!.UsesWorkCalendar);
        Assert.Contains(result.Value.Warnings, w => w.Contains("work calendar"));
        Assert.Equal(Monday.AddDays(5), result.Value.ProjectFinish);
    }

    [Fact]
    public async Task AProjectWithNoCalendar_NeedsNoWarning()
    {
        var f = Build();
        Add(f, "A", 2);

        Assert.Empty((await f.Service.GetScheduleAsync(f.Project.Id, default)).Value!.Warnings);
    }

    [Fact]
    public async Task ActivitiesWithNoDuration_AreWarnedAbout()
    {
        var f = Build();
        Add(f, "Vague");

        var result = await f.Service.GetScheduleAsync(f.Project.Id, default);

        Assert.Contains(result.Value!.Warnings, w => w.Contains("Vague") && w.Contains("one day"));
    }

    [Fact]
    public async Task ACycleInStoredLinks_IsReportedAsAConflict_AndNothingIsApplied()
    {
        var f = Build();
        var a = Add(f, "A", 1);
        var b = Add(f, "B", 1);
        Link(f, a, b);
        Link(f, b, a);

        var result = await f.Service.ApplyScheduleAsync(f.Project.Id, default);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Null(a.StartDate);
    }

    [Fact]
    public async Task AProjectWithNoStartDate_BeginsOnTheEarliestActivityDate_OrToday()
    {
        var withDate = Build(start: null);
        withDate.Project.UpdateDetails("Tower", "TWR", null, null, null, null, null, null, null, null, null, null, null, null, null);
        Add(withDate, "A", 2, start: Monday.AddDays(10));
        Assert.Equal(Monday.AddDays(10), (await withDate.Service.GetScheduleAsync(withDate.Project.Id, default)).Value!.ProjectStart);

        var none = Build();
        none.Project.UpdateDetails("Tower", "TWR", null, null, null, null, null, null, null, null, null, null, null, null, null);
        Add(none, "A", 2);
        Assert.Equal(new DateOnly(2026, 1, 1), (await none.Service.GetScheduleAsync(none.Project.Id, default)).Value!.ProjectStart);
    }

    // ----------------------------------------- the calendar integration

    [Fact]
    public void TheAdapter_AgreesWithTheCalendarEntity_ForEveryDayOfAYear()
    {
        var calendar = new WorkCalendar(Guid.NewGuid(), Tenant, "Iran", DayOfWeekMask.IranWorkWeek);
        calendar.AddException(Guid.NewGuid(), new DateOnly(2026, 3, 21), isWorkingDay: false, "Nowruz");   // a working day made a holiday
        calendar.AddException(Guid.NewGuid(), new DateOnly(2026, 3, 19), isWorkingDay: true, "Make-up day"); // a Thursday made a working day

        var adapter = new WorkCalendarAdapter(calendar);

        for (var date = new DateOnly(2026, 1, 1); date.Year == 2026; date = date.AddDays(1))
        {
            Assert.True(calendar.IsWorkingDay(date) == adapter.IsWorkingDay(date), $"{date} ({date.DayOfWeek})");
        }

        Assert.False(adapter.IsWorkingDay(new DateOnly(2026, 3, 21)));
        Assert.True(adapter.IsWorkingDay(new DateOnly(2026, 3, 19)));
        Assert.False(adapter.IsWorkingDay(new DateOnly(2026, 3, 19).AddDays(1))); // the Friday after
    }

    [Fact]
    public async Task TheProvider_ReturnsOnlyCalendarsOfTheAskingTenant()
    {
        var mine = new WorkCalendar(Guid.NewGuid(), Tenant, "Mine", DayOfWeekMask.AllDays);
        var theirs = new WorkCalendar(Guid.NewGuid(), Guid.NewGuid(), "Theirs", DayOfWeekMask.AllDays);
        var provider = new WorkCalendarProvider(new FakeCalendarRepository(mine, theirs));

        Assert.NotNull(await provider.GetAsync(Tenant, mine.Id, default));
        Assert.Null(await provider.GetAsync(Tenant, theirs.Id, default));
        Assert.Null(await provider.GetAsync(Tenant, Guid.NewGuid(), default));
        Assert.Null(await provider.GetAsync(Tenant, null, default));
    }

    [Fact]
    public async Task TheIntegration_ReplacesTheDefaultProvider_InEitherRegistrationOrder()
    {
        var waterfallFirst = new ServiceCollection();
        waterfallFirst.AddWaterfallPlanning();
        waterfallFirst.AddProjectCalendarIntegration();

        var integrationFirst = new ServiceCollection();
        integrationFirst.AddProjectCalendarIntegration();
        integrationFirst.AddWaterfallPlanning();

        foreach (var services in new[] { waterfallFirst, integrationFirst })
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IWorkingDayCalendarProvider));
            Assert.Equal(typeof(WorkCalendarProvider), descriptor.ImplementationType);
        }

        // Without the integration, the default knows no calendars.
        var alone = new ServiceCollection();
        alone.AddWaterfallPlanning();
        Assert.Equal(typeof(NullWorkingDayCalendarProvider), Assert.Single(alone, d => d.ServiceType == typeof(IWorkingDayCalendarProvider)).ImplementationType);
        Assert.Null(await new NullWorkingDayCalendarProvider().GetAsync(Tenant, Guid.NewGuid(), default));
    }

    private sealed class FakeCalendarRepository(params WorkCalendar[] calendars) : Nexus.Calendar.Application.IWorkCalendarRepository
    {
        public Task<WorkCalendar?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(calendars.SingleOrDefault(c => c.Id == id));
        public Task<IReadOnlyList<WorkCalendar>> ListAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(WorkCalendar calendar, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAsync(WorkCalendar calendar, CancellationToken ct) => throw new NotSupportedException();
    }
}
