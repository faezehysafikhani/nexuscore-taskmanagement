using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Application.EventHandlers;
using Nexus.Actions.Domain;
using Nexus.Actions.Infrastructure;
using Nexus.Calendar.Application;
using Nexus.Calendar.Domain;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.CompositionTests;

/// <summary>Action status requests, the optional pattern of a repeating action and its occurrences, the source link, delete, OnHold.</summary>
public sealed class ActionsPlanningTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();
    private static readonly Guid CalendarId = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    private sealed class FakeActions : IActionItemRepository
    {
        public List<ActionItem> Items { get; } = [];
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(Items);
        public Task AddAsync(ActionItem action, CancellationToken ct) { Items.Add(action); return Task.CompletedTask; }
        public Task RemoveAsync(ActionItem action, CancellationToken ct) { Items.Remove(action); return Task.CompletedTask; }
    }

    private sealed class FakeSubmissions : IActionStatusSubmissionRepository
    {
        public List<ActionStatusSubmission> Items { get; } = [];
        public Task<ActionStatusSubmission?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<ActionStatusSubmission>> ListByActionsAsync(IReadOnlyCollection<Guid> actionIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActionStatusSubmission>>(Items.Where(s => actionIds.Contains(s.ActionId)).ToList());
        public Task<IReadOnlyList<ActionStatusSubmission>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActionStatusSubmission>>(Items.Where(s => projectId is null || s.ProjectId == projectId).ToList());
        public Task AddAsync(ActionStatusSubmission submission, CancellationToken ct) { Items.Add(submission); return Task.CompletedTask; }
    }

    private sealed class FakeUnits : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OrganizationUnit?>(new OrganizationUnit(id, Tenant, "Unit", "U1"));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCalendars : IWorkCalendarRepository
    {
        public Task<WorkCalendar?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<WorkCalendar?>(new WorkCalendar(id, Tenant, "Main", default));
        public Task<IReadOnlyList<WorkCalendar>> ListAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(WorkCalendar calendar, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAsync(WorkCalendar calendar, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Uow : IActionsUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Submitting : IApprovalRequester
    {
        public ApprovalSubject? Subject { get; private set; }
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct)
        {
            Subject = subject;
            return Task.FromResult(ApprovalRequestOutcome.Submitted);
        }
    }

    private sealed class Fixture
    {
        public FakeActions Actions { get; } = new();
        public FakeSubmissions Submissions { get; } = new();
        public ActionItemService Service { get; }
        public ActionStatusSubmissionService StatusService { get; }

        public Fixture(IApprovalRequester? requester = null, ActionsOptions? options = null)
        {
            var approvals = requester ?? new NotConfigured();
            Service = new ActionItemService(Actions, new FakeUnits(), new FakeCalendars(), new Uow(), approvals, options);
            StatusService = new ActionStatusSubmissionService(Submissions, Actions, Service, new Uow(), approvals);
        }

        public async Task<ActionItemDto> AddAsync(
            DateOnly? start = null, DateOnly? end = null, ActionRecurrenceRequest? recurrence = null, string? sourceType = null, Guid? sourceId = null) =>
            (await Service.CreateAsync(new CreateActionItemRequest(
                Tenant, "Action", null, null, null, Unit, CalendarId, Project, start, end, null, recurrence, sourceType, sourceId), default)).Value!;
    }

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ----------------------------------------------------------------- status requests

    [Fact]
    public async Task WithoutAnApprovalBackend_TheActionTakesTheStatusAtOnce()
    {
        var f = new Fixture();
        var action = await f.AddAsync();

        var result = await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.OnHold, "waiting"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalStatus.Approved, result.Value!.ApprovalStatus);
        Assert.Equal(ActionStatus.Open, result.Value.PreviousStatus);
        Assert.Equal(ActionStatus.OnHold, f.Actions.Items.Single().Status);
    }

    [Fact]
    public async Task WithAWorkflow_TheActionStaysPut_UntilTheRequestIsApproved_AndOnlyOneRequestCanBePending()
    {
        var requester = new Submitting();
        var f = new Fixture(requester);
        var action = await f.AddAsync();

        var first = (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.InProgress, null), default)).Value!;

        Assert.Equal(ApprovalStatus.PendingApproval, first.ApprovalStatus);
        Assert.Equal(ActionStatus.Open, f.Actions.Items.Single().Status);
        Assert.Equal("ActionStatus", requester.Subject!.SubjectType);
        Assert.Equal(Project, requester.Subject.ScopeId);

        var second = await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.Completed, null), default);
        Assert.Equal("conflict", second.Error.Code);

        var granted = new ActionStatusApprovalGrantedHandler(f.Submissions, f.Service, new Uow());
        await granted.HandleAsync(new ApprovalGranted("ActionStatus", first.Id, Tenant, null, null), default);

        Assert.Equal(ActionStatus.InProgress, f.Actions.Items.Single().Status);
        Assert.Equal(ApprovalStatus.Approved, f.Submissions.Items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task ARejectedRequest_LeavesTheStatusAlone()
    {
        var f = new Fixture(new Submitting());
        var action = await f.AddAsync();
        var request = (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.Completed, null), default)).Value!;

        await new ActionStatusApprovalRejectedHandler(f.Submissions, new Uow()).HandleAsync(new ApprovalRejected("ActionStatus", request.Id, Tenant, null, null), default);

        Assert.Equal(ActionStatus.Open, f.Actions.Items.Single().Status);
        Assert.Equal(ApprovalStatus.Rejected, f.Submissions.Items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task OtherSubjectTypes_AreIgnoredByTheStatusHandlers()
    {
        var f = new Fixture(new Submitting());
        var action = await f.AddAsync();
        var request = (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.Completed, null), default)).Value!;

        await new ActionStatusApprovalGrantedHandler(f.Submissions, f.Service, new Uow()).HandleAsync(new ApprovalGranted("Action", request.Id, Tenant, null, null), default);

        Assert.Equal(ActionStatus.Open, f.Actions.Items.Single().Status);
    }

    [Fact]
    public async Task AskingForTheStatusItAlreadyHas_OrAnUnknownOne_IsRefused()
    {
        var f = new Fixture();
        var action = await f.AddAsync();

        Assert.Equal("validation.error", (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, ActionStatus.Open, null), default)).Error.Code);
        Assert.Equal("validation.error", (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, action.Id, (ActionStatus)99, null), default)).Error.Code);
        Assert.Equal("not_found", (await f.StatusService.SubmitAsync(new SubmitActionStatusRequest(Tenant, Guid.NewGuid(), ActionStatus.Completed, null), default)).Error.Code);
    }

    // ----------------------------------------------------------------- delete, source

    [Fact]
    public async Task Delete_RemovesTheAction_ButNotWhileItWaitsForApproval()
    {
        var f = new Fixture(new Submitting());
        var gone = await f.AddAsync();
        var pending = await f.AddAsync();
        await f.Service.SubmitForApprovalAsync(pending.Id, default);

        Assert.True((await f.Service.DeleteAsync(gone.Id, default)).IsSuccess);
        Assert.Equal("conflict", (await f.Service.DeleteAsync(pending.Id, default)).Error.Code);
        Assert.Equal("not_found", (await f.Service.DeleteAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.Single(f.Actions.Items);
    }

    [Fact]
    public async Task TheSourceLink_IsStored_KeptWhenAnUpdateOmitsIt_AndReplacedWhenGiven()
    {
        var f = new Fixture();
        var risk = Guid.NewGuid();
        var action = await f.AddAsync(sourceType: "Risk", sourceId: risk);
        Assert.Equal(("Risk", risk), (action.SourceType, action.SourceId));

        UpdateActionItemRequest Update(string? type, Guid? id) =>
            new("Renamed", null, null, null, Unit, CalendarId, Project, null, null, null, null, type, id);

        var kept = (await f.Service.UpdateAsync(action.Id, Update(null, null), default)).Value!;
        Assert.Equal(("Risk", risk), (kept.SourceType, kept.SourceId));

        var stakeholder = Guid.NewGuid();
        var replaced = (await f.Service.UpdateAsync(action.Id, Update("Stakeholder", stakeholder), default)).Value!;
        Assert.Equal(("Stakeholder", stakeholder), (replaced.SourceType, replaced.SourceId));
    }

    [Fact]
    public async Task TheNamedApprovers_AreKeptInOrder_AnUpdateThatOmitsThemKeepsThem_AnEmptyListClearsThem_AndMoreThanThreeIsRefused()
    {
        var f = new Fixture();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var created = (await f.Service.CreateAsync(new CreateActionItemRequest(
            Tenant, "Action", null, null, null, Unit, CalendarId, Project, null, null, null, null, null, null, [a, b]), default)).Value!;
        Assert.Equal([a, b], created.ApproverUserIds);

        UpdateActionItemRequest Update(IReadOnlyList<Guid>? approvers) =>
            new("Renamed", null, null, null, Unit, CalendarId, Project, null, null, null, null, null, null, approvers);

        Assert.Equal([a, b], (await f.Service.UpdateAsync(created.Id, Update(null), default)).Value!.ApproverUserIds);
        Assert.Equal([c, a, b], (await f.Service.UpdateAsync(created.Id, Update([c, a, b]), default)).Value!.ApproverUserIds);
        Assert.Null((await f.Service.UpdateAsync(created.Id, Update([]), default)).Value!.ApproverUserIds);
        Assert.Equal("validation.error", (await f.Service.UpdateAsync(created.Id, Update([a, b, c, Guid.NewGuid()]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Service.UpdateAsync(created.Id, Update([a, a]), default)).Error.Code);
    }

    // ----------------------------------------------------------------- the pattern

    [Fact]
    public async Task ThePattern_IsStoredAndReturned_AndAFullYearOfMonthsMeansEveryMonth()
    {
        var f = new Fixture();
        var dto = await f.AddAsync(D(2026, 1, 1), D(2026, 12, 31),
            new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 2, null, [6, 0, 3], 5, [1, 2, 3], 1.5m));

        var rule = dto.Recurrence!;
        Assert.Equal([0, 3, 6], rule.DaysOfWeek);
        Assert.Equal(5, rule.DayOfMonth);
        Assert.Equal([1, 2, 3], rule.Months);
        Assert.Equal(1.5m, rule.DurationHours);

        var all = await f.AddAsync(D(2026, 1, 1), D(2026, 12, 31),
            new ActionRecurrenceRequest(ActionRecurrenceUnit.Monthly, 1, null, null, 1, Enumerable.Range(1, 12).ToList()));
        Assert.Null(all.Recurrence!.Months);
    }

    [Fact]
    public async Task ARequestWithoutPatternFields_ClearsThem_AndOutOfRangeValuesAreRefused()
    {
        var f = new Fixture();
        var dto = await f.AddAsync(D(2026, 1, 1), D(2026, 3, 1), new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 1, null, [1], null, null, 2));
        var update = new UpdateActionItemRequest("A", null, null, null, Unit, CalendarId, Project, D(2026, 1, 1), D(2026, 3, 1), null,
            new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 1));

        var plain = (await f.Service.UpdateAsync(dto.Id, update, default)).Value!;
        Assert.Null(plain.Recurrence!.DaysOfWeek);
        Assert.Null(plain.Recurrence.DurationHours);

        foreach (var bad in new[]
                 {
                     new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 1, null, [7]),
                     new ActionRecurrenceRequest(ActionRecurrenceUnit.Monthly, 1, null, null, 32),
                     new ActionRecurrenceRequest(ActionRecurrenceUnit.Monthly, 1, null, null, 1, [13]),
                     new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1, null, null, null, null, 25m)
                 })
        {
            var result = await f.Service.UpdateAsync(dto.Id, update with { Recurrence = bad }, default);
            Assert.Equal("validation.error", result.Error.Code);
        }
    }

    // ----------------------------------------------------------------- occurrences

    private static IReadOnlyList<DateOnly> Dates(IReadOnlyList<ActionOccurrenceDto> occurrences) => occurrences.Select(o => o.Date).ToList();

    [Fact]
    public void AOneOffAction_HasASingleOccurrence_OnItsEndDate()
    {
        var one = ActionOccurrenceCalculator.Calculate(null, 1, D(2026, 1, 1), D(2026, 1, 5), null, null, null, null);
        Assert.Equal([D(2026, 1, 5)], Dates(one));

        var noRange = ActionOccurrenceCalculator.Calculate(ActionRecurrenceUnit.Daily, 1, D(2026, 1, 1), null, null, null, null, 2m);
        Assert.Equal([D(2026, 1, 1)], Dates(noRange));
        Assert.Equal(2m, noRange[0].DurationHours);
    }

    [Fact]
    public void Daily_StepsByTheInterval_InsideTheRange()
    {
        var result = ActionOccurrenceCalculator.Calculate(ActionRecurrenceUnit.Daily, 3, D(2026, 1, 1), D(2026, 1, 10), null, null, null, null);

        Assert.Equal([D(2026, 1, 1), D(2026, 1, 4), D(2026, 1, 7), D(2026, 1, 10)], Dates(result));
        Assert.Equal([1, 2, 3, 4], result.Select(o => o.Index));
    }

    [Fact]
    public void Weekly_UsesTheChosenDays_OrSaturdayToWednesdayByDefault_AndEveryNthWeek()
    {
        // 2026-01-03 is a Saturday.
        var defaults = ActionOccurrenceCalculator.Calculate(ActionRecurrenceUnit.Weekly, 1, D(2026, 1, 3), D(2026, 1, 9), null, null, null, null);
        Assert.Equal([D(2026, 1, 3), D(2026, 1, 4), D(2026, 1, 5), D(2026, 1, 6), D(2026, 1, 7)], Dates(defaults));

        var mondaysEveryOtherWeek = ActionOccurrenceCalculator.Calculate(
            ActionRecurrenceUnit.Weekly, 2, D(2026, 1, 5), D(2026, 2, 2), 1 << (int)DayOfWeek.Monday, null, null, null);
        Assert.Equal([D(2026, 1, 5), D(2026, 1, 19), D(2026, 2, 2)], Dates(mondaysEveryOtherWeek));
    }

    [Fact]
    public void Monthly_FollowsTheJalaliDayAndMonths()
    {
        // 1 Farvardin 1405 = 2026-03-21. The 1st of every Jalali month from Farvardin to Khordad.
        var result = ActionOccurrenceCalculator.Calculate(
            ActionRecurrenceUnit.Monthly, 1, D(2026, 3, 21), D(2026, 7, 1), null, 1, null, null);
        Assert.Equal([D(2026, 3, 21), D(2026, 4, 21), D(2026, 5, 22), D(2026, 6, 22)], Dates(result));

        // Only Farvardin and Ordibehesht.
        var two = ActionOccurrenceCalculator.Calculate(
            ActionRecurrenceUnit.Monthly, 1, D(2026, 3, 21), D(2026, 7, 1), null, 1, 0b11, null);
        Assert.Equal([D(2026, 3, 21), D(2026, 4, 21)], Dates(two));

        // Every second month.
        var everyOther = ActionOccurrenceCalculator.Calculate(
            ActionRecurrenceUnit.Monthly, 2, D(2026, 3, 21), D(2026, 7, 1), null, 1, null, null);
        Assert.Equal([D(2026, 3, 21), D(2026, 5, 22)], Dates(everyOther));
    }

    [Fact]
    public void Monthly_ADayThatTheMonthLacks_FallsOnItsLastDay()
    {
        // The 31st: Farvardin has 31 days, Mehr (month 7) 30. 2026-09-23 is 1 Mehr 1405; its last day is 30 Mehr = 2026-10-22.
        var result = ActionOccurrenceCalculator.Calculate(
            ActionRecurrenceUnit.Monthly, 1, D(2026, 9, 23), D(2026, 10, 31), null, 31, 1 << 6, null);

        Assert.Equal([D(2026, 10, 22)], Dates(result));
    }

    [Fact]
    public void ARuleThatMatchesNothing_StillYieldsTheFirstDay_AndTheListIsCapped()
    {
        var none = ActionOccurrenceCalculator.Calculate(ActionRecurrenceUnit.Weekly, 1, D(2026, 1, 3), D(2026, 1, 4), 1 << (int)DayOfWeek.Friday, null, null, null);
        Assert.Equal([D(2026, 1, 3)], Dates(none));

        var capped = ActionOccurrenceCalculator.Calculate(ActionRecurrenceUnit.Daily, 1, D(2026, 1, 1), D(2030, 1, 1), null, null, null, null);
        Assert.Equal(ActionOccurrenceCalculator.DefaultMaxCount, capped.Count);
    }

    [Fact]
    public async Task TheService_ListsTheOccurrencesOfAnAction()
    {
        var f = new Fixture();
        var dto = await f.AddAsync(D(2026, 1, 1), D(2026, 1, 3), new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1, null, null, null, null, 1m));

        var listed = (await f.Service.ListOccurrencesAsync(dto.Id, default)).Value!;

        Assert.Equal(3, listed.Count);
        Assert.Equal("not_found", (await f.Service.ListOccurrencesAsync(Guid.NewGuid(), default)).Error.Code);
    }

    // ----------------------------------------------------------------- the host switch

    [Fact]
    public async Task CompletingARepeatingAction_CreatesTheNextOne_UnlessTheHostTurnsThatOff()
    {
        var chain = new Fixture();
        var a = await chain.AddAsync(D(2026, 1, 1), D(2026, 1, 1), new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1));
        await chain.Service.ChangeStatusAsync(a.Id, new ChangeActionStatusRequest(ActionStatus.Completed), default);
        Assert.Equal(2, chain.Actions.Items.Count);

        var kept = new Fixture(options: new ActionsOptions { CreateNextOccurrenceOnCompletion = false });
        var b = await kept.AddAsync(D(2026, 1, 1), D(2026, 1, 1), new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1));
        await kept.Service.ChangeStatusAsync(b.Id, new ChangeActionStatusRequest(ActionStatus.Completed), default);
        Assert.Single(kept.Actions.Items);
    }

    [Fact]
    public async Task EndAfterStart_IsOnlyEnforcedWhenTheHostAsks_AndOnlyOnChangedDates()
    {
        var lenient = new Fixture();
        Assert.NotNull(await lenient.AddAsync(D(2026, 1, 5), D(2026, 1, 5))); // today's behaviour: accepted

        var strict = new Fixture(options: new ActionsOptions { RequireEndAfterStart = true });
        var refused = await strict.Service.CreateAsync(new CreateActionItemRequest(
            Tenant, "A", null, null, null, Unit, CalendarId, Project, D(2026, 1, 5), D(2026, 1, 5)), default);
        Assert.Equal(ActionItemService.EndNotAfterStartErrorCode, refused.Error.Code);
        Assert.Empty(strict.Actions.Items);

        // An action saved before the rule (equal dates) can still be renamed; changing its dates to a bad range cannot.
        var old = new Fixture();
        var legacy = await old.AddAsync(D(2026, 1, 5), D(2026, 1, 5));
        var withRule = new ActionItemService(old.Actions, new FakeUnits(), new FakeCalendars(), new Uow(), new NotConfigured(), new ActionsOptions { RequireEndAfterStart = true });
        UpdateActionItemRequest Update(DateOnly end) => new("Renamed", null, null, null, Unit, CalendarId, Project, D(2026, 1, 5), end);
        Assert.True((await withRule.UpdateAsync(legacy.Id, Update(D(2026, 1, 5)), default)).IsSuccess);
        Assert.Equal(ActionItemService.EndNotAfterStartErrorCode, (await withRule.UpdateAsync(legacy.Id, Update(D(2026, 1, 4)), default)).Error.Code);
        Assert.True((await withRule.UpdateAsync(legacy.Id, Update(D(2026, 1, 9)), default)).IsSuccess);
    }

    [Fact]
    public void OnHold_IsAppended_SoExistingStoredValuesKeepTheirMeaning()
    {
        Assert.Equal(0, (int)ActionStatus.Open);
        Assert.Equal(1, (int)ActionStatus.InProgress);
        Assert.Equal(2, (int)ActionStatus.Completed);
        Assert.Equal(3, (int)ActionStatus.Cancelled);
        Assert.Equal(4, (int)ActionStatus.OnHold);
    }

    // ----------------------------------------------------------------- schema

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel_AndOnlyAdd()
    {
        using var db = new ActionsDbContext(
            new DbContextOptionsBuilder<ActionsDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "actions", "2026-10-14-add-action-status-and-patterns.sql", typeof(ActionsPlanningSchemaUpgrade),
            ["ActionStatusSubmissions"],
            ("Actions", "[SourceType] nvarchar(40) NULL"),
            ("Actions", "[SourceId] uniqueidentifier NULL"),
            ("Actions", "[Approver1UserId] uniqueidentifier NULL"),
            ("Actions", "[Approver2UserId] uniqueidentifier NULL"),
            ("Actions", "[Approver3UserId] uniqueidentifier NULL"),
            ("Actions", "[RecurrenceDaysOfWeek] int NULL"),
            ("Actions", "[RecurrenceDayOfMonth] int NULL"),
            ("Actions", "[RecurrenceMonths] int NULL"),
            ("Actions", "[RecurrenceDurationHours] decimal(5,2) NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-14-add-action-status-and-patterns.sql", typeof(ActionsPlanningSchemaUpgrade));
    }
}
