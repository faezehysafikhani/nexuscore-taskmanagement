namespace Nexus.ProjectManagement.Waterfall.Application.Scheduling;

/// <summary>
/// The line of working days, numbered from 0, that all schedule arithmetic runs on: index 0 is
/// the first working day on or after the base date. Doing the maths on this line instead of on
/// raw dates is what makes weekends and holidays vanish - a 5-day task is 5 indexes long whether
/// or not a weekend falls inside it - and dates only reappear when an index is converted back.
/// </summary>
public sealed class WorkingDayAxis
{
    /// <summary>Furthest index served (about 110 years of a five-day week).</summary>
    public const int MaxIndex = 30_000;

    // How far past the last known working day to look before deciding a calendar has none.
    private const int MaxScanDays = 200_000;

    private readonly IWorkingDayCalendar _calendar;
    private readonly List<DateOnly> _days = [];
    private DateOnly _cursor;
    private int _scanned;

    public WorkingDayAxis(IWorkingDayCalendar calendar, DateOnly baseDate)
    {
        _calendar = calendar;
        _cursor = baseDate;
        BaseDate = baseDate;
        EnsureCount(1);
    }

    public DateOnly BaseDate { get; }

    /// <summary>The date of the working day with this index (negative indexes serve index 0).</summary>
    public DateOnly DateAt(int index)
    {
        index = Math.Max(index, 0);
        EnsureCount(index + 1);
        return _days[index];
    }

    /// <summary>The number of working days strictly before <paramref name="date"/> - which is also the
    /// index of the first working day on or after it. Dates before the axis start give 0.</summary>
    public int IndexOf(DateOnly date)
    {
        if (date <= _days[0])
        {
            return 0;
        }

        while (_days[^1] < date)
        {
            EnsureCount(_days.Count + 1);
        }

        var low = 0;
        var high = _days.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_days[middle] < date)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Working days from <paramref name="start"/> to <paramref name="end"/>, both included (0 when end is before start).</summary>
    public int WorkingDaysBetween(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return 0;
        }

        var throughEnd = IndexOf(end) + (_calendar.IsWorkingDay(end) ? 1 : 0);
        return Math.Max(throughEnd - IndexOf(start), 0);
    }

    private void EnsureCount(int count)
    {
        if (count > MaxIndex)
        {
            throw new InvalidOperationException("The schedule extends beyond the supported range.");
        }

        while (_days.Count < count)
        {
            if (_scanned++ > MaxScanDays)
            {
                throw new InvalidOperationException("The work calendar has no working days.");
            }

            if (_calendar.IsWorkingDay(_cursor))
            {
                _days.Add(_cursor);
                _scanned = 0;
            }

            _cursor = _cursor.AddDays(1);
        }
    }
}
