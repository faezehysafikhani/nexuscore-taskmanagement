using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Identity.Interfaces;
using Nexus.OrgStructure.Permissions;
using NexusCore.Infrastructure.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>
/// Golden tests of the organization structure on today's flat data (no unit has a parent): who
/// manages whom, whom each person may give tasks to, who stands in for vacant posts, whom a team
/// builder may add, each company's head, and each person's company reach. They record what the
/// code does now; the plan to make units nested must keep every line of them unchanged.
/// To re-record after a deliberate change: set ORG_GOLDEN_RECORD to a directory.
/// </summary>
public sealed class OrgGoldenTests
{

    private static readonly Guid Tenant = Guid.NewGuid();
    internal static Guid TenantId => Tenant;
    private static readonly string[] Who = ["head", "dep", "gm", "e1", "e2", "fin", "fe", "h2", "c2", "nowhere"];

    /// <summary>
    /// Flat layouts (no unit has a parent). A: every unit has a manager. B: the office has none.
    /// C: neither the deputy nor the office has one. Company C1 has the chart; company C2 has a
    /// head and one person, to show the company limit.
    /// </summary>
    private sealed class Layout
    {
        public GoldenWorld W = new();
        public Dictionary<string, Guid> P = new();
        public Dictionary<string, Guid> Seat = new();
        public Guid C1, C2;
    }

    private static async Task<Layout> BuildAsync(string layout)
    {
        var l = new Layout();
        var w = l.W;
        foreach (var n in Who) l.P[n] = w.User(n);
        l.C1 = await w.CompanyAsync("C1");
        l.C2 = await w.CompanyAsync("C2");
        await w.HeadAsync(l.C1, l.P["head"]);
        await w.HeadAsync(l.C2, l.P["h2"]);
        var techManager = layout == "C" ? null : (Guid?)l.P["dep"];
        var officeManager = layout == "A" ? (Guid?)l.P["gm"] : null;
        var tech = w.Unit("معاونت فناوری", techManager);
        var office = w.Unit("اداره کل نرم‌افزار", officeManager);
        var finance = w.Unit("معاونت مالی", l.P["fin"]);
        var second = w.Unit("واحد شرکت دوم", null);
        foreach (var unit in new[] { tech, office, finance })
            Assert.True((await w.Service.LinkUnitAsync(Tenant, unit.Id, new LinkUnitRequest(l.C1), default)).IsSuccess);
        Assert.True((await w.Service.LinkUnitAsync(Tenant, second.Id, new LinkUnitRequest(l.C2), default)).IsSuccess);

        async Task<Guid> PositionUnder(string title, Guid parent, params Guid[] companies) =>
            (await w.Service.CreatePositionAsync(Tenant, new CreatePositionRequest(title, parent, companies.ToList()), default)).Value!.Id;
        var mgrPos = await PositionUnder("معاون", w.TopPosition, l.C1);
        var gmPos = await PositionUnder("مدیرکل", mgrPos, l.C1);
        var expPos = await PositionUnder("کارشناس", gmPos, l.C1);
        var c2Pos = await PositionUnder("کارشناس شرکت دوم", w.TopPosition, l.C2);
        async Task<Guid> Seat(string name, Guid unit, Guid pos)
        {
            var id = (await w.Service.AddUnitPositionAsync(Tenant, unit, new AddUnitPositionRequest(pos), default)).Value!.Id;
            l.Seat[name] = id;
            return id;
        }
        async Task Place(string who, Guid company, Guid unit, Guid pos, Guid seat) =>
            Assert.True((await w.Service.SetAssignmentAsync(Tenant, l.P[who], new SetUserAssignmentRequest(company, unit, pos, null, [seat]), default)).IsSuccess, who);
        await Place("dep", l.C1, tech.Id, mgrPos, await Seat("dep", tech.Id, mgrPos));
        await Place("gm", l.C1, office.Id, gmPos, await Seat("gm", office.Id, gmPos));
        await Place("e1", l.C1, office.Id, expPos, await Seat("e1", office.Id, expPos));
        await Place("e2", l.C1, office.Id, expPos, await Seat("e2", office.Id, expPos));
        await Place("fin", l.C1, finance.Id, mgrPos, await Seat("fin", finance.Id, mgrPos));
        await Place("fe", l.C1, finance.Id, expPos, await Seat("fe", finance.Id, expPos));
        await Place("c2", l.C2, second.Id, c2Pos, await Seat("c2", second.Id, c2Pos));
        return l;
    }

