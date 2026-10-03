using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Nexus.Actions.Application;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Application.Validators;
using Nexus.Actions.Domain;
using Nexus.Actions.Infrastructure;
using Nexus.Calendar.Application;
using Nexus.Calendar.Domain;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class ActionRecurrenceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();
    private static readonly Guid CalendarId = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Responsible = Guid.NewGuid();

    private sealed class FakeActionRepository : IActionItemRepository
    {
        public List<ActionItem> Items { get; } = [];
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ActionItem>>(Items);
        public Task AddAsync(ActionItem action, CancellationToken ct) { Items.Add(action); return Task.CompletedTask; }
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
    }

    private sealed class CountingUnitOfWork : IActionsUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class NotConfiguredApprovals : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static (ActionItemService Service, FakeActionRepository Repository, CountingUnitOfWork Uow) NewService()
    {
        var repository = new FakeActionRepository();
        var uow = new CountingUnitOfWork();
        return (new ActionItemService(repository, new FakeUnits(), new FakeCalendars(), uow, new NotConfiguredApprovals()), repository, uow);
    }

    private static CreateActionItemRequest Create(
        DateOnly? start, DateOnly? end, ActionRecurrenceRequest? recurrence, ActionPriority? priority = null) =>
        new(Tenant, "Weekly status report", "Send it", Owner, Responsible, Unit, CalendarId, Project, start, end, priority, recurrence);

    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    // ------------------------------------------------------------------- rule

    [Fact]
    public async Task Create_StoresTheRule_AndReturnsItOnTheDto()
    {
        var (service, _, _) = NewService();

        var dto = (await service.CreateAsync(Create(Jan1, Jan1.AddDays(6), new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 2, new DateOnly(2026, 6, 30))), default)).Value!;

        Assert.Equal(new ActionRecurrenceDto(ActionRecurrenceUnit.Weekly, 2, new DateOnly(2026, 6, 30)), dto.Recurrence);
        Assert.Null(dto.NextOccurrenceId);
        Assert.Null(dto.RecurrenceSourceId);
    }

    [Fact]
    public async Task AnActionWithoutARule_IsAOneOff_AndCompletingItCreatesNothing()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, Jan1.AddDays(6), null), default)).Value!.Id;

        var done = (await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default)).Value!;

        Assert.Null(done.Recurrence);
        Assert.Single(repository.Items);
        Assert.Null(done.NextOccurrenceId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    [InlineData(-3)]
    public async Task Create_RejectsAnIntervalOutsideOneTo365(int interval)
    {
        var (service, repository, _) = NewService();

        var result = await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, interval)), default);

        Assert.Equal("validation.error", result.Error.Code);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task Create_RejectsARuleOnAnActionWithNoDates_AndAnEndBeforeTheAction()
    {
        var (service, repository, _) = NewService();

        Assert.Equal("validation.error", (await service.CreateAsync(Create(null, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1)), default)).Error.Code);
        Assert.Equal("validation.error", (await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1, Jan1.AddDays(-1))), default)).Error.Code);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task Update_LeavesTheRuleAlone_WhenTheRequestOmitsIt_AndClearsItWhenUnitIsNull()
    {
        var (service, _, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, Jan1, new ActionRecurrenceRequest(ActionRecurrenceUnit.Monthly, 1)), default)).Value!.Id;
        UpdateActionItemRequest Update(ActionRecurrenceRequest? recurrence) =>
            new("Renamed", null, Owner, Responsible, Unit, CalendarId, Project, Jan1, Jan1, null, recurrence);

        var untouched = (await service.UpdateAsync(id, Update(null), default)).Value!;
        Assert.Equal(ActionRecurrenceUnit.Monthly, untouched.Recurrence!.Unit);
        Assert.Equal("Renamed", untouched.Title);

        var changed = (await service.UpdateAsync(id, Update(new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 3)), default)).Value!;
        Assert.Equal(new ActionRecurrenceDto(ActionRecurrenceUnit.Daily, 3, null), changed.Recurrence);

        var cleared = (await service.UpdateAsync(id, Update(new ActionRecurrenceRequest(null)), default)).Value!;
        Assert.Null(cleared.Recurrence);
    }

    // ------------------------------------------------------------- next occurrence

    [Fact]
    public async Task CompletingARepeatingAction_CreatesTheNextOne_WithShiftedDates_AndTheSameDetails()
    {
        var (service, repository, uow) = NewService();
        var id = (await service.CreateAsync(
            Create(Jan1, Jan1.AddDays(2), new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 2, new DateOnly(2026, 12, 31)), ActionPriority.Urgent), default)).Value!.Id;
        var savesBefore = uow.Saves;

        var done = (await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default)).Value!;

        Assert.Equal(2, repository.Items.Count);
        Assert.Equal(savesBefore + 1, uow.Saves); // both written in one save
        var next = repository.Items[1];
        Assert.Equal(done.NextOccurrenceId, next.Id);
        Assert.Equal(id, next.RecurrenceSourceId);
        Assert.Equal(Jan1.AddDays(14), next.StartDate);
        Assert.Equal(Jan1.AddDays(16), next.EndDate);
        Assert.Equal("Weekly status report", next.Title);
        Assert.Equal(Owner, next.OwnerUserId);
        Assert.Equal(Responsible, next.ResponsibleUserId);
        Assert.Equal(Project, next.ProjectId);
        Assert.Equal(Unit, next.OrganizationUnitId);
        Assert.Equal(ActionPriority.Urgent, next.Priority);
        Assert.Equal(ActionStatus.Open, next.Status);
        Assert.Equal(ApprovalStatus.NotSubmitted, next.ApprovalStatus);
        Assert.Equal(ActionRecurrenceUnit.Weekly, next.RecurrenceUnit);
        Assert.Equal(2, next.RecurrenceInterval);
        Assert.Equal(new DateOnly(2026, 12, 31), next.RecurrenceEndDate);
    }

    [Fact]
    public async Task ReopeningAndRecompleting_DoesNotCreateADuplicate()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1)), default)).Value!.Id;

        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default);
        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.InProgress), default);
        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default);

        Assert.Equal(2, repository.Items.Count);
    }

    [Fact]
    public async Task CompletingTwice_WithoutReopening_CreatesNothingExtra()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1)), default)).Value!.Id;

        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default);
        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default);

        Assert.Equal(2, repository.Items.Count);
    }

    [Fact]
    public async Task OnlyCompletingRepeats_CancellingOrProgressingDoesNot()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1)), default)).Value!.Id;

        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.InProgress), default);
        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Cancelled), default);

        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task TheChainStops_WhenTheNextOccurrenceWouldStartAfterTheEndDate()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 1, Jan1.AddDays(1))), default)).Value!.Id;

        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default); // creates Jan 2
        var second = repository.Items[1];
        await service.ChangeStatusAsync(second.Id, new ChangeActionStatusRequest(ActionStatus.Completed), default); // Jan 3 > end: none

        Assert.Equal(2, repository.Items.Count);
        Assert.Null(second.NextOccurrenceId);
    }

    [Fact]
    public async Task ARuleWithOnlyAnEndDate_RepeatsFromThatDate()
    {
        var (service, repository, _) = NewService();
        var id = (await service.CreateAsync(Create(null, Jan1, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 5)), default)).Value!.Id;

        await service.ChangeStatusAsync(id, new ChangeActionStatusRequest(ActionStatus.Completed), default);

        Assert.Null(repository.Items[1].StartDate);
        Assert.Equal(Jan1.AddDays(5), repository.Items[1].EndDate);
    }

    [Theory]
    [InlineData(ActionRecurrenceUnit.Daily, 3, "2026-01-31", "2026-02-03")]
    [InlineData(ActionRecurrenceUnit.Weekly, 1, "2026-01-31", "2026-02-07")]
    [InlineData(ActionRecurrenceUnit.Monthly, 1, "2026-01-31", "2026-02-28")] // shorter month: its last day
    [InlineData(ActionRecurrenceUnit.Monthly, 1, "2028-01-31", "2028-02-29")] // leap year
    [InlineData(ActionRecurrenceUnit.Monthly, 12, "2026-03-15", "2027-03-15")]
    [InlineData(ActionRecurrenceUnit.Yearly, 1, "2028-02-29", "2029-02-28")]
    public void Shift_MovesDatesByTheUnit(ActionRecurrenceUnit unit, int interval, string from, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ActionItem.Shift(DateOnly.Parse(from), unit, interval));

    // ----------------------------------------------------------------- validation

    [Fact]
    public void TheValidators_BoundTheRule_ButAcceptNoRuleAtAll()
    {
        var create = new CreateActionItemRequestValidator();

        Assert.True(create.Validate(Create(Jan1, null, null)).IsValid);
        Assert.True(create.Validate(Create(Jan1, null, new ActionRecurrenceRequest(null))).IsValid);
        Assert.True(create.Validate(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 365))).IsValid);
        Assert.False(create.Validate(Create(Jan1, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Daily, 0))).IsValid);
        Assert.False(create.Validate(Create(Jan1, null, new ActionRecurrenceRequest((ActionRecurrenceUnit)9, 1))).IsValid);

        var update = new UpdateActionItemRequestValidator();
        Assert.True(update.Validate(new UpdateActionItemRequest("T", null, null, null, Unit, CalendarId, null, null, null)).IsValid);
        Assert.False(update.Validate(new UpdateActionItemRequest("T", null, null, null, Unit, CalendarId, null, Jan1, null, null, new ActionRecurrenceRequest(ActionRecurrenceUnit.Weekly, 400))).IsValid);
    }

    // --------------------------------------------------------------------- schema

    private static ActionsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ActionsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheNewColumns_AreNullable_AndMatchTheScriptAndTheHelper()
    {
        using var db = NewSqlServerContext();
        var columns = SchemaUpgradeVerifier.ColumnLines(db.Database.GenerateCreateScript(), "actions", "Actions");
        var script = SchemaUpgradeVerifier.Normalise(SchemaUpgradeVerifier.ReadScript("2026-10-04-add-action-recurrence.sql"));
        var helper = (IEnumerable<(string Column, string AddSql)>)typeof(ActionsSchemaUpgrade)
            .GetField("RecurrenceColumns", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var expected = new[]
        {
            "[RecurrenceUnit] int NULL", "[RecurrenceInterval] int NULL", "[RecurrenceEndDate] date NULL",
            "[RecurrenceSourceId] uniqueidentifier NULL", "[NextOccurrenceId] uniqueidentifier NULL"
        };

        foreach (var line in expected)
        {
            Assert.Contains(line, columns); // EF really declares it this way
            var name = line[1..line.IndexOf(']')];
            Assert.Contains($"COL_LENGTH(N'actions.Actions', N'{name}') IS NULL", script);
            Assert.Contains($"ALTER TABLE [actions].[Actions] ADD {line};", script);
            Assert.Contains($"ALTER TABLE [actions].[Actions] ADD {line};", helper.Single(h => h.Column == name).AddSql);
        }

        Assert.Equal(expected.Length, helper.Count());

        // Only adds columns: nothing dropped, deleted or rewritten.
        var code = System.Text.RegularExpressions.Regex.Replace(
            SchemaUpgradeVerifier.ReadScript("2026-10-04-add-action-recurrence.sql"), @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.DoesNotMatch(@"(?i)\b(DROP|DELETE|TRUNCATE|UPDATE)\b", code);
    }
}
