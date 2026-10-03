using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Nexus.Actions.Application;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Domain;
using Nexus.Actions.Infrastructure;
using Nexus.Calendar.Application;
using Nexus.Calendar.Domain;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class ActionPriorityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();
    private static readonly Guid CalendarId = Guid.NewGuid();

    private sealed class FakeActionRepository : IActionItemRepository
    {
        public List<ActionItem> Items { get; } = [];
        public Task<ActionItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<ActionItem>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActionItem>>(Items);
        public Task AddAsync(ActionItem action, CancellationToken ct) { Items.Add(action); return Task.CompletedTask; }
    }

    private sealed class FakeUnits : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<OrganizationUnit?>(new OrganizationUnit(id, Tenant, "Unit", "U1"));
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(OrganizationUnit unit, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCalendars : IWorkCalendarRepository
    {
        public Task<WorkCalendar?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<WorkCalendar?>(new WorkCalendar(id, Tenant, "Main", default));
        public Task<IReadOnlyList<WorkCalendar>> ListAsync(Guid tenantId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(WorkCalendar calendar, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IActionsUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class NotConfiguredApprovals : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static ActionItemService NewService(FakeActionRepository repository) =>
        new(repository, new FakeUnits(), new FakeCalendars(), new FakeUnitOfWork(), new NotConfiguredApprovals());

    private static CreateActionItemRequest Create(ActionPriority? priority = null) =>
        new(Tenant, "Review drawings", null, null, null, Unit, CalendarId, null, null, null, priority);

    private static UpdateActionItemRequest Update(ActionPriority? priority = null) =>
        new("Review drawings", null, null, null, Unit, CalendarId, null, null, null, priority);

    [Fact]
    public async Task Create_WithoutAPriority_DefaultsToNormal()
    {
        var created = await NewService(new FakeActionRepository()).CreateAsync(Create(), default);

        Assert.True(created.IsSuccess);
        Assert.Equal(ActionPriority.Normal, created.Value!.Priority);
    }

    [Theory]
    [InlineData(ActionPriority.Low)]
    [InlineData(ActionPriority.High)]
    [InlineData(ActionPriority.Urgent)]
    public async Task Create_WithAPriority_StoresIt(ActionPriority priority)
    {
        var created = await NewService(new FakeActionRepository()).CreateAsync(Create(priority), default);

        Assert.Equal(priority, created.Value!.Priority);
    }

    [Fact]
    public async Task Update_ChangesThePriority_WhenGiven_AndLeavesItAloneWhenOmitted()
    {
        var repository = new FakeActionRepository();
        var service = NewService(repository);
        var id = (await service.CreateAsync(Create(ActionPriority.High), default)).Value!.Id;

        // A client that predates the field omits it: the stored priority must survive the edit.
        var untouched = await service.UpdateAsync(id, Update(), default);
        Assert.Equal(ActionPriority.High, untouched.Value!.Priority);

        var changed = await service.UpdateAsync(id, Update(ActionPriority.Urgent), default);
        Assert.Equal(ActionPriority.Urgent, changed.Value!.Priority);
    }

    [Fact]
    public async Task Priority_RoundTripsThroughEfCore_IncludingLowWhichIsTheEnumsZero()
    {
        var options = new DbContextOptionsBuilder<ActionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Guid lowId;
        await using (var db = new ActionsDbContext(options))
        {
            var low = new ActionItem(Guid.NewGuid(), Tenant, "low", Unit, CalendarId);
            low.ChangePriority(ActionPriority.Low);
            var urgent = new ActionItem(Guid.NewGuid(), Tenant, "urgent", Unit, CalendarId);
            urgent.ChangePriority(ActionPriority.Urgent);
            db.Actions.AddRange(low, urgent);
            await db.SaveChangesAsync();
            lowId = low.Id;
        }

        await using (var db = new ActionsDbContext(options))
        {
            Assert.Equal(ActionPriority.Low, (await db.Actions.SingleAsync(a => a.Id == lowId)).Priority);
            Assert.Equal(1, await db.Actions.CountAsync(a => a.Priority == ActionPriority.Urgent));
        }
    }

    [Fact]
    public void TheModel_MapsPriorityToANonNullIntColumn_MatchingTheUpgradeScript()
    {
        // Builds the SQL Server model only; nothing connects.
        var options = new DbContextOptionsBuilder<ActionsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new ActionsDbContext(options);

        var entity = db.Model.FindEntityType(typeof(ActionItem))!;
        var property = entity.FindProperty(nameof(ActionItem.Priority))!;
        var table = StoreObjectIdentifier.Table("Actions", "actions");

        Assert.Equal("Priority", property.GetColumnName(table));
        Assert.False(property.IsNullable);
        // Enums are stored as int, which is what the script's `int NOT NULL` column holds.
        Assert.Equal("int", property.GetColumnType());

        // The model ships no DB-side default: with one, EF would omit the column whenever the
        // value equals the CLR default (Low = 0) and the database would store Normal instead.
        // (GetDefaultValue() alone is no use here - it reports the CLR default for an unconfigured
        // enum - so look for the actual annotations.)
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));

        var script = File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "upgrade", "2026-10-03-add-action-priority.sql"));
        Assert.Contains("[actions].[Actions] ADD [Priority] int NOT NULL", script);
        Assert.Contains("DEFAULT 1", script); // existing rows become Normal (= 1)
        Assert.Equal(1, (int)ActionPriority.Normal);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NexusCore.sln not found above the test output.");
    }
}
