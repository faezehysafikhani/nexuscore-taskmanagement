using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// The recurrence rules, checked without a database.
///
/// Day numbers here are the UI's Jalali week (0 = Saturday … 6 = Friday), not
/// <see cref="DayOfWeek"/>'s. Getting that wrong would shift every weekly schedule by a day,
/// so it is pinned down explicitly.
/// </summary>
public sealed class RecurrenceCalculatorTests
{
    private readonly RecurrenceCalculator _calculator = new();

    private static RepetitiveTask Schedule(
        RecurrenceFrequency frequency,
        DateOnly startDate,
        TimeOnly? startTime = null,
        int[]? weeklyDays = null,
        int[]? monthlyDays = null,
        int? intervalWeeks = null,
        OccurrenceNth? nth = null,
        int? nthWeekday = null,
        DateOnly? endDate = null)
    {
        var schedule = new RepetitiveTask(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), frequency, startDate);
        schedule.UpdateSchedule(
            frequency, intervalWeeks, startTime ?? new TimeOnly(9, 0), null,
            weeklyDays, monthlyDays, nth, nthWeekday, startDate, endDate);
        return schedule;
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday, 0)]
    [InlineData(DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Monday, 2)]
    [InlineData(DayOfWeek.Tuesday, 3)]
    [InlineData(DayOfWeek.Wednesday, 4)]
    [InlineData(DayOfWeek.Thursday, 5)]
    [InlineData(DayOfWeek.Friday, 6)]
    public void JalaliDayIndex_StartsOnSaturday(DayOfWeek dayOfWeek, int expected) =>
        Assert.Equal(expected, RecurrenceCalculator.ToJalaliDayIndex(dayOfWeek));

    [Fact]
    public void Daily_ReturnsNextDayAtScheduledTime()
    {
        var schedule = Schedule(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1), new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        var next = _calculator.CalculateNextExecution(schedule, after);

        Assert.Equal(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Daily_ReturnsSameDay_WhenScheduledTimeHasNotPassed()
    {
        var schedule = Schedule(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1), new TimeOnly(18, 0));
        var after = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        var next = _calculator.CalculateNextExecution(schedule, after);

        Assert.Equal(new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_PicksTheNextSelectedDay()
    {
        // 2026-01-05 is a Monday, which is 2 in the UI's numbering. Asking for Wednesday (4).
        var schedule = Schedule(
            RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), new TimeOnly(9, 0), weeklyDays: [4]);

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero));

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Wednesday, next!.Value.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_WithInterval_SkipsTheWeeksInBetween()
    {
        // Every second week from Saturday 2026-01-03, on Saturdays.
        var schedule = Schedule(
            RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 3), new TimeOnly(9, 0),
            weeklyDays: [0], intervalWeeks: 2);

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero));

        // 2026-01-10 is the skipped week; the next hit is a fortnight on.
        Assert.Equal(new DateTimeOffset(2026, 1, 17, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_WithNoDaysSelected_Terminates()
    {
        var schedule = Schedule(RecurrenceFrequency.Weekly, new DateOnly(2026, 1, 1), weeklyDays: []);

        // Must not hang looking for a day that can never match.
        Assert.Null(_calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Monthly_PicksTheNextSelectedDayOfMonth()
    {
        var schedule = Schedule(
            RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1), new TimeOnly(9, 0), monthlyDays: [15]);

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 2, 15, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void MonthlyNthWeekday_FindsTheSecondMondayOfTheMonth()
    {
        // Monday is 2 in the UI's numbering.
        var schedule = Schedule(
            RecurrenceFrequency.MonthlyNthWeekday, new DateOnly(2026, 1, 1), new TimeOnly(9, 0),
            nth: OccurrenceNth.Second, nthWeekday: 2);

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 1, 12), DateOnly.FromDateTime(next.Value.UtcDateTime));
    }

    [Fact]
    public void MonthlyNthWeekday_Last_FindsTheFinalMatchingWeekday()
    {
        var schedule = Schedule(
            RecurrenceFrequency.MonthlyNthWeekday, new DateOnly(2026, 1, 1), new TimeOnly(9, 0),
            nth: OccurrenceNth.Last, nthWeekday: 2);

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(next);
        var date = DateOnly.FromDateTime(next!.Value.UtcDateTime);
        Assert.Equal(DayOfWeek.Monday, next.Value.DayOfWeek);

        // Another week would fall into February, which is what "last" means.
        Assert.NotEqual(date.Month, date.AddDays(7).Month);
    }

    [Fact]
    public void ReturnsNull_OnceTheEndDateHasPassed()
    {
        var schedule = Schedule(
            RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1),
            endDate: new DateOnly(2026, 1, 10));

        Assert.Null(_calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ReturnsNull_WhenTheScheduleIsInactive()
    {
        var schedule = Schedule(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1));
        schedule.Deactivate();

        Assert.Null(_calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void NeverReturnsATimeAtOrBeforeTheMomentAsked()
    {
        var schedule = Schedule(RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1), new TimeOnly(9, 0));
        var after = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        var next = _calculator.CalculateNextExecution(schedule, after);

        // Strictly greater, or the scheduler would fire the same occurrence again.
        Assert.True(next > after);
    }

    [Fact]
    public void StartsAtTheScheduleStart_WhenAskedFromBeforeIt()
    {
        var schedule = Schedule(RecurrenceFrequency.Daily, new DateOnly(2026, 6, 1), new TimeOnly(9, 0));

        var next = _calculator.CalculateNextExecution(
            schedule, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), next);
    }
}
