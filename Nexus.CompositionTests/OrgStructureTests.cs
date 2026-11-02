using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Organization;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>Companies, positions and the manager/subordinate hierarchy (Nexus.OrgStructure).</summary>
public sealed class OrgStructureTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    // ------------------------------------------------------------------ the rules themselves

    [Fact]
    public void Manager_IsExplicit_ThenUnitManager_ThenParentUnitManager_ThenCompanyHead()
    {
        Guid company = Guid.NewGuid(), deputy = Guid.NewGuid(), office = Guid.NewGuid();
        Guid ceo = Guid.NewGuid(), deputyHead = Guid.NewGuid(), officeHead = Guid.NewGuid(), clerk = Guid.NewGuid(), assistant = Guid.NewGuid();

        var resolver = new HierarchyResolver(
            [new UnitNode(deputy, null, deputyHead), new UnitNode(office, deputy, officeHead)],
            new Dictionary<Guid, Guid> { [deputy] = company },
            new Dictionary<Guid, Guid?> { [company] = ceo },
            [
                new AssignmentNode(clerk, company, office, null),
                new AssignmentNode(officeHead, company, office, null),
                new AssignmentNode(deputyHead, company, deputy, null),
                new AssignmentNode(ceo, company, null, null),
                new AssignmentNode(assistant, company, office, ceo),
            ]);

        Assert.Equal(new ResolvedManager(officeHead, ManagerSource.UnitManager), resolver.ManagerOf(clerk));
        // The head of a unit reports to the head of the unit above.
        Assert.Equal(new ResolvedManager(deputyHead, ManagerSource.UnitManager), resolver.ManagerOf(officeHead));
        // The head of the company's top unit reports to the company's head.
        Assert.Equal(new ResolvedManager(ceo, ManagerSource.CompanyManager), resolver.ManagerOf(deputyHead));
        Assert.Null(resolver.ManagerOf(ceo));
        // An explicit manager wins over the unit's.
        Assert.Equal(new ResolvedManager(ceo, ManagerSource.Explicit), resolver.ManagerOf(assistant));

        Assert.Equal([officeHead, deputyHead, ceo], resolver.ManagementChainOf(clerk));
        var all = resolver.AllSubordinatesOf(ceo).ToDictionary(x => x.UserId, x => x.Depth);
        Assert.Equal(4, all.Count);
        Assert.Equal(1, all[deputyHead]);
        Assert.Equal(1, all[assistant]);
        Assert.Equal(2, all[officeHead]);
        Assert.Equal(3, all[clerk]);
        Assert.Equal([clerk], resolver.DirectSubordinatesOf(officeHead));
    }

    [Fact]
    public void Manager_NeverComesFromAUnitAboveTheCompanysOwnTopUnit()
    {
        Guid company = Guid.NewGuid(), holdingUnit = Guid.NewGuid(), deputy = Guid.NewGuid();
        Guid holdingBoss = Guid.NewGuid(), deputyHead = Guid.NewGuid();

        var resolver = new HierarchyResolver(
            [new UnitNode(holdingUnit, null, holdingBoss), new UnitNode(deputy, holdingUnit, deputyHead)],
            new Dictionary<Guid, Guid> { [deputy] = company },
            new Dictionary<Guid, Guid?> { [company] = null },
            [new AssignmentNode(deputyHead, company, deputy, null)]);

        Assert.Null(resolver.ManagerOf(deputyHead));
        Assert.Equal(company, resolver.CompanyOfUnit(deputy));
        Assert.Null(resolver.CompanyOfUnit(holdingUnit));
    }

    [Fact]
    public void LoopingData_NeverHangs()
    {
        Guid company = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var resolver = new HierarchyResolver(
            [],
            new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, Guid?>(),
            [new AssignmentNode(a, company, null, b), new AssignmentNode(b, company, null, c), new AssignmentNode(c, company, null, a)]);

        Assert.Equal([b, c], resolver.ManagementChainOf(a));
        Assert.Equal(2, resolver.AllSubordinatesOf(a).Count);
        Assert.NotNull(resolver.FindLoop());
    }

    // ------------------------------------------------------------------ the service

    [Fact]
    public async Task Companies_AreCreatedPerTenant_WithUniqueCodes()
    {
        var world = new World();
        var created = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest(" بانک تجارت ", "TJ", null), default)).Value!;
        Assert.Equal("بانک تجارت", created.Name);

        var duplicate = await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("دیگر", "TJ", null), default);
        Assert.Equal("conflict", duplicate.Error.Code);

        // Same code in another tenant is fine, and neither tenant sees the other's companies.
        Assert.True((await world.Service.CreateCompanyAsync(OtherTenant, new CreateCompanyRequest("X", "TJ", null), default)).IsSuccess);
        Assert.Single((await world.Service.ListCompaniesAsync(Tenant, default)).Value!);
        Assert.Equal("not_found", (await world.Service.GetCompanyAsync(OtherTenant, created.Id, default)).Error.Code);
    }

    [Fact]
    public async Task Units_BelongToACompany_ThroughTheirTopUnit()
    {
        var world = new World();
        var company = await world.CompanyAsync("TJ");
        var other = await world.CompanyAsync("SUB");
        var deputy = world.Unit("معاونت فناوری");
        var office = world.Unit("اداره نرم‌افزار", deputy.Id);

        Assert.True((await world.Service.LinkUnitAsync(Tenant, deputy.Id, new LinkUnitRequest(company), default)).IsSuccess);

        // The office already belongs to the company through its deputy.
        Assert.Equal("validation.error", (await world.Service.LinkUnitAsync(Tenant, office.Id, new LinkUnitRequest(other), default)).Error.Code);

        var user = world.User("کارشناس");
        Assert.True((await world.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(company, office.Id, null, null), default)).IsSuccess);

        // ...so the user can't be placed there under another company,
        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(other, office.Id, null, null), default)).Error.Code);
        // and the deputy can't be moved or unlinked while people sit in its branch.
        Assert.Equal("conflict", (await world.Service.LinkUnitAsync(Tenant, deputy.Id, new LinkUnitRequest(other), default)).Error.Code);
        Assert.Equal("conflict", (await world.Service.UnlinkUnitAsync(Tenant, deputy.Id, default)).Error.Code);
        Assert.Equal("conflict", (await world.Service.DeleteCompanyAsync(Tenant, company, default)).Error.Code);

        // A unit with nothing linked under it and no company can't hold the user.
        var loose = world.Unit("واحد بدون شرکت");
        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(company, loose.Id, null, null), default)).Error.Code);
    }

    [Fact]
    public async Task Assignment_GivesPositionAndManager_AndTheChartAndSubordinatesFollow()
    {
        var world = new World();
        var head = world.User("مدیرعامل");
        var company = await world.CompanyAsync("TJ", head);
        var deputyHead = world.User("معاون");
        var deputy = world.Unit("معاونت", managerUserId: deputyHead);
        var expert = world.User("کارشناس");
        await world.Service.LinkUnitAsync(Tenant, deputy.Id, new LinkUnitRequest(company), default);
        var position = (await world.Service.CreatePositionAsync(Tenant, new CreatePositionRequest("کارشناس ارشد", world.TopPosition, [company]), default)).Value!;

        await world.Service.SetAssignmentAsync(Tenant, deputyHead, new SetUserAssignmentRequest(company, deputy.Id, null, null), default);
        var placed = (await world.Service.SetAssignmentAsync(Tenant, expert, new SetUserAssignmentRequest(company, deputy.Id, position.Id, null), default)).Value!;

        Assert.Equal("کارشناس ارشد", placed.PositionTitle);
        Assert.Equal("معاونت", placed.UnitName);
        Assert.Equal(deputyHead, placed.Manager!.UserId);
        Assert.Equal("UnitManager", placed.Manager.Source);

        var mine = (await world.Service.GetPlacementAsync(Tenant, expert, default)).Value!;
        Assert.Equal([deputyHead, head], mine.ManagementChain.Select(p => p.UserId));

        var direct = (await world.Service.GetSubordinatesAsync(Tenant, head, includeIndirect: false, default)).Value!;
        Assert.Equal([deputyHead], direct.Select(s => s.UserId));
        var all = (await world.Service.GetSubordinatesAsync(Tenant, head, includeIndirect: true, default)).Value!;
        Assert.Equal([deputyHead, expert], all.Select(s => s.UserId));
        Assert.Equal(2, all.Single(s => s.UserId == expert).Depth);

        var chart = Assert.Single((await world.Service.GetChartAsync(Tenant, null, default)).Value!);
        Assert.Equal(head, chart.Manager!.UserId);
        var unit = Assert.Single(chart.Units);
        // The expert shows under their position; the deputy's head, with no position, as a member.
        Assert.Equal(deputyHead, Assert.Single(unit.Members).UserId);
        var held = Assert.Single(unit.Positions!);
        Assert.Equal("کارشناس ارشد", held.Title);
        Assert.Equal(expert, Assert.Single(held.Holders).UserId);
        Assert.Equal(2, unit.PeopleCount);

        Assert.Equal("conflict", (await world.Service.DeletePositionAsync(Tenant, position.Id, default)).Error.Code);
    }

    [Fact]
    public async Task ManagementLoops_AreRejected_WhetherExplicitOrThroughAUnitsManager()
    {
        var world = new World();
        var company = await world.CompanyAsync("TJ");
        var a = world.User("الف");
        var b = world.User("ب");

        Assert.True((await world.Service.SetAssignmentAsync(Tenant, a, new SetUserAssignmentRequest(company, null, null, b), default)).IsSuccess);
        Assert.Equal("conflict", (await world.Service.SetAssignmentAsync(Tenant, b, new SetUserAssignmentRequest(company, null, null, a), default)).Error.Code);
        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(Tenant, b, new SetUserAssignmentRequest(company, null, null, b), default)).Error.Code);

        // a manages a unit; c sits in it, so c's manager is a. Making c a's manager would loop.
        var world2 = new World();
        var company2 = await world2.CompanyAsync("TJ");
        var boss = world2.User("رئیس");
        var unit = world2.Unit("اداره", managerUserId: boss);
        await world2.Service.LinkUnitAsync(Tenant, unit.Id, new LinkUnitRequest(company2), default);
        var c = world2.User("ج");
        await world2.Service.SetAssignmentAsync(Tenant, c, new SetUserAssignmentRequest(company2, unit.Id, null, null), default);
        Assert.Equal("conflict", (await world2.Service.SetAssignmentAsync(Tenant, boss, new SetUserAssignmentRequest(company2, null, null, c), default)).Error.Code);

        // Same through the company head: boss sits at company level (reporting to its head), and
        // making d - who reports to boss - the head would close the loop.
        Assert.True((await world2.Service.SetAssignmentAsync(Tenant, boss, new SetUserAssignmentRequest(company2, null, null, null), default)).IsSuccess);
        var d = world2.User("د");
        Assert.Equal("conflict", (await world2.Service.SetAssignmentAsync(Tenant, d, new SetUserAssignmentRequest(company2, null, world2.TopPosition, boss), default)).Error.Code);
    }

    [Fact]
    public async Task OnlyUsersOfTheSameTenant_CanBePlacedOrBeManagers()
    {
        var world = new World();
        var company = await world.CompanyAsync("TJ");
        var local = world.User("محلی");
        var foreign = world.User("خارجی", OtherTenant);

        Assert.Equal("not_found", (await world.Service.SetAssignmentAsync(Tenant, foreign, new SetUserAssignmentRequest(company, null, null, null), default)).Error.Code);
        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(Tenant, local, new SetUserAssignmentRequest(company, null, null, foreign), default)).Error.Code);
    }

    // ------------------------------------------------------------------ optional by design

    [Fact]
    public void Module_Composes_AndAddsItsPermissions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=CompositionTests;Trusted_Connection=True;TrustServerCertificate=True" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddOrganizationApplication();
        services.AddOrganizationInfrastructure(configuration);
        services.AddOrgStructure();
        services.AddOrgStructureInfrastructure(configuration);

        services.AddSingleton<IConfiguration>(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOrgStructureService>());
        Assert.Contains(provider.GetServices<IPermissionCatalog>().SelectMany(c => c.GetPermissions()), p => p.Name == "org_structure.manage_assignments");
    }

    [Fact]
    public void NothingShared_DependsOnTheModule_SoOtherProductsNeverCarryIt()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "NexusCore.sln")))
        {
            root = root.Parent!;
        }

        foreach (var csproj in Directory.GetFiles(root.FullName, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(csproj);
            // Optional integrations built on the module (and the tests) may reference it.
            if (name.StartsWith("Nexus.OrgStructure", StringComparison.Ordinal)
                || name is "Nexus.CompositionTests" or "Nexus.Integrations.TaskReporting" or "Nexus.Integrations.CalendarOrgStructure")
            {
                continue;
            }

            Assert.DoesNotMatch(new Regex(@"Include=""[^""]*Nexus\.OrgStructure"), File.ReadAllText(csproj));
        }
    }

    // ------------------------------------------------------------------ test world

    private sealed class World
    {
        private readonly OrganizationDbContext _organization;
        private readonly FakeUserDirectory _users = new();

        public World()
        {
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            Service = new OrgStructureService(new OrgStructureRepository(structure), structure, new OrganizationUnitRepository(_organization), _users, new OrgChartTeamsAndTitlesTests.NoWorkTeams());
        }

        public IOrgStructureService Service { get; }

        public Guid User(string name, Guid? tenantId = null)
        {
            var id = Guid.NewGuid();
            _users.Add(new UserContact(id, tenantId ?? Tenant, name, null, null, null, null, false, true));
            return id;
        }

        public OrganizationUnit Unit(string name, Guid? parentId = null, Guid? managerUserId = null)
        {
            var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, name, managerUserId);
            unit.Update(unit.Name, managerUserId, isActive: true);
            // Units sit under units again (the dynamic structure): the parent is real.
            unit.SetParent(parentId);
            _organization.OrganizationUnits.Add(unit);
            _organization.SaveChanges();
            return unit;
        }

        public async Task<Guid> CompanyAsync(string code, Guid? headUserId = null)
        {
            var id = await AddCompanyAsync("شرکت " + code, code);
            if (headUserId is { } head)
            {
                await HeadAsync(id, head);
            }

            return id;
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

    }

    private sealed class FakeUserDirectory : IUserDirectory
    {
        private readonly List<UserContact> _users = [];

        public void Add(UserContact user) => _users.Add(user);

        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(_users.Where(u => userIds.Contains(u.Id)).ToList());

        public Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(_users.Where(u => u.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
