using Microsoft.EntityFrameworkCore;
using Nexus.Integrations.TaskReporting;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// Task reports (Nexus.Integrations.TaskReporting): a user reports on the tasks they can see plus
/// every task of anyone below them in the organization structure - nothing else.
/// </summary>
public sealed class TaskReportTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3.5));

    // boss (company head) <- deputyHead (manages the deputy) <- expert; outsider and other are not in the structure.
    private readonly Guid _boss = Guid.NewGuid(), _deputyHead = Guid.NewGuid(), _expert = Guid.NewGuid(), _outsider = Guid.NewGuid(), _other = Guid.NewGuid();
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly Users _users = new();
    private readonly History _history = new();
    private Guid _expertPosition;
    private TaskItem _t1 = null!, _t2 = null!, _t3 = null!, _t4 = null!, _t5 = null!, _t6 = null!;

    private async Task SeedAsync()
    {
        foreach (var (id, name) in new[] { (_boss, "رئیس"), (_deputyHead, "معاون"), (_expert, "کارشناس"), (_outsider, "بیرونی"), (_other, "دیگر") })
        {
            _users.Add(new UserContact(id, Tenant, name, null, null, null, null, false, true));
        }

        var structure = OrgStructure();
        var unitsDb = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + _db).Options);
        var deputy = new OrganizationUnit(Guid.NewGuid(), Tenant, "معاونت", _deputyHead);
        unitsDb.OrganizationUnits.Add(deputy);
        await unitsDb.SaveChangesAsync();

        var company = (await structure.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت", "C1", null), default)).Value!.Id;
        await structure.LinkUnitAsync(Tenant, deputy.Id, new LinkUnitRequest(company), default);
        var headPosition = (await structure.CreatePositionAsync(Tenant, new CreatePositionRequest("رئیس کل", null, [company]), default)).Value!.Id;
        _expertPosition = (await structure.CreatePositionAsync(Tenant, new CreatePositionRequest("کارشناس", headPosition, [company]), default)).Value!.Id;
        Assert.True((await structure.SetAssignmentAsync(Tenant, _boss, new SetUserAssignmentRequest(company, null, headPosition, null), default)).IsSuccess);
        await structure.SetAssignmentAsync(Tenant, _deputyHead, new SetUserAssignmentRequest(company, deputy.Id, null, null), default);
        Assert.True((await structure.SetAssignmentAsync(Tenant, _expert, new SetUserAssignmentRequest(company, deputy.Id, _expertPosition, null), default)).IsSuccess);

        using var tasks = TasksDb(restricted: false, null);
        var finance = new Tag(Guid.NewGuid(), Tenant, "مالی");
        tasks.Tags.Add(finance);

        TaskItem Add(string title, Guid owner, Guid responsible, int dueInDays, bool project = false, Guid? tenant = null)
        {
            var task = new TaskItem(Guid.NewGuid(), tenant ?? Tenant, title, Today.AddDays(dueInDays), TaskPriority.Medium, project, owner);
            task.SetResponsibleUsers([responsible]);
            tasks.Tasks.Add(task);
            return task;
        }

        _t1 = Add("کار کارشناس که رئیس رویش نیست", _outsider, _expert, 5);
        _t2 = Add("کار بیرونی", _outsider, _other, 5);
        _t3 = Add("پروژه رئیس", _boss, _boss, 10, project: true);
        _t4 = Add("تکراری ساخته کارشناس", _expert, _other, 3);
        _t4.AttachRecurrence(new RepetitiveTask(Guid.NewGuid(), Tenant, _t4.Id, RecurrenceFrequency.Daily, Today));
        _t5 = Add("کار تمام‌شده معاون", _outsider, _deputyHead, -10);
        _t5.ChangeStatus(TaskItemStatus.Completed, DateTimeOffset.UtcNow);
        tasks.TaskTags.Add(TaskTag.ForTask(Guid.NewGuid(), finance.Id, _t5.Id));
        _t6 = Add("کار عقب‌افتاده کارشناس", _outsider, _expert, -2);
        Add("سازمان دیگر", _outsider, _expert, 1, tenant: OtherTenant);
        await tasks.SaveChangesAsync();
    }

    [Fact]
    public async Task Manager_SeesOwnAndAllSubordinatesTasks_AtAnyDepth_AndNothingElse()
    {
        await SeedAsync();

        var boss = (await Service(_boss).GetReportAsync(Tenant, _boss, Query(), default)).Value!;
        Assert.Equal(Ids(_t1, _t3, _t4, _t5, _t6), Ids(boss));
        // On their own task the boss can open it; the rest are in the report only as their manager.
        Assert.True(boss.Rows.Single(r => r.Id == _t3.Id).CanOpen);
        Assert.False(boss.Rows.Single(r => r.Id == _t1.Id).CanOpen);

        var deputy = (await Service(_deputyHead).GetReportAsync(Tenant, _deputyHead, Query(), default)).Value!;
        Assert.Equal(Ids(_t1, _t4, _t5, _t6), Ids(deputy));

        // Without subordinates: only what the user can already see.
        var expert = (await Service(_expert).GetReportAsync(Tenant, _expert, Query(), default)).Value!;
        Assert.Equal(Ids(_t1, _t4, _t6), Ids(expert));
    }

    [Fact]
    public async Task Admin_SeesTheWholeOrganization_ButNotOtherTenants()
    {
        await SeedAsync();
        var all = (await Service(_outsider, restricted: false).GetReportAsync(Tenant, _outsider, Query(), default)).Value!;
        Assert.Equal(Ids(_t1, _t2, _t3, _t4, _t5, _t6), Ids(all));
    }

    [Fact]
    public async Task Filters_ByPersonAsResponsibleOrCreator_Position_Tag_Type_Status_AndDueDate()
    {
        await SeedAsync();
        var service = Service(_boss);
        async Task<IEnumerable<Guid>> Run(TaskReportQuery q) => Ids((await service.GetReportAsync(Tenant, _boss, q, default)).Value!);

        Assert.Equal(Ids(_t1, _t6), await Run(Query() with { PersonId = _expert }));
        Assert.Equal(Ids(_t4), await Run(Query() with { PersonId = _expert, PersonRole = "creator" }));
        Assert.Equal(Ids(_t1, _t6), await Run(Query() with { PositionId = _expertPosition }));
        Assert.Equal(Ids(_t4), await Run(Query() with { PositionId = _expertPosition, PersonRole = "creator" }));
        Assert.Equal(Ids(_t5), await Run(Query() with { Tag = "مالی" }));
        Assert.Equal(Ids(_t3), await Run(Query() with { Type = "project" }));
        Assert.Equal(Ids(_t4), await Run(Query() with { Type = "recurring" }));
        Assert.Equal(Ids(_t1, _t5, _t6), await Run(Query() with { Type = "normal" }));
        Assert.Equal(Ids(_t5), await Run(Query() with { Statuses = ["completed"] }));
        Assert.Equal(Ids(_t1, _t3, _t4, _t6), await Run(Query() with { Statuses = ["todo", "in_progress"] }));
        Assert.Equal(Ids(_t4, _t6), await Run(Query() with { DueFrom = Today.AddDays(-5), DueTo = Today.AddDays(3) }));
    }

    [Fact]
    public async Task Summary_CountsEveryMatch()
    {
        await SeedAsync();
        var summary = (await Service(_boss).GetReportAsync(Tenant, _boss, Query(), default)).Value!.Summary;
        Assert.Equal(new TaskReportSummary(Total: 5, Todo: 4, InProgress: 0, Paused: 0, Completed: 1, Overdue: 1, Normal: 3, Project: 1, Recurring: 1), summary);
    }

    [Fact]
    public async Task Scope_ListsSelfAndSubordinatesWithTheirPositions()
    {
        await SeedAsync();
        var scope = (await Service(_boss).GetScopeAsync(Tenant, _boss, default)).Value!;
        Assert.False(scope.SeesAll);
        Assert.Equal([(_boss, "self"), (_deputyHead, "direct"), (_expert, "indirect")], scope.People.Select(p => (p.UserId, p.Relation)));
        Assert.Equal("کارشناس", scope.People.Single(p => p.UserId == _expert).PositionTitle);
        Assert.Equal(["مالی"], scope.Tags);
        // The head's position counts too: the boss holds it.
        Assert.Equal(["رئیس کل", "کارشناس"], scope.Positions.Select(p => p.Title).Order());
    }

    [Fact]
    public async Task InvalidFilters_AreRejected()
    {
        await SeedAsync();
        var service = Service(_boss);
        Assert.Equal("validation.error", (await service.GetReportAsync(Tenant, _boss, Query() with { PersonRole = "watcher" }, default)).Error.Code);
        Assert.Equal("validation.error", (await service.GetReportAsync(Tenant, _boss, Query() with { Statuses = ["done"] }, default)).Error.Code);
        Assert.Equal("validation.error", (await service.GetReportAsync(Tenant, _boss, Query() with { DueFrom = Today, DueTo = Today.AddDays(-1) }, default)).Error.Code);
    }

    [Fact]
    public async Task TasksGivenToPosts_ShowWhoWasOnThemWhen_WithTheirPositionThen()
    {
        await SeedAsync();
        // The expert was on t7 (an expert then), the deputy's head stood in while its post was
        // vacant, then «other» took the post. The task is now only «other»'s.
        var t7 = new TaskItem(Guid.NewGuid(), Tenant, "کار پستی", Today, TaskPriority.Medium, false, _outsider);
        t7.SetResponsibleUsers([_other]);
        using (var tasks = TasksDb(restricted: false, null))
        {
            tasks.Tasks.Add(t7);
            await tasks.SaveChangesAsync();
        }

        var at = DateTimeOffset.UtcNow;
        var post = Guid.NewGuid();
        TaskPostPeriodDto Period(Guid who, string role, string position, Guid positionId, int fromDays, int? toDays) => new(
            t7.Id, who, role, post, "کارشناس", "7", "معاونت · شرکت", positionId, position, at.AddDays(fromDays), toDays is { } d ? at.AddDays(d) : null);
        _history.Periods.AddRange([
            Period(_expert, "responsible", "کارشناس", _expertPosition, -10, -5),
            Period(_deputyHead, "acting", "مدیر معاونت", Guid.NewGuid(), -5, -3),
            Period(_other, "responsible", "کارشناس", _expertPosition, -3, null),
        ]);

        var boss = Service(_boss, restricted: false);
        async Task<TaskReportDto> Run(TaskReportQuery q) => (await boss.GetReportAsync(Tenant, _boss, q, default)).Value!;

        // Found by the people who were on it (the stand-in as responsible) and by the position they had then.
        Assert.Contains(t7.Id, Ids(await Run(Query() with { PersonId = _expert })));
        Assert.Contains(t7.Id, Ids(await Run(Query() with { PersonId = _deputyHead })));
        Assert.DoesNotContain(t7.Id, Ids(await Run(Query() with { PersonId = _expert, PersonRole = "creator" })));
        Assert.Contains(t7.Id, Ids(await Run(Query() with { PositionId = _expertPosition })));
        // And by the expert in their own report, though it is no longer theirs.
        var own = (await Service(_expert).GetReportAsync(Tenant, _expert, Query(), default)).Value!;
        Assert.False(Assert.Single(own.Rows, r => r.Id == t7.Id).CanOpen);

        var row = (await Run(Query())).Rows.Single(r => r.Id == t7.Id);
        Assert.Equal(
            [("کارشناس", "responsible", "کارشناس", true), ("معاون", "acting", "مدیر معاونت", true), ("دیگر", "responsible", "کارشناس", false)],
            row.History!.Select(h => (h.DisplayName, h.Role, h.PositionTitle, h.ToUtc is not null)).ToArray());

        // Done while the stand-in was on it: their stretch ends then, and «other», who came after, is left out.
        using (var tasks = TasksDb(restricted: false, null))
        {
            var done = await tasks.Tasks.SingleAsync(t => t.Id == t7.Id);
            done.ChangeStatus(TaskItemStatus.Completed, at.AddDays(-4));
            await tasks.SaveChangesAsync();
        }

        row = (await Run(Query())).Rows.Single(r => r.Id == t7.Id);
        Assert.Equal(["کارشناس", "معاون"], row.History!.Select(h => h.DisplayName).ToArray());
        Assert.Equal(at.AddDays(-4), row.History![1].ToUtc);
    }

    // ------------------------------------------------------------------ helpers

    private static TaskReportQuery Query() => new(null, null, null, null, null, [], null, null);

    private static IEnumerable<Guid> Ids(params TaskItem[] tasks) => tasks.Select(t => t.Id).OrderBy(id => id);

    private static IEnumerable<Guid> Ids(TaskReportDto report) => report.Rows.Select(r => r.Id).OrderBy(id => id);

    private OrgStructureService OrgStructure()
    {
        var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + _db).Options);
        var units = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + _db).Options);
        return new OrgStructureService(new OrgStructureRepository(structure), structure, new OrganizationUnitRepository(units), _users, new OrgChartTeamsAndTitlesTests.NoWorkTeams());
    }

    private TaskManagementDbContext TasksDb(bool restricted, Guid? userId) =>
        new(new DbContextOptionsBuilder<TaskManagementDbContext>().UseInMemoryDatabase("tasks-" + _db).Options, new Scope(restricted, userId));

    private TaskReportService Service(Guid userId, bool restricted = true) =>
        new(TasksDb(restricted, userId), new Scope(restricted, userId), OrgStructure(), _users, null, _history);

    private sealed class Scope(bool restricted, Guid? userId) : ITaskAccessScope
    {
        public bool IsRestricted => restricted;
        public Guid? UserId => userId;
        public IReadOnlyList<Guid> GroupIds => [];
        public Task EnsureLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public bool CanManage(TaskItem task) => !restricted || task.OwnerUserId == userId;
    }

    private sealed class History : ITaskPostHistory
    {
        public List<TaskPostPeriodDto> Periods { get; } = [];

        public Task<IReadOnlyList<TaskPostPeriodDto>> ForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaskPostPeriodDto>>(Periods.Where(p => taskIds.Contains(p.TaskId)).ToList());

        public Task<IReadOnlyList<TaskPostPeriodDto>> FindAsync(Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaskPostPeriodDto>>(Periods
                .Where(p => (userIds is null || userIds.Contains(p.UserId)) && (positionId is null || p.PositionId == positionId)).ToList());
    }

    private sealed class Users : IUserDirectory
    {
        private readonly List<UserContact> _users = [];
        public void Add(UserContact user) => _users.Add(user);

        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(_users.Where(u => userIds.Contains(u.Id)).ToList());

        public Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(_users.Where(u => u.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
