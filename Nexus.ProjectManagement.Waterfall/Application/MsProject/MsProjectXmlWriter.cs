using System.Globalization;
using System.Xml.Linq;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.MsProject;

/// <summary>
/// Writes a calculated schedule as MS Project XML (MSPDI): a project-summary row, the WBS in
/// outline order with start, finish, working-day duration, milestone/summary flags, percent
/// complete and predecessor links, and one calendar carrying the project's working week and
/// holidays. Elements follow the order MS Project's schema defines.
/// </summary>
public static class MsProjectXmlWriter
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    public const int MinutesPerDay = MsProjectXmlReader.DefaultMinutesPerDay;

    /// <summary>Most holiday exceptions written, so an odd calendar cannot produce a huge file.</summary>
    private const int MaxCalendarExceptions = 1_000;

    public static XDocument Write(
        string projectName, ScheduleResult schedule, IReadOnlyList<ScheduleLinkInput> links, IWorkingDayCalendar calendar)
    {
        var order = OutlineOrder(schedule);
        var uidOf = order.Select((activity, index) => (activity.Id, Uid: index + 1)).ToDictionary(x => x.Id, x => x.Uid);
        var byId = schedule.Activities.ToDictionary(a => a.Id);
        var depth = order.ToDictionary(a => a.Id, a => Depth(a, byId));
        var outlineNumber = OutlineNumbers(order, depth);

        var tasks = new XElement(Ns + "Tasks", SummaryRow(projectName, schedule));
        foreach (var activity in order)
        {
            tasks.Add(TaskElement(activity, uidOf[activity.Id], depth[activity.Id], outlineNumber[activity.Id], links, uidOf));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(Ns + "Project",
            new XElement(Ns + "SaveVersion", 14),
            new XElement(Ns + "Name", projectName),
            new XElement(Ns + "Title", projectName),
            new XElement(Ns + "ScheduleFromStart", 1),
            new XElement(Ns + "StartDate", Stamp(schedule.ProjectStart, 8)),
            new XElement(Ns + "FinishDate", Stamp(schedule.ProjectFinish, 17)),
            new XElement(Ns + "CalendarUID", 1),
            new XElement(Ns + "MinutesPerDay", MinutesPerDay),
            new XElement(Ns + "MinutesPerWeek", MinutesPerDay * 5),
            new XElement(Ns + "DaysPerMonth", 20),
            new XElement(Ns + "DefaultStartTime", "08:00:00"),
            new XElement(Ns + "DefaultFinishTime", "17:00:00"),
            CalendarElement(calendar, schedule.ProjectStart, schedule.ProjectFinish),
            tasks));
    }

    private static XElement SummaryRow(string projectName, ScheduleResult schedule) =>
        new(Ns + "Task",
            new XElement(Ns + "UID", 0), new XElement(Ns + "ID", 0), new XElement(Ns + "Name", projectName),
            new XElement(Ns + "Active", 1), new XElement(Ns + "Manual", 0), new XElement(Ns + "Type", 1), new XElement(Ns + "IsNull", 0),
            new XElement(Ns + "WBS", "0"), new XElement(Ns + "OutlineNumber", "0"), new XElement(Ns + "OutlineLevel", 0),
            new XElement(Ns + "Start", Stamp(schedule.ProjectStart, 8)), new XElement(Ns + "Finish", Stamp(schedule.ProjectFinish, 17)),
            new XElement(Ns + "Duration", Duration(schedule.ProjectDurationDays)), new XElement(Ns + "DurationFormat", 7),
            new XElement(Ns + "Milestone", 0), new XElement(Ns + "Summary", 1),
            new XElement(Ns + "PercentComplete", Percent(schedule.ActualProgress)),
            new XElement(Ns + "ConstraintType", 0), new XElement(Ns + "CalendarUID", -1));

    private static XElement TaskElement(
        ScheduledActivity activity, int uid, int depth, string outline,
        IReadOnlyList<ScheduleLinkInput> links, Dictionary<Guid, int> uidOf)
    {
        var element = new XElement(Ns + "Task",
            new XElement(Ns + "UID", uid), new XElement(Ns + "ID", uid), new XElement(Ns + "Name", activity.Name),
            new XElement(Ns + "Active", 1), new XElement(Ns + "Manual", 0), new XElement(Ns + "Type", 1), new XElement(Ns + "IsNull", 0),
            new XElement(Ns + "WBS", outline), new XElement(Ns + "OutlineNumber", outline), new XElement(Ns + "OutlineLevel", depth + 1),
            new XElement(Ns + "Priority", 500),
            // A milestone has no length: both ends are the start of its day.
            new XElement(Ns + "Start", Stamp(activity.Start, 8)),
            new XElement(Ns + "Finish", activity.IsMilestone ? Stamp(activity.Finish, 8) : Stamp(activity.Finish, 17)),
            new XElement(Ns + "Duration", Duration(activity.DurationDays)), new XElement(Ns + "DurationFormat", 7),
            new XElement(Ns + "Milestone", activity.IsMilestone ? 1 : 0), new XElement(Ns + "Summary", activity.IsSummary ? 1 : 0),
            new XElement(Ns + "Critical", activity.IsCritical ? 1 : 0),
            new XElement(Ns + "TotalSlack", activity.TotalFloatDays * MinutesPerDay * 10),
            new XElement(Ns + "PercentComplete", Percent(activity.ActualProgress)),
            new XElement(Ns + "ConstraintType", 0), new XElement(Ns + "CalendarUID", -1));

        foreach (var link in links.Where(l => l.SuccessorId == activity.Id && uidOf.ContainsKey(l.PredecessorId)))
        {
            element.Add(new XElement(Ns + "PredecessorLink",
                new XElement(Ns + "PredecessorUID", uidOf[link.PredecessorId]),
                new XElement(Ns + "Type", link.Type switch
                {
                    DependencyType.FinishToFinish => 0,
                    DependencyType.StartToFinish => 2,
                    DependencyType.StartToStart => 3,
                    _ => 1
                }),
                new XElement(Ns + "CrossProject", 0),
                new XElement(Ns + "LinkLag", (long)link.LagDays * MinutesPerDay * 10),
                new XElement(Ns + "LagFormat", 7)));
        }

        return element;
    }

    /// <summary>One calendar: the weekdays the project mostly works, plus the dates that depart from that.</summary>
    private static XElement CalendarElement(IWorkingDayCalendar calendar, DateOnly start, DateOnly finish)
    {
        var weekly = WeeklyPattern(calendar, start);
        var weekDays = new XElement(Ns + "WeekDays");
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var working = weekly[(int)day];
            var weekDay = new XElement(Ns + "WeekDay",
                new XElement(Ns + "DayType", (int)day + 1), // MSPDI: Sunday = 1 ... Saturday = 7
                new XElement(Ns + "DayWorking", working ? 1 : 0));
            if (working)
            {
                weekDay.Add(new XElement(Ns + "WorkingTimes",
                    WorkingTime("08:00:00", "12:00:00"), WorkingTime("13:00:00", "17:00:00")));
            }

            weekDays.Add(weekDay);
        }

        var exceptions = new XElement(Ns + "Exceptions");
        var from = start.AddDays(-7);
        var until = finish.AddDays(30);
        for (var date = from; date <= until && exceptions.Elements().Count() < MaxCalendarExceptions; date = date.AddDays(1))
        {
            var working = calendar.IsWorkingDay(date);
            if (working == weekly[(int)date.DayOfWeek])
            {
                continue;
            }

            var exception = new XElement(Ns + "Exception",
                new XElement(Ns + "EnteredByOccurrences", 0),
                new XElement(Ns + "TimePeriod",
                    new XElement(Ns + "FromDate", Stamp(date, 0)),
                    new XElement(Ns + "ToDate", $"{date:yyyy-MM-dd}T23:59:00")),
                new XElement(Ns + "Occurrences", 1),
                new XElement(Ns + "Name", working ? "Working day" : "Holiday"),
                new XElement(Ns + "Type", 1),
                new XElement(Ns + "DayWorking", working ? 1 : 0));
            if (working)
            {
                exception.Add(new XElement(Ns + "WorkingTimes", WorkingTime("08:00:00", "12:00:00"), WorkingTime("13:00:00", "17:00:00")));
            }

            exceptions.Add(exception);
        }

        var element = new XElement(Ns + "Calendar",
            new XElement(Ns + "UID", 1), new XElement(Ns + "Name", "Standard"),
            new XElement(Ns + "IsBaseCalendar", 1), new XElement(Ns + "BaseCalendarUID", -1), weekDays);
        if (exceptions.HasElements)
        {
            element.Add(exceptions);
        }

        return new XElement(Ns + "Calendars", element);
    }

    /// <summary>A weekday is a working day of the week when it works on at least half of the
    /// dates sampled over the following three years; individual holidays are written as exceptions.</summary>
    public static bool[] WeeklyPattern(IWorkingDayCalendar calendar, DateOnly from)
    {
        var worked = new int[7];
        var total = new int[7];
        for (var date = from; date < from.AddYears(3); date = date.AddDays(1))
        {
            total[(int)date.DayOfWeek]++;
            if (calendar.IsWorkingDay(date))
            {
                worked[(int)date.DayOfWeek]++;
            }
        }

        return Enumerable.Range(0, 7).Select(day => worked[day] * 2 >= total[day]).ToArray();
    }

    private static XElement WorkingTime(string from, string to) =>
        new(Ns + "WorkingTime", new XElement(Ns + "FromTime", from), new XElement(Ns + "ToTime", to));

    /// <summary>Tree order: each activity directly followed by its sub-activities, siblings by start then name.</summary>
    private static List<ScheduledActivity> OutlineOrder(ScheduleResult schedule)
    {
        var childrenOf = schedule.Activities.Where(a => a.ParentId is not null)
            .GroupBy(a => a.ParentId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(a => a.StartIndex).ThenBy(a => a.Name, StringComparer.Ordinal).ToList());
        var ordered = new List<ScheduledActivity>();

        void Visit(ScheduledActivity activity)
        {
            ordered.Add(activity);
            if (childrenOf.TryGetValue(activity.Id, out var children))
            {
                children.ForEach(Visit);
            }
        }

        schedule.Activities.Where(a => a.ParentId is null).OrderBy(a => a.StartIndex).ThenBy(a => a.Name, StringComparer.Ordinal).ToList().ForEach(Visit);
        return ordered;
    }

    private static int Depth(ScheduledActivity activity, Dictionary<Guid, ScheduledActivity> byId)
    {
        var depth = 0;
        for (var cursor = activity.ParentId; cursor is { } id && byId.TryGetValue(id, out var parent); cursor = parent.ParentId)
        {
            depth++;
        }

        return depth;
    }

    private static Dictionary<Guid, string> OutlineNumbers(List<ScheduledActivity> order, Dictionary<Guid, int> depth)
    {
        var counters = new List<int>();
        var numbers = new Dictionary<Guid, string>();
        foreach (var activity in order)
        {
            var level = depth[activity.Id];
            while (counters.Count <= level)
            {
                counters.Add(0);
            }

            counters[level]++;
            counters.RemoveRange(level + 1, counters.Count - level - 1);
            numbers[activity.Id] = string.Join('.', counters.Take(level + 1));
        }

        return numbers;
    }

    private static string Stamp(DateOnly date, int hour) => $"{date:yyyy-MM-dd}T{hour:00}:00:00";

    private static string Duration(int days) => $"PT{(long)days * MinutesPerDay / 60}H0M0S";

    private static int Percent(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
