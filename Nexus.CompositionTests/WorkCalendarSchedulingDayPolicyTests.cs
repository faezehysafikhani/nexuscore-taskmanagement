using Microsoft.EntityFrameworkCore;
using Nexus.Calendar.Application;
using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Domain;
using Nexus.Calendar.Infrastructure;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// New tasks and meetings follow the organization's default work calendar when it has one; without one (or with no
/// tenant) the fixed Iranian rule. Days before today are refused either way.
/// </summary>
public sealed class WorkCalendarSchedulingDayPolicyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    // Saturday 2026-10-31, noon in Iran.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T08:30:00Z");
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static readonly DateOnly NextFriday = new(2026, 11, 6);
    private static readonly DateOnly NextThursday = new(2026, 11, 5);
    private static readonly DateOnly NextMonday = new(2026, 11, 2);
    private const DayOfWeekMask OfficeWeek = DayOfWeekMask.Saturday | DayOfWeekMask.Sunday | DayOfWeekMask.Monday | DayOfWeekMask.Tuesday | DayOfWeekMask.Wednesday;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class User(Guid? tenantId) : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Email => null;
        public string? IpAddress => null;
    }

    private static WorkCalendarService NewService()
    {
        var db = new CalendarDbContext(new DbContextOptionsBuilder<CalendarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new WorkCalendarService(new WorkCalendarRepository(db), db, null, [], null);
    }

    private static WorkCalendarSchedulingDayPolicy Policy(IWorkCalendarService service, Guid? tenant) =>
        new(service, new User(tenant), new FixedClock(Now));

    [Fact]
    public async Task TheDefaultCalendar_Decides_ItsOwnDaysOffAndExtraWorkingDays()
    {
        var service = NewService();
        var id = (await service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Office", OfficeWeek, true), default)).Value!.Id;
        await service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(NextMonday, false, "Company day off"), default);
        await service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(NextFriday, true, "Inventory"), default);
        var policy = Policy(service, Tenant);

        var dayOff = policy.Check(NextMonday);
        Assert.False(dayOff.IsAllowed);
        Assert.Contains("Company day off", dayOff.Reason);
        Assert.True(policy.Check(NextFriday).IsAllowed);    // an extra working day on a Friday
        Assert.False(policy.Check(NextThursday).IsAllowed); // not in this calendar's week
        Assert.True(policy.Check(Today).IsAllowed);
    }

    [Fact]
    public async Task ANonDefaultCalendar_ChangesNothing_TheIranianRuleApplies()
    {
        var service = NewService();
        await service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Other", DayOfWeekMask.Friday, false), default);
        var policy = Policy(service, Tenant);

        Assert.False(policy.Check(NextFriday).IsAllowed);
        Assert.True(policy.Check(NextThursday).IsAllowed);
        Assert.False(policy.Check(new DateOnly(2027, 3, 21)).IsAllowed); // Nowruz
    }

    [Fact]
    public void WithoutATenant_TheIranianRuleApplies()
    {
        var policy = Policy(NewService(), null);
        Assert.False(policy.Check(NextFriday).IsAllowed);
        Assert.True(policy.Check(NextThursday).IsAllowed);
    }

    [Fact]
    public async Task DaysBeforeToday_AreRefused_EvenOnACalendarWorkingDay()
    {
        var service = NewService();
        await service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Every day", (DayOfWeekMask)127, true), default);
        Assert.False(Policy(service, Tenant).Check(Today.AddDays(-1)).IsAllowed);
    }

    [Fact]
    public async Task ADateBeyondTheFirstWindow_IsStillReadFromTheCalendar()
    {
        var service = NewService();
        var id = (await service.CreateAsync(new CreateWorkCalendarRequest(Tenant, "Office", OfficeWeek, true), default)).Value!.Id;
        var far = Today.AddDays(1000);
        while (far.DayOfWeek != DayOfWeek.Monday) far = far.AddDays(1);
        await service.AddExceptionAsync(id, new AddWorkCalendarExceptionRequest(far, false, "Far away"), default);

        var policy = Policy(service, Tenant);
        Assert.True(policy.Check(NextMonday).IsAllowed);
        Assert.False(policy.Check(far).IsAllowed);
        Assert.True(policy.Check(far.AddDays(1)).IsAllowed);
    }
}
