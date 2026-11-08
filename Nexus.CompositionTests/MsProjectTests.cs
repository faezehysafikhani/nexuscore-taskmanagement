using System.Text;
using System.Xml.Linq;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.MsProject;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.CompositionTests;

public sealed class MsProjectTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    /// <summary>
    /// A small plan written the way MS Project writes it: namespace, a UID 0 project-summary row,
    /// a blank row, outline levels, a milestone, ISO durations in hours (8 h = 1 day), percent
    /// complete and every link type, with lags in tenths of a minute (4800 = one 8-hour day).
    /// </summary>
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Project xmlns="http://schemas.microsoft.com/project">
          <SaveVersion>14</SaveVersion>
          <Name>Tower.xml</Name>
          <Title>Tower</Title>
          <StartDate>2026-03-02T08:00:00</StartDate>
          <MinutesPerDay>480</MinutesPerDay>
          <Tasks>
            <Task><UID>0</UID><ID>0</ID><Name>Tower</Name><OutlineLevel>0</OutlineLevel><Summary>1</Summary></Task>
            <Task><UID>1</UID><ID>1</ID><Name>Foundations</Name><OutlineLevel>1</OutlineLevel><Summary>1</Summary>
              <Start>2026-03-02T08:00:00</Start><Finish>2026-03-10T17:00:00</Finish><Duration>PT56H0M0S</Duration></Task>
            <Task><UID>2</UID><ID>2</ID><Name>Excavation</Name><OutlineLevel>2</OutlineLevel><Summary>0</Summary>
              <Start>2026-03-02T08:00:00</Start><Finish>2026-03-04T17:00:00</Finish><Duration>PT24H0M0S</Duration><PercentComplete>50</PercentComplete></Task>
            <Task><UID>3</UID><ID>3</ID><Name>Concrete</Name><OutlineLevel>2</OutlineLevel><Summary>0</Summary>
              <Start>2026-03-05T08:00:00</Start><Finish>2026-03-10T17:00:00</Finish><Duration>PT32H0M0S</Duration>
              <PredecessorLink><PredecessorUID>2</PredecessorUID><Type>1</Type><CrossProject>0</CrossProject><LinkLag>4800</LinkLag><LagFormat>7</LagFormat></PredecessorLink>
            </Task>
            <Task><UID>4</UID><ID>4</ID><IsNull>1</IsNull></Task>
            <Task><UID>5</UID><ID>5</ID><Name>Foundations done</Name><OutlineLevel>1</OutlineLevel><Milestone>1</Milestone>
              <Start>2026-03-10T17:00:00</Start><Finish>2026-03-10T17:00:00</Finish><Duration>PT0H0M0S</Duration>
              <PredecessorLink><PredecessorUID>3</PredecessorUID><Type>1</Type></PredecessorLink>
            </Task>
            <Task><UID>6</UID><ID>6</ID><Name>Walls</Name><OutlineLevel>1</OutlineLevel><Duration>PT40H0M0S</Duration>
              <PredecessorLink><PredecessorUID>5</PredecessorUID><Type>3</Type><LinkLag>-4800</LinkLag></PredecessorLink>
              <PredecessorLink><PredecessorUID>2</PredecessorUID><Type>0</Type></PredecessorLink>
              <PredecessorLink><PredecessorUID>3</PredecessorUID><Type>2</Type></PredecessorLink>
            </Task>
          </Tasks>
        </Project>
        """;

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    // ---------------------------------------------------------------- reading

    [Fact]
    public void TheReader_ParsesATypicalMsProjectFile()
    {
        var plan = MsProjectXmlReader.Read(Stream(Sample)).Value!;

        Assert.Equal(("Tower", new DateOnly(2026, 3, 2), 480), (plan.Name, plan.Start, plan.MinutesPerDay));
        // The UID 0 summary row and the blank (IsNull) row are not tasks.
        Assert.Equal([1, 2, 3, 5, 6], plan.Tasks.Select(t => t.Uid));

        var excavation = plan.Tasks.Single(t => t.Uid == 2);
        Assert.Equal(("Excavation", 2, 1440, 50m), (excavation.Name, excavation.OutlineLevel, excavation.DurationMinutes, excavation.PercentComplete));
        Assert.Equal((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)), (excavation.Start, excavation.Finish));
        Assert.True(plan.Tasks.Single(t => t.Uid == 1).IsSummary);
        Assert.True(plan.Tasks.Single(t => t.Uid == 5).IsMilestone);
    }

    [Fact]
    public void TheReader_MapsEveryLinkType_AndConvertsLagsFromTenthsOfAMinute()
    {
        var plan = MsProjectXmlReader.Read(Stream(Sample)).Value!;

        var concrete = Assert.Single(plan.Tasks.Single(t => t.Uid == 3).Links);
        Assert.Equal((2, DependencyType.FinishToStart, 480), (concrete.PredecessorUid, concrete.Type, concrete.LagMinutes));

        var walls = plan.Tasks.Single(t => t.Uid == 6).Links;
        Assert.Equal(
            [(5, DependencyType.StartToStart, -480), (2, DependencyType.FinishToFinish, 0), (3, DependencyType.StartToFinish, 0)],
            walls.Select(l => (l.PredecessorUid, l.Type, l.LagMinutes)));

        // No Type element means finish-to-start.
        Assert.Equal(DependencyType.FinishToStart, plan.Tasks.Single(t => t.Uid == 5).Links.Single().Type);
    }

    [Fact]
    public void TheReader_AlsoReadsFilesWithoutTheNamespace()
    {
        var plan = MsProjectXmlReader.Read(Stream(Sample.Replace(" xmlns=\"http://schemas.microsoft.com/project\"", ""))).Value!;

        Assert.Equal(5, plan.Tasks.Count);
    }

    [Theory]
    [InlineData("PT8H0M0S", 480)]
    [InlineData("PT40H0M0S", 2400)]
    [InlineData("PT0H0M0S", 0)]
    [InlineData("PT4H30M0S", 270)]
    [InlineData("P2DT0H0M0S", 960)]
    [InlineData("PT90S", 2)]
    public void Durations_AreConvertedToMinutes(string iso, int minutes)
    {
        Assert.Equal(minutes, MsProjectXmlReader.ParseDurationMinutes(iso, 480));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eight hours")]
    [InlineData("8H")]
    public void AnUnreadableDuration_IsNull(string? iso)
    {
        Assert.Null(MsProjectXmlReader.ParseDurationMinutes(iso, 480));
    }

    [Fact]
    public void TheReader_RefusesNonXml_WrongRoots_AndEmptyPlans()
    {
        Assert.Equal("validation.error", MsProjectXmlReader.Read(Stream("this is not xml")).Error.Code);
        Assert.Equal("validation.error", MsProjectXmlReader.Read(Stream("<Plan><Tasks/></Plan>")).Error.Code);
        Assert.Equal("validation.error", MsProjectXmlReader.Read(Stream("<Project><Tasks/></Project>")).Error.Code);
        Assert.Equal("validation.error", MsProjectXmlReader.Read(Stream("<Project><Tasks><Task><UID>0</UID></Task></Tasks></Project>")).Error.Code);
    }

    [Fact]
    public void TheReader_RefusesFilesWithTooManyTasks()
    {
        var rows = string.Concat(Enumerable.Range(1, MsProjectXmlReader.MaxTasks + 2).Select(i => $"<Task><UID>{i}</UID><Name>t</Name></Task>"));

        Assert.Equal("validation.error", MsProjectXmlReader.Read(Stream($"<Project><Tasks>{rows}</Tasks></Project>")).Error.Code);
    }

    [Fact]
    public void TheReader_DoesNotExpandEntitiesOrLoadExternalFiles()
    {
        // A billion-laughs style bomb and an external entity: both must be rejected, not evaluated.
        const string bomb = """
            <?xml version="1.0"?>
            <!DOCTYPE Project [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <Project><Tasks><Task><UID>1</UID><Name>&b;</Name></Task></Tasks></Project>
            """;
        const string external = """
            <?xml version="1.0"?>
            <!DOCTYPE Project [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <Project><Tasks><Task><UID>1</UID><Name>&secret;</Name></Task></Tasks></Project>
            """;

        Assert.True(MsProjectXmlReader.Read(Stream(bomb)).IsFailure);
        var result = MsProjectXmlReader.Read(Stream(external));
        Assert.True(result.IsFailure);
        Assert.Equal("validation.error", result.Error.Code);
    }

    // -------------------------------------------------------------- importing

    private sealed record Fixture(
        MsProjectService Service, ScheduleService Schedule, FakeActivityRepository Activities,
        FakeDependencyRepository Dependencies, Project Project, IWorkingDayCalendar? Calendar);

    private sealed class Weekdays : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && date != new DateOnly(2026, 3, 11);
    }

    private static Fixture Build(IWorkingDayCalendar? calendar = null, string name = "Tower", string code = "TWR")
    {
        var project = new Project(Guid.NewGuid(), Tenant, name, code, ProjectType.Waterfall);
        project.UpdateDetails(name, code, null, null, null, calendar is null ? null : Guid.NewGuid(), Monday, null, null, null, null, null, null, null, null);
        var activities = new FakeActivityRepository();
        var dependencies = new FakeDependencyRepository();
        var unitOfWork = new FakeWaterfallUnitOfWork();
        var projects = new FakeProjectRepository(project);
        var schedule = new ScheduleService(activities, dependencies, projects, new FakeCalendarProvider(calendar), unitOfWork,
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        return new Fixture(new MsProjectService(activities, dependencies, projects, schedule, unitOfWork), schedule, activities, dependencies, project, calendar);
    }

    private static Activity ByName(Fixture f, string name) => f.Activities.Items.Single(a => a.Name == name);

    [Fact]
    public async Task Import_BuildsTheTreeDurationsMilestonesProgressAndLinks()
    {
        var f = Build();

        var result = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), replaceExisting: false, default);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal((5, 5, 0), (result.Value!.ActivitiesImported, result.Value.DependenciesImported, result.Value.ActivitiesReplaced));

        var foundations = ByName(f, "Foundations");
        Assert.Null(foundations.ParentActivityId);
        Assert.Null(foundations.DurationDays); // a summary has no duration of its own
        Assert.Equal(foundations.Id, ByName(f, "Excavation").ParentActivityId);
        Assert.Equal(foundations.Id, ByName(f, "Concrete").ParentActivityId);
        Assert.Null(ByName(f, "Walls").ParentActivityId);

        Assert.Equal(3, ByName(f, "Excavation").DurationDays);
        Assert.Equal(4, ByName(f, "Concrete").DurationDays);
        Assert.Equal(50m, ByName(f, "Excavation").ActualProgress);

        var gate = ByName(f, "Foundations done");
        Assert.True(gate.IsMilestone);
        Assert.Equal(0, gate.DurationDays);

        Assert.All(f.Activities.Items, a => Assert.Equal((Tenant, f.Project.Id), (a.TenantId, a.ProjectId)));
    }

    [Fact]
    public async Task Import_CreatesTheLinks_WithTheirTypesAndLagsInWorkingDays()
    {
        var f = Build();
        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);

        (DependencyType Type, int Lag) Of(string predecessor, string successor)
        {
            var link = f.Dependencies.Items.Single(d =>
                d.PredecessorActivityId == ByName(f, predecessor).Id && d.SuccessorActivityId == ByName(f, successor).Id);
            return (link.Type, link.LagDays);
        }

        Assert.Equal((DependencyType.FinishToStart, 1), Of("Excavation", "Concrete")); // 480 min = 1 day
        Assert.Equal((DependencyType.FinishToStart, 0), Of("Concrete", "Foundations done"));
        Assert.Equal((DependencyType.StartToStart, -1), Of("Foundations done", "Walls"));
        Assert.Equal((DependencyType.FinishToFinish, 0), Of("Excavation", "Walls"));
        Assert.Equal((DependencyType.StartToFinish, 0), Of("Concrete", "Walls"));
    }

    [Fact]
    public async Task Import_ProducesAScheduleThatTheEngineCanCalculate()
    {
        var f = Build();
        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);

        var schedule = await f.Schedule.GetScheduleAsync(f.Project.Id, default);

        Assert.True(schedule.IsSuccess, schedule.IsFailure ? schedule.Error.Message : null);
        // Excavation 3 days (Mar 2-4), +1 lag, Concrete 4 days from Mar 6: ends Mar 9.
        var concrete = schedule.Value!.Activities.Single(a => a.Name == "Concrete");
        Assert.Equal((new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 9)), (concrete.Start, concrete.Finish));
    }

    [Fact]
    public async Task Import_IntoAProjectWithActivities_IsRefused_UnlessReplaceIsAskedFor()
    {
        var f = Build();
        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);
        var before = f.Activities.Items.Select(a => a.Id).ToList();

        var refused = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), replaceExisting: false, default);

        Assert.Equal("conflict", refused.Error.Code);
        Assert.Equal(before, f.Activities.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task Replace_DeletesTheOldActivitiesAndLinks_ThenImports()
    {
        var f = Build();
        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);
        var oldIds = f.Activities.Items.Select(a => a.Id).ToHashSet();

        var replaced = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), replaceExisting: true, default);

        Assert.Equal((5, 5, 5), (replaced.Value!.ActivitiesImported, replaced.Value.DependenciesImported, replaced.Value.ActivitiesReplaced));
        Assert.Equal(5, f.Activities.Items.Count);
        Assert.Equal(5, f.Dependencies.Items.Count);
        Assert.DoesNotContain(f.Activities.Items, a => oldIds.Contains(a.Id));
    }

    [Fact]
    public async Task ABadFile_NeverCostsTheExistingPlan_EvenWithReplace()
    {
        var f = Build();
        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);
        var before = f.Activities.Items.Select(a => a.Id).ToList();

        var result = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream("<Project><Tasks/></Project>"), replaceExisting: true, default);

        Assert.True(result.IsFailure);
        Assert.Equal(before, f.Activities.Items.Select(a => a.Id));
        Assert.Equal(5, f.Dependencies.Items.Count);
    }

    [Fact]
    public async Task Import_ReportsAnUnknownProject()
    {
        var f = Build();

        Assert.Equal("not_found", (await f.Service.ImportAsync(Tenant, Guid.NewGuid(), Stream(Sample), false, default)).Error.Code);
    }

    [Fact]
    public async Task Import_SkipsLinksItCannotKeep_AndSaysSo()
    {
        const string xml = """
            <Project><MinutesPerDay>480</MinutesPerDay><Tasks>
              <Task><UID>1</UID><Name>Phase</Name><OutlineLevel>1</OutlineLevel></Task>
              <Task><UID>2</UID><Name>A</Name><OutlineLevel>2</OutlineLevel><Duration>PT8H0M0S</Duration></Task>
              <Task><UID>3</UID><Name>B</Name><OutlineLevel>2</OutlineLevel><Duration>PT8H0M0S</Duration>
                <PredecessorLink><PredecessorUID>2</PredecessorUID><Type>1</Type></PredecessorLink>
                <PredecessorLink><PredecessorUID>2</PredecessorUID><Type>3</Type></PredecessorLink>
                <PredecessorLink><PredecessorUID>99</PredecessorUID></PredecessorLink>
                <PredecessorLink><PredecessorUID>1</PredecessorUID></PredecessorLink>
              </Task>
              <Task><UID>4</UID><Name>C</Name><OutlineLevel>1</OutlineLevel><Duration>PT8H0M0S</Duration>
                <PredecessorLink><PredecessorUID>3</PredecessorUID></PredecessorLink>
                <PredecessorLink><PredecessorUID>2</PredecessorUID><LinkLag>99999999</LinkLag></PredecessorLink>
              </Task>
              <Task><UID>5</UID><Name>D</Name><OutlineLevel>1</OutlineLevel><Duration>PT8H0M0S</Duration>
                <PredecessorLink><PredecessorUID>5</PredecessorUID></PredecessorLink>
              </Task>
            </Tasks></Project>
            """;
        var f = Build();

        var result = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(xml), false, default);

        Assert.True(result.IsSuccess);
        // A->B (kept), B->C (kept), A->C with a huge lag (kept, limited to 365 days).
        Assert.Equal(3, result.Value!.DependenciesImported);
        var huge = f.Dependencies.Items.Single(d => d.PredecessorActivityId == ByName(f, "A").Id && d.SuccessorActivityId == ByName(f, "C").Id);
        Assert.Equal(365, huge.LagDays);

        var warnings = string.Join("\n", result.Value.Warnings);
        Assert.Contains("duplicate link", warnings);
        Assert.Contains("not in the file", warnings);
        Assert.Contains("summary tasks", warnings);
        Assert.Contains("limited to 365", warnings);
        Assert.Contains("circular", warnings); // D linked to itself
    }

    [Fact]
    public async Task Import_SkipsLinksThatWouldFormACycle()
    {
        const string xml = """
            <Project><Tasks>
              <Task><UID>1</UID><Name>A</Name><Duration>PT8H0M0S</Duration><PredecessorLink><PredecessorUID>2</PredecessorUID></PredecessorLink></Task>
              <Task><UID>2</UID><Name>B</Name><Duration>PT8H0M0S</Duration><PredecessorLink><PredecessorUID>1</PredecessorUID></PredecessorLink></Task>
            </Tasks></Project>
            """;
        var f = Build();

        var result = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(xml), false, default);

        Assert.Equal(1, result.Value!.DependenciesImported);
        Assert.Contains("circular", string.Join(" ", result.Value.Warnings));
        Assert.True((await f.Schedule.GetScheduleAsync(f.Project.Id, default)).IsSuccess);
    }

    [Fact]
    public async Task Import_CopesWithOddOutlinesNamesAndRepeatedIds()
    {
        var longName = new string('x', 300);
        var xml = $"""
            <Project><Tasks>
              <Task><UID>1</UID><Name>Top</Name><OutlineLevel>1</OutlineLevel></Task>
              <Task><UID>2</UID><Name>Skipped a level</Name><OutlineLevel>5</OutlineLevel><Duration>PT8H0M0S</Duration></Task>
              <Task><UID>3</UID><Name></Name><OutlineLevel>1</OutlineLevel><Duration>PT8H0M0S</Duration></Task>
              <Task><UID>3</UID><Name>Same id again</Name><OutlineLevel>1</OutlineLevel></Task>
              <Task><UID>4</UID><Name>{longName}</Name><OutlineLevel>1</OutlineLevel><Duration>PT1H0M0S</Duration></Task>
            </Tasks></Project>
            """;
        var f = Build();

        var result = await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(xml), false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.ActivitiesImported);
        Assert.Equal(ByName(f, "Top").Id, ByName(f, "Skipped a level").ParentActivityId); // a jump in level is one level deeper
        Assert.Contains(f.Activities.Items, a => a.Name == "(unnamed)");
        Assert.Equal(200, f.Activities.Items.Single(a => a.Name.StartsWith('x')).Name.Length);
        Assert.Equal(1, f.Activities.Items.Single(a => a.Name.StartsWith('x')).DurationDays); // an hour still takes a day
        var text = string.Join("\n", result.Value.Warnings);
        Assert.Contains("repeated a unique ID", text);
        Assert.Contains("no name", text);
        Assert.Contains("shortened", text);
    }

    // -------------------------------------------------------------- exporting

    private static async Task<(Fixture Fixture, XDocument Document)> ExportedAsync(Fixture f, string? xml = null)
    {
        if (xml is not null)
        {
            Assert.True((await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(xml), false, default)).IsSuccess);
        }

        var export = await f.Service.ExportAsync(f.Project.Id, default);
        Assert.True(export.IsSuccess, export.IsFailure ? export.Error.Message : null);
        return (f, XDocument.Parse(Encoding.UTF8.GetString(export.Value!.Content)));
    }

    private static string? Value(XElement element, string name) => element.Element(Ns + name)?.Value;

    private static XElement Task(XDocument document, string name) =>
        document.Root!.Element(Ns + "Tasks")!.Elements(Ns + "Task").Single(t => Value(t, "Name") == name);

    [Fact]
    public async Task Export_WritesAnMspdiDocument_WithTheProjectSummaryRowFirst()
    {
        var (f, document) = await ExportedAsync(Build(), Sample);

        Assert.Equal(Ns, document.Root!.Name.Namespace);
        Assert.Equal("Project", document.Root.Name.LocalName);
        Assert.Equal(("Tower", "480", "2026-03-02T08:00:00"), (Value(document.Root, "Title"), Value(document.Root, "MinutesPerDay"), Value(document.Root, "StartDate")));

        var tasks = document.Root.Element(Ns + "Tasks")!.Elements(Ns + "Task").ToList();
        Assert.Equal(("0", "Tower", "1"), (Value(tasks[0], "UID"), Value(tasks[0], "Name"), Value(tasks[0], "Summary")));
        Assert.Equal(f.Activities.Items.Count + 1, tasks.Count);
        Assert.Equal(Enumerable.Range(0, tasks.Count).Select(i => i.ToString()), tasks.Select(t => Value(t, "UID")));
    }

    [Fact]
    public async Task Export_KeepsTheOutline_InTreeOrder_WithWbsNumbers()
    {
        var (_, document) = await ExportedAsync(Build(), Sample);

        var rows = document.Root!.Element(Ns + "Tasks")!.Elements(Ns + "Task").Skip(1)
            .Select(t => (Value(t, "Name"), Value(t, "OutlineLevel"), Value(t, "OutlineNumber"))).ToList();

        // Siblings go by their calculated start: Walls (which leads the milestone by a day) comes before it.
        Assert.Equal(
            [("Foundations", "1", "1"), ("Excavation", "2", "1.1"), ("Concrete", "2", "1.2"), ("Walls", "1", "2"), ("Foundations done", "1", "3")],
            rows.OrderBy(r => r.Item3, StringComparer.Ordinal).ToList());
        // Rows are in outline order: a parent comes first, then all of its children, then the next sibling.
        var names = rows.Select(r => r.Item1).ToList();
        var parent = names.IndexOf("Foundations");
        Assert.Equal(
            new[] { "Excavation", "Concrete" }.Order(StringComparer.Ordinal),
            names.Skip(parent + 1).Take(2).Order(StringComparer.Ordinal));
        Assert.True(names.IndexOf("Foundations done") > parent + 2);
        Assert.True(names.IndexOf("Walls") > parent + 2);
    }

    [Fact]
    public async Task Export_WritesDurationsInHours_FlagsAndPercentComplete()
    {
        var (_, document) = await ExportedAsync(Build(), Sample);

        var excavation = Task(document, "Excavation");
        Assert.Equal(("PT24H0M0S", "7", "0", "0", "50"), (Value(excavation, "Duration"), Value(excavation, "DurationFormat"), Value(excavation, "Milestone"), Value(excavation, "Summary"), Value(excavation, "PercentComplete")));
        Assert.Equal("1", Value(Task(document, "Foundations"), "Summary"));

        var gate = Task(document, "Foundations done");
        Assert.Equal(("1", "PT0H0M0S"), (Value(gate, "Milestone"), Value(gate, "Duration")));
        Assert.Equal(Value(gate, "Start"), Value(gate, "Finish")); // a milestone has no length
    }

    [Fact]
    public async Task Export_WritesPredecessorLinks_WithMspTypeNumbersAndTenthsOfAMinuteLags()
    {
        var (_, document) = await ExportedAsync(Build(), Sample);
        var uidOf = document.Root!.Element(Ns + "Tasks")!.Elements(Ns + "Task").ToDictionary(t => Value(t, "Name")!, t => Value(t, "UID")!);

        var concreteLink = Task(document, "Concrete").Elements(Ns + "PredecessorLink").Single();
        Assert.Equal((uidOf["Excavation"], "1", "4800"), (Value(concreteLink, "PredecessorUID"), Value(concreteLink, "Type"), Value(concreteLink, "LinkLag")));

        var wallLinks = Task(document, "Walls").Elements(Ns + "PredecessorLink")
            .Select(l => (Value(l, "PredecessorUID"), Value(l, "Type"), Value(l, "LinkLag"))).ToList();
        Assert.Equal(3, wallLinks.Count);
        Assert.Contains((uidOf["Foundations done"], "3", "-4800"), wallLinks);   // start-to-start, one day of lead
        Assert.Contains((uidOf["Excavation"], "0", "0"), wallLinks);             // finish-to-finish
        Assert.Contains((uidOf["Concrete"], "2", "0"), wallLinks);               // start-to-finish
    }

    [Fact]
    public async Task Export_CarriesTheWorkingWeekAndHolidays_OfTheProjectsCalendar()
    {
        var f = Build(new Weekdays());
        var (_, document) = await ExportedAsync(f, Sample);

        var calendar = document.Root!.Element(Ns + "Calendars")!.Element(Ns + "Calendar")!;
        var working = calendar.Element(Ns + "WeekDays")!.Elements(Ns + "WeekDay")
            .ToDictionary(d => int.Parse(Value(d, "DayType")!), d => Value(d, "DayWorking") == "1");
        Assert.Equal(7, working.Count);
        Assert.False(working[1]); // Sunday
        Assert.True(working[2]);  // Monday
        Assert.False(working[7]); // Saturday

        // 2026-03-11 (a Wednesday) is a holiday in the test calendar.
        var exception = Assert.Single(calendar.Element(Ns + "Exceptions")!.Elements(Ns + "Exception"));
        Assert.Equal("0", Value(exception, "DayWorking"));
        Assert.StartsWith("2026-03-11", exception.Element(Ns + "TimePeriod")!.Element(Ns + "FromDate")!.Value);
    }

    [Fact]
    public void TheWeeklyPattern_IsTheDaysAProjectMostlyWorks()
    {
        // Indexed by DayOfWeek (Sunday = 0): Thursday and Friday are the weekend.
        Assert.Equal(
            [true, true, true, true, false, false, true],
            MsProjectXmlWriter.WeeklyPattern(new IranWeek(), Monday));

        // A single holiday does not change the pattern; a day that is off most of the time does.
        Assert.True(MsProjectXmlWriter.WeeklyPattern(new Weekdays(), Monday)[(int)DayOfWeek.Wednesday]);
        Assert.Equal(
            [true, true, true, true, true, true, true],
            MsProjectXmlWriter.WeeklyPattern(AllDaysCalendar.Instance, Monday));
    }

    private sealed class IranWeek : IWorkingDayCalendar
    {
        // Saturday to Wednesday are working days.
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday);
    }

    [Fact]
    public async Task Export_OfAnUnknownProject_IsNotFound_AndTheFileNameIsMadeSafe()
    {
        var f = Build(code: "TWR / 2026: Phase*1");

        Assert.Equal("not_found", (await f.Service.ExportAsync(Guid.NewGuid(), default)).Error.Code);

        await f.Service.ImportAsync(Tenant, f.Project.Id, Stream(Sample), false, default);
        Assert.Equal("TWR_2026_Phase_1.xml", (await f.Service.ExportAsync(f.Project.Id, default)).Value!.FileName);
    }

    [Fact]
    public async Task Export_OfAnEmptyProject_StillProducesAValidDocument()
    {
        var f = Build();

        var export = await f.Service.ExportAsync(f.Project.Id, default);

        Assert.True(export.IsSuccess);
        var document = XDocument.Parse(Encoding.UTF8.GetString(export.Value!.Content));
        Assert.Single(document.Root!.Element(Ns + "Tasks")!.Elements(Ns + "Task")); // only the summary row
    }

    // -------------------------------------------------------------- round trip

    [Fact]
    public async Task ARoundTrip_ExportThenImport_ReproducesTheTreeDurationsLinksAndSchedule()
    {
        var source = Build(new Weekdays());
        await source.Service.ImportAsync(Tenant, source.Project.Id, Stream(Sample), false, default);
        var original = (await source.Schedule.GetScheduleAsync(source.Project.Id, default)).Value!;
        var exported = (await source.Service.ExportAsync(source.Project.Id, default)).Value!;

        var target = Build(new Weekdays());
        var imported = await target.Service.ImportAsync(Tenant, target.Project.Id, new MemoryStream(exported.Content), false, default);

        Assert.True(imported.IsSuccess, imported.IsFailure ? imported.Error.Message : null);
        Assert.Equal(source.Activities.Items.Count, imported.Value!.ActivitiesImported);
        Assert.Equal(source.Dependencies.Items.Count, imported.Value.DependenciesImported);

        foreach (var name in source.Activities.Items.Select(a => a.Name))
        {
            var a = ByName(source, name);
            var b = ByName(target, name);
            Assert.Equal((a.IsMilestone, a.DurationDays, a.ActualProgress), (b.IsMilestone, b.DurationDays, b.ActualProgress));
            Assert.Equal(
                a.ParentActivityId is null ? null : source.Activities.Items.Single(x => x.Id == a.ParentActivityId).Name,
                b.ParentActivityId is null ? null : target.Activities.Items.Single(x => x.Id == b.ParentActivityId).Name);
        }

        (string, string, DependencyType, int) Edge(Fixture f, ActivityDependency d) =>
            (f.Activities.Items.Single(x => x.Id == d.PredecessorActivityId).Name, f.Activities.Items.Single(x => x.Id == d.SuccessorActivityId).Name, d.Type, d.LagDays);
        Assert.Equal(
            source.Dependencies.Items.Select(d => Edge(source, d)).OrderBy(e => e.Item1).ThenBy(e => e.Item2),
            target.Dependencies.Items.Select(d => Edge(target, d)).OrderBy(e => e.Item1).ThenBy(e => e.Item2));

        var again = (await target.Schedule.GetScheduleAsync(target.Project.Id, default)).Value!;
        Assert.Equal(
            original.Activities.Select(a => (a.Name, a.Start, a.Finish)).OrderBy(x => x.Name),
            again.Activities.Select(a => (a.Name, a.Start, a.Finish)).OrderBy(x => x.Name));
        Assert.Equal(original.ProjectFinish, again.ProjectFinish);
    }
}
