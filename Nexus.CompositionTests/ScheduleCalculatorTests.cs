using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class ScheduleCalculatorTests
{
    // 2026-03-02 is a Monday.
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly Today = new(2026, 1, 1);

    private sealed class WeekdaysCalendar(params DateOnly[] holidays) : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) =>
            date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);
    }

    private sealed class NeverWorkingCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => false;
    }

    /// <summary>Names double as handles, so each test reads like the plan it describes.</summary>
    private sealed class Plan
    {
        private readonly Dictionary<string, Guid> _ids = [];
        public List<ScheduleActivityInput> Activities { get; } = [];
        public List<ScheduleLinkInput> Links { get; } = [];

        public Guid Id(string name) => _ids[name];

        public Plan Task(string name, int? days, string? parent = null, DateOnly? start = null, DateOnly? end = null,
            decimal weight = 0, decimal planned = 0, decimal actual = 0)
        {
            var id = _ids[name] = Guid.NewGuid();
            Activities.Add(new ScheduleActivityInput(id, parent is null ? null : _ids[parent], name, false, days, start, end, weight, planned, actual));
            return this;
        }

        public Plan Milestone(string name, string? parent = null, DateOnly? at = null)
        {
            var id = _ids[name] = Guid.NewGuid();
            Activities.Add(new ScheduleActivityInput(id, parent is null ? null : _ids[parent], name, true, null, at, at, 0, 0, 0));
            return this;
        }

        public Plan Link(string predecessor, string successor, DependencyType type = DependencyType.FinishToStart, int lag = 0)
        {
            Links.Add(new ScheduleLinkInput(_ids[predecessor], _ids[successor], type, lag));
            return this;
        }

        public ScheduleResult Run(IWorkingDayCalendar? calendar = null, DateOnly? projectStart = null)
        {
            var result = ScheduleCalculator.Compute(Activities, Links, calendar ?? AllDaysCalendar.Instance, projectStart ?? Monday, Today);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            return result.Value!;
        }

        public ScheduledActivity Of(ScheduleResult result, string name) => result.Activities.Single(a => a.Id == _ids[name]);
    }

    private static DateOnly Day(int offsetFromMonday) => Monday.AddDays(offsetFromMonday);

    // ------------------------------------------------------- dependency types

    [Fact]
    public void FinishToStart_Diamond_GivesTheClassicDatesFloatsAndCriticalPath()
    {
        var plan = new Plan()
            .Task("A", 3).Task("B", 2).Task("C", 4).Task("D", 1)
            .Link("A", "B").Link("A", "C").Link("B", "D").Link("C", "D");

        var result = plan.Run();

        Assert.Equal((Day(0), Day(2)), (plan.Of(result, "A").Start, plan.Of(result, "A").Finish));
        Assert.Equal((Day(3), Day(4)), (plan.Of(result, "B").Start, plan.Of(result, "B").Finish));
        Assert.Equal((Day(3), Day(6)), (plan.Of(result, "C").Start, plan.Of(result, "C").Finish));
        Assert.Equal((Day(7), Day(7)), (plan.Of(result, "D").Start, plan.Of(result, "D").Finish));

        // B has two spare days, the longest chain A -> C -> D has none.
        Assert.Equal(2, plan.Of(result, "B").TotalFloatDays);
        Assert.False(plan.Of(result, "B").IsCritical);
        Assert.Equal(new[] { plan.Id("A"), plan.Id("C"), plan.Id("D") }, result.CriticalPath);
        Assert.Equal((Day(0), Day(7), 8), (result.ProjectStart, result.ProjectFinish, result.ProjectDurationDays));
        Assert.Equal((Day(5), Day(6)), (plan.Of(result, "B").LateStart, plan.Of(result, "B").LateFinish));
    }

    [Fact]
    public void StartToStart_WithLag_StartsTheSuccessorThatManyDaysAfterThePredecessorStarted()
    {
        var plan = new Plan().Task("A", 4).Task("B", 3).Link("A", "B", DependencyType.StartToStart, lag: 2);

        var result = plan.Run();

        Assert.Equal(Day(2), plan.Of(result, "B").Start);
        Assert.Equal(Day(4), plan.Of(result, "B").Finish);
        // B's start is tied to A's, so delaying A would delay the project: both are critical.
        Assert.True(plan.Of(result, "A").IsCritical);
        Assert.True(plan.Of(result, "B").IsCritical);
    }

    [Fact]
    public void FinishToFinish_MakesTheSuccessorEndWhenThePredecessorEnds()
    {
        var plan = new Plan().Task("A", 5).Task("B", 2).Link("A", "B", DependencyType.FinishToFinish);

        var result = plan.Run();

        Assert.Equal(Day(4), plan.Of(result, "A").Finish);
        Assert.Equal((Day(3), Day(4)), (plan.Of(result, "B").Start, plan.Of(result, "B").Finish));
    }

    [Fact]
    public void StartToFinish_MakesTheSuccessorEndWhenThePredecessorStarts()
    {
        // A is held back to day 10 by its own start date; B must finish when A starts.
        var plan = new Plan().Task("A", 3, start: Day(10)).Task("B", 2).Link("A", "B", DependencyType.StartToFinish);

        var result = plan.Run(projectStart: Monday);

        Assert.Equal(Day(10), plan.Of(result, "A").Start);
        Assert.Equal((Day(8), Day(9)), (plan.Of(result, "B").Start, plan.Of(result, "B").Finish));
    }

    [Fact]
    public void ANegativeLag_LetsTheSuccessorOverlapThePredecessor()
    {
        var plan = new Plan().Task("A", 5).Task("B", 3).Link("A", "B", DependencyType.FinishToStart, lag: -2);

        var result = plan.Run();

        Assert.Equal(Day(3), plan.Of(result, "B").Start);
        Assert.Equal(Day(5), plan.Of(result, "B").Finish);
    }

    [Fact]
    public void ALeadLongerThanTheWorkBeforeIt_NeverPutsAnActivityBeforeTheStartOfThePlan()
    {
        var plan = new Plan().Task("A", 2).Task("B", 3).Link("A", "B", DependencyType.FinishToStart, lag: -10);

        var result = plan.Run();

        Assert.Equal(Day(0), plan.Of(result, "B").Start);
    }

    [Fact]
    public void AMultiplePredecessors_TheLatestConstraintWins()
    {
        var plan = new Plan().Task("A", 2).Task("B", 6).Task("C", 1)
            .Link("A", "C").Link("B", "C", DependencyType.FinishToStart, lag: 1);

        var result = plan.Run();

        Assert.Equal(Day(7), plan.Of(result, "C").Start); // B ends on day 5, +1 lag => day 7
    }

    // ------------------------------------------------------------ calendars

    [Fact]
    public void Weekends_AreSkipped_AndDurationsCountWorkingDaysOnly()
    {
        var plan = new Plan().Task("A", 3).Task("B", 4).Link("A", "B");

        var result = plan.Run(new WeekdaysCalendar());

        Assert.Equal((Day(0), Day(2)), (plan.Of(result, "A").Start, plan.Of(result, "A").Finish)); // Mon-Wed
        // B: Thu, Fri, then Mon, Tue.
        Assert.Equal((Day(3), Day(8)), (plan.Of(result, "B").Start, plan.Of(result, "B").Finish));
        Assert.Equal(DayOfWeek.Tuesday, plan.Of(result, "B").Finish.DayOfWeek);
        Assert.Equal(7, result.ProjectDurationDays);
    }

    [Fact]
    public void AHoliday_PushesWorkOverIt()
    {
        var plan = new Plan().Task("A", 3);

        var result = plan.Run(new WeekdaysCalendar(Day(2))); // Wednesday off

        Assert.Equal((Day(0), Day(3)), (plan.Of(result, "A").Start, plan.Of(result, "A").Finish)); // Mon, Tue, Thu
    }

    [Fact]
    public void AnActivityAnchoredOnAWeekend_StartsOnTheNextWorkingDay()
    {
        var plan = new Plan().Task("A", 2, start: Day(5)); // Saturday

        var result = plan.Run(new WeekdaysCalendar());

        Assert.Equal((Day(7), Day(8)), (plan.Of(result, "A").Start, plan.Of(result, "A").Finish));
    }

    [Fact]
    public void ADurationDerivedFromDates_CountsWorkingDays()
    {
        // Friday to the following Tuesday: Fri, Mon, Tue.
        var plan = new Plan().Task("A", null, start: Day(4), end: Day(8));

        var result = plan.Run(new WeekdaysCalendar());

        Assert.Equal(3, plan.Of(result, "A").DurationDays);
        Assert.False(plan.Of(result, "A").UsedDefaultDuration);
    }

    [Fact]
    public void AnActivityWithNoDurationAndNoDates_GetsOneDay_AndIsFlagged()
    {
        var plan = new Plan().Task("A", null);

        var a = plan.Of(plan.Run(), "A");

        Assert.Equal(1, a.DurationDays);
        Assert.True(a.UsedDefaultDuration);
    }

    // ----------------------------------------------------------- milestones

    [Fact]
    public void AMilestone_SitsOnTheDayThePrecedingWorkFinishes_AndSuccessorsStartAfterIt()
    {
        var plan = new Plan().Task("A", 3).Milestone("Gate").Task("B", 2)
            .Link("A", "Gate").Link("Gate", "B");

        var result = plan.Run(new WeekdaysCalendar());

        Assert.Equal(Day(2), plan.Of(result, "Gate").Start); // Wednesday, the day A finishes
        Assert.Equal(Day(2), plan.Of(result, "Gate").Finish);
        Assert.Equal(0, plan.Of(result, "Gate").DurationDays);
        Assert.True(plan.Of(result, "Gate").IsMilestone);
        Assert.Equal(Day(3), plan.Of(result, "B").Start);
    }

    [Fact]
    public void AnAnchoredMilestone_KeepsItsOwnDate_AndOneWithoutADateSitsOnTheProjectStart()
    {
        var plan = new Plan().Milestone("Kick-off").Milestone("Contract", at: Day(4)); // Friday

        var result = plan.Run(new WeekdaysCalendar());

        Assert.Equal(Day(0), plan.Of(result, "Kick-off").Start);
        Assert.Equal(Day(4), plan.Of(result, "Contract").Start);
    }

    // ------------------------------------------------------------- anchoring

    [Fact]
    public void RootsStartOnTheirOwnDate_OrTheProjectStart_ButSuccessorsIgnoreTheirOwnStoredDates()
    {
        var plan = new Plan()
            .Task("Root", 2)
            .Task("Anchored", 2, start: Day(5))
            .Task("Follower", 2, start: Day(40), end: Day(41)) // stale dates from an earlier schedule
            .Link("Root", "Follower");

        var result = plan.Run();

        Assert.Equal(Day(0), plan.Of(result, "Root").Start);
        Assert.Equal(Day(5), plan.Of(result, "Anchored").Start);
        Assert.Equal(Day(2), plan.Of(result, "Follower").Start); // driven by Root, not by its stored date
    }

    [Fact]
    public void Recalculating_AfterAPredecessorShrinks_PullsTheSuccessorEarlier()
    {
        var before = new Plan().Task("A", 5).Task("B", 2).Link("A", "B");
        Assert.Equal(Day(5), before.Of(before.Run(), "B").Start);

        var after = new Plan().Task("A", 3).Task("B", 2).Link("A", "B");
        Assert.Equal(Day(3), after.Of(after.Run(), "B").Start);
    }

    [Fact]
    public void FloatIsMeasuredAgainstTheProjectFinish_SoALateAnchoredParallelChainHasNone()
    {
        // Long runs days 0-12 (13 days). Short is anchored on day 5 and runs days 5-7, so the
        // project finishes 5 working days after Short does.
        var plan = new Plan().Task("Long", 13).Task("Short", 3, start: Day(5));

        var result = plan.Run();

        Assert.True(plan.Of(result, "Long").IsCritical);
        Assert.Equal(5, plan.Of(result, "Short").TotalFloatDays);
        Assert.Equal((Day(10), Day(12)), (plan.Of(result, "Short").LateStart, plan.Of(result, "Short").LateFinish));
    }

    // ------------------------------------------------------------- summaries

    [Fact]
    public void ASummary_SpansItsChildren_AndInheritsTheirFloatAndCriticality()
    {
        var plan = new Plan()
            .Task("Phase", null)
            .Task("A", 3, parent: "Phase").Task("B", 2, parent: "Phase")
            .Task("Other", 10)
            .Link("A", "B");

        var result = plan.Run();

        var phase = plan.Of(result, "Phase");
        Assert.True(phase.IsSummary);
        Assert.Equal((Day(0), Day(4), 5), (phase.Start, phase.Finish, phase.DurationDays));
        // The 10-day task sets the project end, so the whole phase has 5 days of float.
        Assert.Equal(5, phase.TotalFloatDays);
        Assert.False(phase.IsCritical);
        Assert.DoesNotContain(plan.Id("Phase"), result.CriticalPath);
    }

    [Fact]
    public void ASummary_IsCritical_WhenAnyChildIs_ButIsNotListedOnThePath()
    {
        var plan = new Plan().Task("Phase", null).Task("A", 5, parent: "Phase");

        var result = plan.Run();

        Assert.True(plan.Of(result, "Phase").IsCritical);
        Assert.Equal(new[] { plan.Id("A") }, result.CriticalPath);
    }

    [Fact]
    public void Nested_Summaries_Work_ThroughSeveralLevels()
    {
        var plan = new Plan()
            .Task("Top", null).Task("Mid", null, parent: "Top")
            .Task("Leaf1", 2, parent: "Mid").Task("Leaf2", 3, parent: "Mid").Link("Leaf1", "Leaf2");

        var result = plan.Run();

        Assert.Equal((Day(0), Day(4)), (plan.Of(result, "Top").Start, plan.Of(result, "Top").Finish));
        Assert.Equal((Day(0), Day(4)), (plan.Of(result, "Mid").Start, plan.Of(result, "Mid").Finish));
    }

    // -------------------------------------------------------------- progress

    [Fact]
    public void Progress_RollsUpByStatedWeights()
    {
        var plan = new Plan().Task("Phase", null, weight: 100)
            .Task("A", 1, parent: "Phase", weight: 30, planned: 100, actual: 100)
            .Task("B", 1, parent: "Phase", weight: 70, planned: 100, actual: 0);

        var phase = plan.Of(plan.Run(), "Phase");

        Assert.Equal(100m, phase.PlannedProgress);
        Assert.Equal(30m, phase.ActualProgress); // 0.3 * 100 + 0.7 * 0
    }

    [Fact]
    public void Progress_FallsBackToDuration_WhenNoWeightsAreGiven()
    {
        var plan = new Plan().Task("Phase", null)
            .Task("Short", 1, parent: "Phase", actual: 100)
            .Task("Long", 3, parent: "Phase", actual: 0);

        Assert.Equal(25m, plan.Of(plan.Run(), "Phase").ActualProgress); // 1 of 4 days done
    }

    [Fact]
    public void Progress_FallsBackToEqualWeights_WhenThereIsNoWeightAndNoDuration()
    {
        var plan = new Plan().Task("Phase", null)
            .Milestone("M1", parent: "Phase").Milestone("M2", parent: "Phase");

        var phase = plan.Of(plan.Run(), "Phase");

        Assert.Equal(0m, phase.ActualProgress); // no division by zero
    }

    [Fact]
    public void ProjectProgress_RollsUpTheTopLevelActivities()
    {
        var plan = new Plan()
            .Task("A", 2, weight: 50, planned: 100, actual: 60)
            .Task("B", 2, weight: 50, planned: 50, actual: 20);

        var result = plan.Run();

        Assert.Equal(75m, result.PlannedProgress);
        Assert.Equal(40m, result.ActualProgress);
    }

    // ------------------------------------------------------ robustness/errors

    [Fact]
    public void ACycleInTheLinks_IsReported_NotLoopedOn()
    {
        var plan = new Plan().Task("A", 1).Task("B", 1).Link("A", "B").Link("B", "A");

        var result = ScheduleCalculator.Compute(plan.Activities, plan.Links, AllDaysCalendar.Instance, Monday, Today);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void ACalendarWithNoWorkingDays_IsReported()
    {
        var plan = new Plan().Task("A", 1);

        var result = ScheduleCalculator.Compute(plan.Activities, plan.Links, new NeverWorkingCalendar(), Monday, Today);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.error", result.Error.Code);
    }

    [Fact]
    public void AnAbsurdDuration_IsRefused()
    {
        var plan = new Plan().Task("A", ScheduleCalculator.MaxDurationDays + 1);

        var result = ScheduleCalculator.Compute(plan.Activities, plan.Links, AllDaysCalendar.Instance, Monday, Today);

        Assert.Equal("validation.error", result.Error.Code);
    }

    [Fact]
    public void LinksToUnknownOrSummaryActivities_AreIgnored()
    {
        var plan = new Plan().Task("Phase", null).Task("A", 2, parent: "Phase").Task("B", 2);
        plan.Links.Add(new ScheduleLinkInput(plan.Id("Phase"), plan.Id("B"), DependencyType.FinishToStart, 0)); // summary end
        plan.Links.Add(new ScheduleLinkInput(Guid.NewGuid(), plan.Id("B"), DependencyType.FinishToStart, 0)); // unknown

        var result = plan.Run();

        Assert.Equal(Day(0), plan.Of(result, "B").Start);
    }

    [Fact]
    public void ALoopInStoredParentLinks_DoesNotCrash_AndEveryActivityIsStillScheduled()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var activities = new[]
        {
            new ScheduleActivityInput(a, b, "A", false, 2, null, null, 0, 0, 0),
            new ScheduleActivityInput(b, a, "B", false, 3, null, null, 0, 0, 0)
        };

        var result = ScheduleCalculator.Compute(activities, [], AllDaysCalendar.Instance, Monday, Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Activities.Count);
    }

    [Fact]
    public void ParentIdsPointingNowhere_MakeTheActivityTopLevel()
    {
        var activities = new[] { new ScheduleActivityInput(Guid.NewGuid(), Guid.NewGuid(), "Orphan", false, 2, null, null, 0, 0, 0) };

        var result = ScheduleCalculator.Compute(activities, [], AllDaysCalendar.Instance, Monday, Today);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Activities.Single().ParentId);
    }

    [Fact]
    public void NoActivities_ProducesAnEmptySchedule_OnTheProjectStart()
    {
        var result = new Plan().Run();

        Assert.Empty(result.Activities);
        Assert.Empty(result.CriticalPath);
        Assert.Equal((Monday, Monday, 0), (result.ProjectStart, result.ProjectFinish, 0));
    }

    [Fact]
    public void WithNoProjectStartAndNoDates_TheScheduleBeginsOnTheGivenToday()
    {
        var plan = new Plan().Task("A", 2);

        var result = ScheduleCalculator.Compute(plan.Activities, plan.Links, AllDaysCalendar.Instance, projectStart: null, Today);

        Assert.Equal(Today, result.Value!.ProjectStart);
    }

    [Fact]
    public void TheResult_IsOrderedByStart_AndASummaryComesWithItsFirstChild()
    {
        var plan = new Plan().Task("Late", 1, start: Day(5)).Task("Early", 1);

        var result = plan.Run();

        Assert.Equal(["Early", "Late"], result.Activities.Select(a => a.Name));
    }

    // ----------------------------------------------------------- the axis

    [Fact]
    public void TheAxis_MapsDatesAndIndexesBothWays_AcrossWeekends()
    {
        var axis = new WorkingDayAxis(new WeekdaysCalendar(), Day(5)); // base is a Saturday

        Assert.Equal(Day(7), axis.DateAt(0)); // first working day: Monday
        Assert.Equal(Day(11), axis.DateAt(4)); // Friday
        Assert.Equal(Day(14), axis.DateAt(5)); // next Monday
        Assert.Equal(4, axis.IndexOf(Day(11)));
        Assert.Equal(5, axis.IndexOf(Day(12))); // a Saturday -> the next working day
        Assert.Equal(0, axis.IndexOf(Day(0))); // before the axis -> 0
        Assert.Equal(3, axis.WorkingDaysBetween(Day(7), Day(9)));
        Assert.Equal(2, axis.WorkingDaysBetween(Day(11), Day(14))); // Fri + Mon
        Assert.Equal(0, axis.WorkingDaysBetween(Day(9), Day(7)));

        // Dates before the axis begins have no working days behind them.
        Assert.Equal(0, axis.WorkingDaysThrough(Day(0)));
        Assert.Equal(0, axis.WorkingDaysThrough(Day(6))); // the Sunday just before the first working day
        Assert.Equal(1, axis.WorkingDaysThrough(Day(7)));
        Assert.Equal(5, axis.WorkingDaysThrough(Day(12))); // Saturday after the first week: still 5
        Assert.Equal(0, axis.WorkingDaysBetween(Day(0), Day(6)));
    }
}
