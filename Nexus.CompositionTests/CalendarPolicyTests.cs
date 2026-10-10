using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Calendar.Application;
using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Domain;
using Nexus.Calendar.Infrastructure;
using Nexus.Calendar.IranianHolidays;
using Nexus.Integrations.ProjectCalendar.Application;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class CalendarPolicyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeHolidays(params (DateOnly Date, string Reason)[] holidays) : IOfficialHolidayProvider
    {
        public string? GetReason(DateOnly date) => holidays.Where(h => h.Date == date).Select(h => h.Reason).FirstOrDefault();
    }

    private sealed class Fixture
    {
        public CalendarDbContext Db { get; }
        public WorkCalendarService Service { get; }

        public Fixture(IOfficialHolidayProvider? holidays, params ICalendarUsageChecker[] checkers)
        {
            Db = new CalendarDbContext(new DbContextOptionsBuilder<CalendarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Service = new WorkCalendarService(new WorkCalendarRepository(Db), Db, holidays, checkers);
        }
    }

    // Saturday-Wednesday working (the Iranian office week without Thursday).
    private const DayOfWeekMask OfficeWeek = DayOfWeekMask.Saturday | DayOfWeekMask.Sunday | DayOfWeekMask.Monday | DayOfWeekMask.Tuesday | DayOfWeekMask.Wednesday;
    private static readonly DateOnly Monday = new(2026, 6, 1);

    [Fact]
    public async Task ANewCalendar_DefaultsToEightHours_AndFollowingOfficialHolidays()
    {
        var f = new Fixture(null);

        var dto = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Main", OfficeWeek, true), default)).Value!;

        Assert.Equal(8, dto.WorkHoursPerDay);
        Assert.True(dto.ApplyOfficialHolidays);
    }

    [Fact]
    public async Task Create_StoresTheGivenPolicy_AndDescription()
    {
        var f = new Fixture(null);

        var dto = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Site", OfficeWeek, false, 10, false, "Shifts"), default)).Value!;

        Assert.Equal(10, dto.WorkHoursPerDay);
        Assert.False(dto.ApplyOfficialHolidays);
        Assert.Equal("Shifts", dto.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(-1)]
    public async Task TheHoursPerDay_MustBeBetween1And24(int hours)
    {
        var f = new Fixture(null);
        Assert.Equal("validation.error", (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "X", OfficeWeek, false, hours), default)).Error.Code);

        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Y", OfficeWeek, false), default)).Value!.Id;
        Assert.Equal("validation.error", (await f.Service.UpdateAsync(id, new UpdateWorkCalendarRequest("Y", null, OfficeWeek, false, hours), default)).Error.Code);
    }

    [Fact]
    public async Task Update_LeavesThePolicyAlone_WhenOmitted_AndChangesItWhenGiven()
    {
        var f = new Fixture(null);
        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Main", OfficeWeek, false, 6, false), default)).Value!.Id;

        var renamed = (await f.Service.UpdateAsync(id, new UpdateWorkCalendarRequest("Renamed", null, OfficeWeek, false), default)).Value!;
        Assert.Equal(6, renamed.WorkHoursPerDay);
        Assert.False(renamed.ApplyOfficialHolidays);

        var changed = (await f.Service.UpdateAsync(id, new UpdateWorkCalendarRequest("Renamed", null, OfficeWeek, false, 9, true), default)).Value!;
        Assert.Equal(9, changed.WorkHoursPerDay);
        Assert.True(changed.ApplyOfficialHolidays);
    }

    // ------------------------------------------------- whole-calendar saves, default, delete

    private sealed class UsedBy(string? usage) : ICalendarUsageChecker
    {
        public Task<string?> GetUsageAsync(Guid tenantId, Guid calendarId, CancellationToken cancellationToken) => Task.FromResult(usage);
    }

    [Fact]
    public async Task Create_WithExceptions_StoresThem_AndUpdateReplacesThemWholesale()
    {
        var f = new Fixture(null);
        var d1 = new DateOnly(2026, 6, 10);
        var d2 = new DateOnly(2026, 6, 11);
        var d3 = new DateOnly(2026, 6, 12);
        var created = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false, Exceptions:
            [new AddWorkCalendarExceptionRequest(d1, false, "one"), new AddWorkCalendarExceptionRequest(d2, false, "two")]), default)).Value!;
        Assert.Equal([d1, d2], created.Exceptions.Select(e => e.Date).Order());
        var keptId = created.Exceptions.Single(e => e.Date == d1).Id;

        var updated = (await f.Service.UpdateAsync(created.Id, new UpdateWorkCalendarRequest("A", null, OfficeWeek, false, Exceptions:
            [new AddWorkCalendarExceptionRequest(d1, false, "renamed"), new AddWorkCalendarExceptionRequest(d3, true, "extra")]), default)).Value!;

        Assert.Equal([d1, d3], updated.Exceptions.Select(e => e.Date).Order());
        var kept = updated.Exceptions.Single(e => e.Date == d1);
        Assert.Equal(keptId, kept.Id);            // an exception that stays keeps its identity
        Assert.Equal("renamed", kept.Description);
        Assert.True(updated.Exceptions.Single(e => e.Date == d3).IsWorkingDay);
    }

    [Fact]
    public async Task Update_WithoutExceptions_LeavesThemAlone_AndAnEmptyListClearsThem()
    {
        var f = new Fixture(null);
        var created = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false, Exceptions:
            [new AddWorkCalendarExceptionRequest(Monday, false, "x")]), default)).Value!;

        var untouched = (await f.Service.UpdateAsync(created.Id, new UpdateWorkCalendarRequest("A", null, OfficeWeek, false), default)).Value!;
        Assert.Single(untouched.Exceptions);

        var cleared = (await f.Service.UpdateAsync(created.Id, new UpdateWorkCalendarRequest("A", null, OfficeWeek, false, Exceptions: []), default)).Value!;
        Assert.Empty(cleared.Exceptions);
    }

    [Fact]
    public async Task ThereIsOneDefaultCalendarPerTenant()
    {
        var f = new Fixture(null);
        var first = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "First", OfficeWeek, true), default)).Value!;
        var second = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Second", OfficeWeek, true), default)).Value!;
        var other = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Guid.NewGuid(), "Other tenant", OfficeWeek, true), default)).Value!;

        var calendars = (await f.Service.ListAsync(Tenant, default)).Value!;
        Assert.False(calendars.Single(c => c.Id == first.Id).IsDefault);
        Assert.True(calendars.Single(c => c.Id == second.Id).IsDefault);
        Assert.True((await f.Service.GetAsync(other.Id, default)).Value!.IsDefault); // another tenant's default is untouched

        await f.Service.UpdateAsync(first.Id, new UpdateWorkCalendarRequest("First", null, OfficeWeek, true), default);
        calendars = (await f.Service.ListAsync(Tenant, default)).Value!;
        Assert.Equal(["First"], calendars.Where(c => c.IsDefault).Select(c => c.Name));
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedCalendar_AndRefusesOneThatIsInUse()
    {
        var f = new Fixture(null, new UsedBy(null), new UsedBy("2 actions"));
        var used = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Used", OfficeWeek, false), default)).Value!;

        var refused = await f.Service.DeleteAsync(used.Id, default);
        Assert.Equal("conflict", refused.Error.Code);
        Assert.Contains("2 actions", refused.Error.Message);
        Assert.True((await f.Service.GetAsync(used.Id, default)).IsSuccess);

        var free = new Fixture(null, new UsedBy(null));
        var calendar = (await free.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Free", OfficeWeek, false), default)).Value!;
        Assert.True((await free.Service.DeleteAsync(calendar.Id, default)).IsSuccess);
        Assert.Equal("not_found", (await free.Service.GetAsync(calendar.Id, default)).Error.Code);
        Assert.Equal("not_found", (await free.Service.DeleteAsync(calendar.Id, default)).Error.Code);
    }

    [Fact]
    public async Task TheDefaultCalendar_CannotBeDeleted()
    {
        var f = new Fixture(null);
        var main = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Main", OfficeWeek, true), default)).Value!;

        Assert.Equal("conflict", (await f.Service.DeleteAsync(main.Id, default)).Error.Code);

        var other = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Other", OfficeWeek, true), default)).Value!; // now the default
        Assert.True((await f.Service.DeleteAsync(main.Id, default)).IsSuccess);
        Assert.Equal("conflict", (await f.Service.DeleteAsync(other.Id, default)).Error.Code);
    }

    [Fact]
    public async Task Delete_WithNoCheckersInstalled_JustDeletes()
    {
        var f = new Fixture(null);
        var calendar = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false), default)).Value!;

        Assert.True((await f.Service.DeleteAsync(calendar.Id, default)).IsSuccess);
    }

    private sealed class FakeActions(params ActionItem[] actions) : IActionItemRepository
    {
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(actions.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(actions);
        public Task AddAsync(ActionItem action, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task ActionsReportTheCalendarsTheyUse()
    {
        var calendarId = Guid.NewGuid();
        var uses = new ActionItem(Guid.NewGuid(), Tenant, "A", Guid.NewGuid(), calendarId);
        var other = new ActionItem(Guid.NewGuid(), Tenant, "B", Guid.NewGuid(), Guid.NewGuid());
        var checker = new ActionCalendarUsageChecker(new FakeActions(uses, other));

        Assert.Equal("1 action(s)", await checker.GetUsageAsync(Tenant, calendarId, default));
        Assert.Null(await checker.GetUsageAsync(Tenant, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ProjectsReportTheCalendarsTheyUse_ThroughTheRealRepositoryFilter()
    {
        var db = new ProjectManagementCoreDbContext(new DbContextOptionsBuilder<ProjectManagementCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var calendarId = Guid.NewGuid();
        var project = new Project(Guid.NewGuid(), Tenant, "P", "P1", ProjectType.Waterfall, null, null);
        project.UpdateDetails("P", "P1", null, null, null, calendarId, null, null, null, null, null, null, null, null, null);
        var other = new Project(Guid.NewGuid(), Tenant, "Q", "Q1", ProjectType.Waterfall, null, null);
        db.Projects.AddRange(project, other);
        await db.SaveChangesAsync();
        var checker = new ProjectCalendarUsageChecker(new ProjectRepository(db));

        Assert.Equal("1 project(s)", await checker.GetUsageAsync(Tenant, calendarId, default));
        Assert.Null(await checker.GetUsageAsync(Tenant, Guid.NewGuid(), default));
        Assert.Null(await checker.GetUsageAsync(Guid.NewGuid(), calendarId, default)); // another tenant
    }

    // ------------------------------------------------------------------ the rule

    [Fact]
    public async Task AnOfficialHoliday_ClosesAWorkingDay_OnlyWhenTheCalendarFollowsThem()
    {
        var holidays = new FakeHolidays((Monday, "Holiday"));
        var f = new Fixture(holidays);
        var follows = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false, null, true), default)).Value!.Id;
        var ignores = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "B", OfficeWeek, false, null, false), default)).Value!.Id;

        Assert.False((await f.Service.IsWorkingDayAsync(follows, Monday, default)).Value);
        Assert.True((await f.Service.IsWorkingDayAsync(ignores, Monday, default)).Value);
    }

    [Fact]
    public async Task WithoutAProvider_OfficialHolidaysChangeNothing()
    {
        var f = new Fixture(null);
        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false), default)).Value!.Id;

        Assert.True((await f.Service.IsWorkingDayAsync(id, Monday, default)).Value);
        Assert.Empty((await f.Service.ListOfficialHolidaysAsync(Monday, Monday.AddDays(30), default)).Value!);
    }

    [Fact]
    public async Task AnExceptionOfTheCalendar_BeatsAnOfficialHoliday_AndTheWeeklyPattern()
    {
        var f = new Fixture(new FakeHolidays((Monday, "Holiday")));
        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false), default)).Value!.Id;
        var thursday = Monday.AddDays(3);
        await f.Service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(Monday, true, "We work"), default);
        await f.Service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(thursday, true, "Extra day"), default);

        Assert.True((await f.Service.IsWorkingDayAsync(id, Monday, default)).Value);
        Assert.True((await f.Service.IsWorkingDayAsync(id, thursday, default)).Value);
    }

    [Fact]
    public async Task TheDaysEndpoint_SaysWhyEachDayIsWhatItIs()
    {
        var f = new Fixture(new FakeHolidays((Monday, "Holiday")));
        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false), default)).Value!.Id;
        var tuesday = Monday.AddDays(1);
        var friday = Monday.AddDays(4);
        await f.Service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(tuesday, false, "Day off"), default);

        var days = (await f.Service.GetDaysAsync(id, Monday, Monday.AddDays(4), default)).Value!;

        Assert.Equal(5, days.Count);
        Assert.Equal(new CalendarDayDto(Monday, false, CalendarDaySource.OfficialHoliday, "Holiday"), days[0]);
        Assert.Equal(new CalendarDayDto(tuesday, false, CalendarDaySource.Exception, "Day off"), days[1]);
        Assert.Equal(new CalendarDayDto(Monday.AddDays(2), true, CalendarDaySource.Weekly, null), days[2]);
        Assert.Equal(new CalendarDayDto(friday, false, CalendarDaySource.Weekly, null), days[4]);
    }

    [Fact]
    public async Task TheRangeQueries_AreBounded_AndValidated()
    {
        var f = new Fixture(new FakeHolidays());
        var id = (await f.Service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "A", OfficeWeek, false), default)).Value!.Id;

        Assert.Equal("validation.error", (await f.Service.GetDaysAsync(id, Monday, Monday.AddDays(-1), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Service.GetDaysAsync(id, Monday, Monday.AddDays(WorkCalendarService.MaxDaysPerQuery), default)).Error.Code);
        Assert.True((await f.Service.GetDaysAsync(id, Monday, Monday.AddDays(WorkCalendarService.MaxDaysPerQuery - 1), default)).IsSuccess);
        Assert.Equal("not_found", (await f.Service.GetDaysAsync(Guid.NewGuid(), Monday, Monday, default)).Error.Code);
        Assert.Equal("validation.error", (await f.Service.ListOfficialHolidaysAsync(Monday, Monday.AddDays(5000), default)).Error.Code);
    }

    [Fact]
    public async Task TheOfficialHolidayList_ReturnsOnlyHolidays_InOrder()
    {
        var f = new Fixture(new FakeHolidays((Monday.AddDays(3), "B"), (Monday, "A")));

        var list = (await f.Service.ListOfficialHolidaysAsync(Monday, Monday.AddDays(10), default)).Value!;

        Assert.Equal(["A", "B"], list.Select(h => h.Reason));
    }

    // ------------------------------------------------------- the scheduling adapter

    [Fact]
    public void TheSchedulingAdapter_FollowsTheSameRule()
    {
        var follows = new WorkCalendar(Guid.NewGuid(), Tenant, "A", OfficeWeek);
        follows.SetPolicy(8, true);
        var ignores = new WorkCalendar(Guid.NewGuid(), Tenant, "B", OfficeWeek);
        ignores.SetPolicy(8, false);
        var holidays = new FakeHolidays((Monday, "Holiday"));

        Assert.False(new WorkCalendarAdapter(follows, holidays).IsWorkingDay(Monday));
        Assert.True(new WorkCalendarAdapter(ignores, holidays).IsWorkingDay(Monday));
        Assert.True(new WorkCalendarAdapter(follows).IsWorkingDay(Monday)); // no provider installed

        follows.AddException(Guid.NewGuid(), Monday, true, "Open");
        Assert.True(new WorkCalendarAdapter(follows, holidays).IsWorkingDay(Monday));
    }

    // -------------------------------------------------------- Iran's official holidays

    private static readonly IranianOfficialHolidayProvider Iran = new();

    [Theory]
    [InlineData("2026-03-21", "عید نوروز")]                      // 1 Farvardin 1405
    [InlineData("2026-03-24", "عید نوروز")]                      // 4 Farvardin
    [InlineData("2026-04-01", "روز جمهوری اسلامی")]             // 12 Farvardin
    [InlineData("2026-04-02", "روز طبیعت (سیزده به در)")]        // 13 Farvardin
    [InlineData("2027-02-11", "پیروزی انقلاب اسلامی ایران")]     // 22 Bahman 1405
    public void IranianHolidays_KnownSolarDates(string date, string expected) =>
        Assert.Equal(expected, Iran.GetReason(DateOnly.Parse(date)));

    [Fact]
    public void IranianHolidays_AnOrdinaryDayHasNone_AndLunarOnesComeFromTheTable()
    {
        Assert.Null(Iran.GetReason(new DateOnly(2026, 6, 1)));
        Assert.Null(Iran.GetReason(new DateOnly(1500, 1, 1))); // outside what the Persian calendar can express: no holiday, no exception

        // 1 Farvardin 1405 is Eid al-Fitr in the table AND Nowruz: the solar entry wins, either way it is a holiday.
        Assert.NotNull(Iran.GetReason(new DateOnly(2026, 3, 21)));
        // 6 Khordad 1405: Eid al-Adha (lunar table).
        Assert.Equal("عید سعید قربان", Iran.GetReason(new DateOnly(2026, 5, 27)));
    }

    // ---------------------------------------------------------------------- schema

    private static CalendarDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<CalendarDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "calendar", "2026-10-10-add-calendar-policy.sql", typeof(CalendarSchemaUpgrade),
            [],
            ("WorkCalendars", "[WorkHoursPerDay] int NOT NULL"),
            ("WorkCalendars", "[ApplyOfficialHolidays] bit NOT NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-10-add-calendar-policy.sql", typeof(CalendarSchemaUpgrade));
    }
}
