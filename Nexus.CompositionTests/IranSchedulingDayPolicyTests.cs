using Nexus.Calendar.Application;

namespace Nexus.CompositionTests;

/// <summary>Days before today, Friday (not Thursday) and the official Iranian holidays refuse tasks and meetings.</summary>
public sealed class IranSchedulingDayPolicyTests
{
    // Saturday 2026-10-31, noon in Iran.
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static IranSchedulingDayPolicy At(string utc) => new(new FixedClock(DateTimeOffset.Parse(utc)));
    private readonly IranSchedulingDayPolicy _policy = At("2026-10-31T08:30:00Z");

    [Theory]
    [InlineData(2026, 11, 6)]  // Friday
    [InlineData(2027, 3, 21)]  // Nowruz, 1406/1/1 (Sunday)
    [InlineData(2027, 2, 11)]  // 22 Bahman 1405 (Thursday)
    public void Friday_and_official_holidays_are_refused(int y, int m, int d)
        => Assert.False(_policy.Check(new DateOnly(y, m, d)).IsAllowed);

    [Theory]
    [InlineData(2026, 11, 5)]  // Thursday
    [InlineData(2026, 11, 7)]  // Saturday
    [InlineData(2026, 10, 31)] // today
    public void Thursday_today_and_ordinary_days_are_allowed(int y, int m, int d)
        => Assert.True(_policy.Check(new DateOnly(y, m, d)).IsAllowed);

    [Theory]
    [InlineData(2026, 10, 30)]
    [InlineData(2025, 1, 1)]
    public void Days_before_today_are_refused(int y, int m, int d)
        => Assert.False(_policy.Check(new DateOnly(y, m, d)).IsAllowed);

    [Fact]
    public void Today_is_read_in_Iran_time_not_UTC()
    {
        // 21:00 UTC on Oct 31 is already 00:30 on Nov 1 in Iran.
        var lateNight = At("2026-10-31T21:00:00Z");
        Assert.False(lateNight.Check(new DateOnly(2026, 10, 31)).IsAllowed);
        Assert.True(lateNight.Check(new DateOnly(2026, 11, 1)).IsAllowed);
    }
}