    private static string Names(Layout l, IEnumerable<Guid> ids) =>
        string.Join(",", ids.Select(id => l.P.FirstOrDefault(p => p.Value == id).Key ?? "?").OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Everything the plan must keep unchanged, one line per fact, sorted.</summary>
    private static async Task<string> CaptureAsync(Layout l)
    {
        var w = l.W;
        var o = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var options = Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { LimitTaskAssignment = true });
        foreach (var n in Who)
        {
            var id = l.P[n];
            var manager = (await w.Service.GetManagerAsync(Tenant, id, default)).Value;
            o[$"01 manager[{n}]"] = manager is null ? "-" : $"{Names(l, [manager.UserId])}({manager.Source})";
            var chain = (await w.Service.GetPlacementAsync(Tenant, id, default)).Value!.ManagementChain;
            o[$"02 chain[{n}]"] = string.Join(">", chain.Select(p => Names(l, [p.UserId])));
            var direct = (await w.Service.GetSubordinatesAsync(Tenant, id, false, default)).Value!;
            var all = (await w.Service.GetSubordinatesAsync(Tenant, id, true, default)).Value!;
            o[$"03 direct[{n}]"] = Names(l, direct.Select(s => s.UserId));
            o[$"04 below[{n}]"] = string.Join(",", all.OrderBy(s => s.Depth).ThenBy(s => Names(l, [s.UserId])).Select(s => $"{Names(l, [s.UserId])}:{s.Depth}"));
            var limit = await w.AssignmentScope(id, true).GetLimitAsync(Tenant, id, default);
            o[$"05 mayNotAssign[{n}]"] = limit is null ? "(unlimited)" : Names(l, limit.BlockedUserIds);
            var team = await w.MemberScope(id).GetLimitAsync(Tenant, id, default);
            o[$"07 teamMayAdd[{n}]"] = team is null ? "(anyone)" : $"{Names(l, team.AllowedUserIds)}|{team.ScopeName}|{team.IsCompany}";
            var reach = await w.Reach(id, true).GetReachAsync(Tenant, id, default);
            o[$"08 reach[{n}]"] = reach is null ? "(all)" : $"{Names(l, reach.UserIds)}|elsewhere:{Names(l, reach.PlacedElsewhere)}";
        }
        // the posts' superiors in the substitution inbox: SubstitutionService's own SuperiorsOf
        var sub = (SubstitutionService)w.Substitution(l.P["head"]);
        var load = typeof(SubstitutionService).GetMethod("LoadAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var task = (Task)load.Invoke(sub, [Tenant, CancellationToken.None])!;
        await task;
        var state = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var superiors = state.GetType().GetMethod("SuperiorsOf")!;
        foreach (var (name, seat) in l.Seat)
            o[$"06 superiorsOfSeat[{name}]"] = string.Join(">", ((IReadOnlyList<Guid>)superiors.Invoke(state, [seat])!).Select(id => Names(l, [id])));
        // the head of each company, and the head's post in the chart
        foreach (var company in (await w.Service.GetChartAsync(Tenant, null, default)).Value!.OrderBy(c => c.Code))
        {
            o[$"09 companyHead[{company.Code}]"] = company.Manager is null ? "-" : Names(l, [company.Manager.UserId]);
            o[$"10 headPost[{company.Code}]"] = string.Join(",", company.Positions!.Where(p => p.IsHead).Select(p => $"{p.Title}:{Names(l, p.Holders.Select(h => h.UserId))}"));
            o[$"11 chartUnits[{company.Code}]"] = string.Join(",", company.Units.Select(u => $"{u.Name}(mgr:{(u.Manager is null ? "-" : Names(l, [u.Manager.UserId]))};children:{u.Children.Count};posts:{u.Positions!.Count})").OrderBy(x => x, StringComparer.Ordinal));
        }
        return string.Join("\n", o.Select(kv => $"{kv.Key} = {kv.Value}"));
    }

    private static async Task CheckAsync(string name, Layout l)
    {
        var actual = await CaptureAsync(l);
        if (Environment.GetEnvironmentVariable("ORG_GOLDEN_RECORD") is { Length: > 0 } dir)
            File.WriteAllText(Path.Combine(dir, $"golden-{name}.txt"), actual);
        Assert.True(OrgGoldenExpected.Data.TryGetValue(name, out var expected), $"no golden for {name}");
        Assert.Equal(expected!.Replace("\r\n", "\n").Trim(), actual.Trim());
    }

    [Fact] public async Task Golden_FlatLayoutA_ManagersOnAllUnits() => await CheckAsync("A", await BuildAsync("A"));
    [Fact] public async Task Golden_FlatLayoutB_NoManagerOnOneUnit() => await CheckAsync("B", await BuildAsync("B"));
    [Fact] public async Task Golden_FlatLayoutC_NoManagerOnTwoUnits() => await CheckAsync("C", await BuildAsync("C"));

    [Fact]
    public async Task Golden_FlatLayoutA_AfterTheCompanyHeadLeaves()
    {
        var l = await BuildAsync("A");
        await l.W.DeactivatedAsync(l.P["head"]);
        await CheckAsync("A-headLeaves", l);
    }

    [Fact]
    public async Task Golden_IsRepeatable_TheSameLayoutGivesTheSameFacts()
    {
        Assert.Equal(await CaptureAsync(await BuildAsync("B")), await CaptureAsync(await BuildAsync("B")));
    }

    public sealed class RecordingUpdater : ITaskPeopleUpdater
    {
        public List<TaskPersonChange> Applied { get; } = [];
        public List<TaskPeopleSnapshot> Tasks { get; } = [];

        public Task ApplyAsync(Guid tenantId, IReadOnlyList<TaskPersonChange> changes, CancellationToken cancellationToken)
        {
            Applied.AddRange(changes);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskPeopleSnapshot>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaskPeopleSnapshot>>(Tasks);

        public List<TaskSummary> Summaries { get; } = [];

        public Task<IReadOnlyList<TaskSummary>> GetSummariesAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaskSummary>>(Summaries.Where(s => taskIds.Contains(s.TaskId)).ToList());
    }

    internal sealed class GoldenWorld
    {
        private readonly OrganizationDbContext _organization;
        private readonly OrgStructureRepository _repository;
        private readonly OrgStructureDbContext _structure;
        private readonly Directory _users = new();
        private readonly WorkTeams _teams = new();

        public GoldenWorld()
        {
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            var structure = _structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            var repository = _repository = new OrgStructureRepository(structure);
            var updaters = new ServiceCollection().AddSingleton<ITaskPeopleUpdater>(Updater).BuildServiceProvider();
            TaskPosts = new TaskPostDirectory(
                repository, structure, new OrganizationUnitRepository(_organization),
                Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { AssignTasksToPosts = true }), updaters);
            Service = new OrgStructureService(repository, structure, new OrganizationUnitRepository(_organization), _users, _teams, TaskPosts);
            Titles = new HonorificService(repository, structure, _users, _users);
        }

        public IOrgStructureService Service { get; }
        public OrgStructureRepository Repo => _repository;
        public OrgStructureDbContext Structure => _structure;
        public OrganizationDbContext Org => _organization;
        public Directory Users => _users;
        public WorkTeams TeamDir => _teams;
        public IHonorificService Titles { get; }
        public TaskPostDirectory TaskPosts { get; }
        public RecordingUpdater Updater { get; } = new();

        /// <summary>Who was on each task through its posts, and when.</summary>
        public ITaskPostHistory History => new TaskPostHistory(_repository);

        public string NameOf(Guid userId) => _users.Names[userId];

        /// <summary>The substitution inbox as <paramref name="signedIn"/> sees it, holding <paramref name="permissions"/>.</summary>
        public ISubstitutionService Substitution(Guid signedIn, params string[] permissions) =>
            new SubstitutionService(_repository, _structure, new OrganizationUnitRepository(_organization), TaskPosts,
                new SignedIn(signedIn, permissions), _users, new ServiceCollection().AddSingleton<ITaskPeopleUpdater>(Updater).BuildServiceProvider());

        /// <summary>The account was disabled.</summary>
        public Task DeactivatedAsync(Guid userId) =>
            new PostReleaseOnDeactivation(_repository, _structure, TaskPosts).UserDeactivatedAsync(Tenant, userId, default);

        /// <summary>Whom <paramref name="signedIn"/> may add to their work teams, holding <paramref name="permissions"/>.</summary>
        public IWorkTeamMemberScope MemberScope(Guid signedIn, params string[] permissions) =>
            new DeputyWorkTeamMemberScope(_repository, new OrganizationUnitRepository(_organization), new SignedIn(signedIn, permissions));

        /// <summary>Whom <paramref name="signedIn"/> reaches, with the company limit turned <paramref name="on"/>.</summary>
        public CompanyReachScope Reach(Guid signedIn, bool on = true, params string[] permissions) =>
            new(_repository, new SignedIn(signedIn, permissions),
                Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { LimitToOwnCompany = on }));

        /// <summary>The structure as <paramref name="signedIn"/> sees and changes it, kept to their own company.</summary>
        public IOrgStructureService ServiceAs(Guid signedIn, params string[] permissions) =>
            new OrgStructureService(_repository, _structure, new OrganizationUnitRepository(_organization), _users, _teams, TaskPosts,
                Reach(signedIn, true, permissions), new SignedIn(signedIn, permissions));

        /// <summary>Whom <paramref name="signedIn"/> may add to their work teams, kept to their own company.</summary>
        public IWorkTeamMemberScope KeptMemberScope(Guid signedIn, params string[] permissions) =>
            new DeputyWorkTeamMemberScope(_repository, new OrganizationUnitRepository(_organization), new SignedIn(signedIn, permissions),
                Reach(signedIn, true, permissions));

        /// <summary>The posts tasks may be given to, as <paramref name="signedIn"/> (kept to their own company) sees them.</summary>
        public Task<IReadOnlyList<WorkPost>> PostsAsSeenByAsync(Guid signedIn)
        {
            var who = new SignedIn(signedIn, []);
            var provider = new ServiceCollection()
                .AddSingleton<ICurrentUserContext>(who)
                .AddSingleton(new CompanyReachScope(_repository, who,
                    Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { LimitToOwnCompany = true })))
                .BuildServiceProvider();
            return new TaskPostDirectory(_repository, _structure, new OrganizationUnitRepository(_organization),
                Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { AssignTasksToPosts = true }), provider).ListPostsAsync(Tenant, default);
        }

        /// <summary>Whom <paramref name="signedIn"/> may not give tasks to, with the limit turned <paramref name="on"/>.</summary>
        public IWorkAssignmentScope AssignmentScope(Guid signedIn, bool on, params string[] permissions) =>
            new HierarchyWorkAssignmentScope(
                _repository, new OrganizationUnitRepository(_organization), new SignedIn(signedIn, permissions),
                Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { LimitTaskAssignment = on }));

        public void Team(string name, Guid owner, params Guid[] members) =>
            _teams.Teams.Add(new WorkTeamInfo(Guid.NewGuid(), name, null, owner, members));

        public Guid User(string name)
        {
            var id = Guid.NewGuid();
            _users.Names[id] = name;
            _users.Bare[id] = name;
            return id;
        }

        public OrganizationUnit Unit(string name, Guid? managerUserId = null)
        {
            var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, name, managerUserId);
            _organization.OrganizationUnits.Add(unit);
            _organization.SaveChanges();
            return unit;
        }


        private readonly List<Guid> _companies = [];
        private Guid? _top;

        /// <summary>The head's position (no superior), for every company made so far.</summary>
        public Guid TopPosition => _top!.Value;

        /// <summary>A position under the head's, for every company made so far.</summary>
        public async Task<Guid> PositionAsync(string title) =>
            (await Service.CreatePositionAsync(Tenant, new CreatePositionRequest(title, _top, _companies.ToList()), default)).Value!.Id;

        /// <summary>Makes the user the company's head: placed directly in it with the head's position.</summary>
        public async Task HeadAsync(Guid companyId, Guid userId) =>
            Assert.True((await Service.SetAssignmentAsync(Tenant, userId, new SetUserAssignmentRequest(companyId, null, _top, null), default)).IsSuccess);

        private async Task<Guid> AddCompanyAsync(string name, string code)
        {
            var id = (await Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest(name, code, null), default)).Value!.Id;
            _companies.Add(id);
            if (_top is null)
            {
                _top = (await Service.CreatePositionAsync(Tenant, new CreatePositionRequest("رئیس کل", null, [id]), default)).Value!.Id;
            }
            else
            {
                Assert.True((await Service.UpdatePositionAsync(Tenant, _top.Value, new UpdatePositionRequest("رئیس کل", true, null, _companies.ToList()), default)).IsSuccess);
            }

            return id;
        }

        /// <summary>Gives the unit a position (created with this title) and returns the unit position's id.</summary>
        public async Task<Guid> SeatAsync(Guid unitId, string title) =>
            (await Service.AddUnitPositionAsync(Tenant, unitId, new AddUnitPositionRequest(await PositionAsync(title)), default)).Value!.Id;

        public async Task<Guid> SeatOfAsync(Guid unitId, Guid positionId) =>
            (await Service.ListUnitPositionsAsync(Tenant, unitId, default)).Value!.Single(p => p.PositionId == positionId).Id;

        public Task<Guid> CompanyAsync(string code) => AddCompanyAsync("شرکت " + code, code);
    }

    internal sealed class SignedIn(Guid userId, string[] permissions) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => Tenant;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    internal sealed class WorkTeams : IWorkTeamDirectory
    {
        public List<WorkTeamInfo> Teams { get; } = [];

        public Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkTeamInfo>>(Teams);
    }

    /// <summary>No work teams at all (tests about other parts of the structure).</summary>
    public sealed class GoldenNoWorkTeams : IWorkTeamDirectory
    {
        public Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkTeamInfo>>([]);
    }

    /// <summary>The identity side: users' names, and the title writer working on them like the real one.</summary>
    internal sealed class Directory : IUserDirectory, IUserTitleWriter
    {
        public Dictionary<Guid, string> Names { get; } = [];

        /// <summary>The first and last name, without a title.</summary>
        public Dictionary<Guid, string> Bare { get; } = [];

        private UserContact Contact(Guid id) => new(id, Tenant, Names[id], null, null, null, null, false, true);

        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(userIds.Where(Names.ContainsKey).Select(Contact).ToList());

        public Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(Names.Keys.Select(Contact).ToList());

        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result> ApplyTitleAsync(Guid tenantId, Guid userId, string? previousTitle, string? title, CancellationToken cancellationToken)
        {
            var name = Bare[userId];
            Names[userId] = string.IsNullOrWhiteSpace(title) ? name : $"{title} {name}";
            return Task.FromResult(Result.Success());
        }
    }
}
