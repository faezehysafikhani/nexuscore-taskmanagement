using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 2 of the dynamic structure: the node API (nested nodes behind AllowNesting, the rules,
/// loops, the company, versions, deactivation, search, path, order), node types and rules, the
/// chart's new fields, the separate permissions and the impact preview.
/// </summary>
public sealed class OrgNodeApiTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // ------------------------------------------------------------------ the switch and the rules

    [Fact]
    public async Task WithNestingOff_NodesAreMadeAndMovedOnlyAtTheTop_AsBefore()
    {
        var w = new World(allowNesting: false);
        var company = await w.CompanyAsync("C1");
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: company), default));
        Assert.Null(it.ParentNodeId);
        Assert.Equal(company, it.CompanyId);
        // The plain «واحد» type, like every existing unit.
        Assert.Equal(w.PlainType, it.NodeTypeId);

        var under = await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل", ParentNodeId: it.Id), default);
        Assert.Equal("validation.error", under.Error.Code);
        var other = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: company), default));
        Assert.Equal("validation.error", (await w.Nodes.MoveNodeAsync(Tenant, other.Id, new MoveNodeRequest(it.Id), default)).Error.Code);

        // The chart is as before: top units, no children.
        var chart = Assert.Single((await w.Service.GetChartAsync(Tenant, null, default)).Value!);
        Assert.Equal(["معاونت فناوری", "معاونت مالی"], chart.Units.Select(u => u.Name));
        Assert.All(chart.Units, unit => Assert.Empty(unit.Children));
        Assert.Equal("normal", chart.UiLevel);
    }

    [Fact]
    public async Task WithNoRules_NodesStayFlat_EvenWithNestingOn()
    {
        var w = new World();
        var company = await w.CompanyAsync("C1");
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: company), default));
        var under = await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل", ParentNodeId: it.Id), default);
        Assert.Equal("validation.error", under.Error.Code);
        Assert.Contains("قاعده", under.Error.Message);
    }

    [Fact]
    public async Task Nodes_FollowTheRules_NeverLoop_AndTheChartShowsThemNested()
    {
        var w = await World.BankAsync();
        var (deputy, gd, committee) = (w.Type("deputy"), w.Type("gd"), w.Type("committee"));
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: deputy), default));
        var finance = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: deputy), default));
        var software = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل نرم‌افزار", ParentNodeId: it.Id, NodeTypeId: gd), default));
        Assert.Equal(it.Id, software.ParentNodeId);
        Assert.Equal(w.Company, software.CompanyId);

        // Not allowed by the rules: a general department at the top, a committee under a deputy.
        Assert.Equal("validation.error", (await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل سرگردان", CompanyId: w.Company, NodeTypeId: gd), default)).Error.Code);
        Assert.Equal("validation.error", (await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته", ParentNodeId: it.Id, NodeTypeId: committee), default)).Error.Code);
        // A type change that its children would not fit under.
        Assert.Equal("validation.error", (await w.Nodes.UpdateNodeAsync(Tenant, it.Id, new UpdateNodeRequest(it.Name, committee), default)).Error.Code);

        // Moved to the other deputy: fine. The deputy under its own department: a loop.
        var moved = await w.Ok(w.Nodes.MoveNodeAsync(Tenant, software.Id, new MoveNodeRequest(finance.Id), default));
        Assert.Equal(finance.Id, moved.ParentNodeId);
        Assert.Equal("validation.error", (await w.Nodes.MoveNodeAsync(Tenant, finance.Id, new MoveNodeRequest(software.Id), default)).Error.Code);
        Assert.Equal("validation.error", (await w.Nodes.MoveNodeAsync(Tenant, finance.Id, new MoveNodeRequest(finance.Id), default)).Error.Code);

        // The chart: the department under its deputy, with its type's colour and icon; only top nodes at the top.
        var chart = Assert.Single((await w.Service.GetChartAsync(Tenant, null, default)).Value!);
        Assert.Equal(["معاونت فناوری", "معاونت مالی"], chart.Units.Select(u => u.Name));
        var financeNode = chart.Units.Single(u => u.UnitId == finance.Id);
        var child = Assert.Single(financeNode.Children);
        Assert.Equal(software.Id, child.UnitId);
        Assert.Equal(finance.Id, child.ParentNodeId);
        Assert.Equal("#0369a1", child.NodeType!.Color);
        Assert.Equal("folder-tree", child.NodeType.Icon);
        Assert.False(child.NodeType.IsDomainBoundary);

        // A node under another is not linked to a company itself (only the top of each branch is).
        Assert.DoesNotContain((await w.Service.ListCompanyUnitsAsync(Tenant, default)).Value!, link => link.UnitId == software.Id);
        Assert.Equal("validation.error", (await w.Service.LinkUnitAsync(Tenant, software.Id, new LinkUnitRequest(w.Company), default)).Error.Code);

        // Every change is in the log.
        var log = await w.Repo.ListOrgChangeLogsAsync(Tenant, software.Id, 10, default);
        Assert.Equal(["move", "create"], log.Select(entry => entry.Action));
    }

    [Fact]
    public async Task ANodeWithPeopleOrPosts_DoesNotMoveToAnotherCompany()
    {
        var w = await World.BankAsync();
        var other = await w.CompanyAsync("C2");
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        await w.SeatAsync(it.Id, "معاون");
        var refused = await w.Nodes.MoveNodeAsync(Tenant, it.Id, new MoveNodeRequest(null, other), default);
        Assert.Equal("conflict", refused.Error.Code);

        var empty = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت خالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        Assert.Equal(other, (await w.Ok(w.Nodes.MoveNodeAsync(Tenant, empty.Id, new MoveNodeRequest(null, other), default))).CompanyId);
    }

    // ------------------------------------------------------------------ versions, deactivation, order, search

    [Fact]
    public async Task AnOldVersion_IsRefused_WithAClearMessage()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var stale = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);
        var refused = await w.Nodes.UpdateNodeAsync(Tenant, it.Id, new UpdateNodeRequest("نام تازه", null, stale), default);
        Assert.Equal("conflict", refused.Error.Code);
        Assert.Contains("تغییر داده", refused.Error.Message);
        Assert.Equal("validation.error", (await w.Nodes.UpdateNodeAsync(Tenant, it.Id, new UpdateNodeRequest("نام تازه", null, "not base64!"), default)).Error.Code);
        // The version it was read with: saved.
        Assert.Equal("نام تازه", (await w.Ok(w.Nodes.UpdateNodeAsync(Tenant, it.Id, new UpdateNodeRequest("نام تازه", null, it.RowVersion), default))).Name);
    }

    [Fact]
    public async Task Deactivating_IsRefusedWhileSomethingDependsOnIt_AndNothingIsDeleted()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var software = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل نرم‌افزار", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        await w.SeatAsync(software.Id, "مدیر کل");

        var withChild = await w.Nodes.DeactivateNodeAsync(Tenant, it.Id, new NodeVersionRequest(), default);
        Assert.Equal("conflict", withChild.Error.Code);
        Assert.Contains("اداره کل نرم‌افزار", withChild.Error.Message);
        var withPost = await w.Nodes.DeactivateNodeAsync(Tenant, software.Id, new NodeVersionRequest(), default);
        Assert.Equal("conflict", withPost.Error.Code);
        Assert.Contains("پست", withPost.Error.Message);

        var leaf = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل خالی", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        Assert.False((await w.Ok(w.Nodes.DeactivateNodeAsync(Tenant, leaf.Id, new NodeVersionRequest(), default))).IsActive);
        Assert.True((await w.Ok(w.Nodes.ActivateNodeAsync(Tenant, leaf.Id, new NodeVersionRequest(), default))).IsActive);
        Assert.Equal(3, (await w.Ok(w.Nodes.ListNodesAsync(Tenant, null, default))).Count);
    }

    [Fact]
    public async Task Siblings_KeepTheirOrder_AndSearchGivesThePath()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوري", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var a = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل الف", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        var b = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل ب", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        var c = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل پ", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        await w.Ok(w.Nodes.ReorderNodeAsync(Tenant, c.Id, new ReorderNodeRequest(0), default));
        var chart = Assert.Single((await w.Service.GetChartAsync(Tenant, null, default)).Value!);
        Assert.Equal([c.Id, a.Id, b.Id], chart.Units.Single().Children.Select(child => child.UnitId));

        // By name (Arabic ي / Persian ی alike), by post code and by who holds a post; with the path from the top.
        var holder = w.User("زهرا کریمی");
        var seat = await w.SeatAsync(b.Id, "کارشناس");
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, holder, new SetUserAssignmentRequest(w.Company, b.Id, (await w.Service.ListUnitPositionsAsync(Tenant, b.Id, default)).Value!.Single().PositionId, null), default));
        var byName = Assert.Single(await w.Ok(w.Nodes.SearchAsync(Tenant, "فناوری", null, default)));
        Assert.Equal(("name", it.Id), (byName.MatchedOn, byName.Node.Id));
        var byPerson = Assert.Single(await w.Ok(w.Nodes.SearchAsync(Tenant, "کریمی", null, default)));
        Assert.Equal(("person", b.Id), (byPerson.MatchedOn, byPerson.Node.Id));
        Assert.Equal([it.Id], byPerson.Path.Select(p => p.Id));
        var code = (await w.Service.ListUnitPositionsAsync(Tenant, b.Id, default)).Value!.Single(p => p.Id == seat).Code;
        Assert.Contains(await w.Ok(w.Nodes.SearchAsync(Tenant, code, null, default)), hit => hit.MatchedOn == "code" && hit.Node.Id == b.Id);
        Assert.Equal(["معاونت فناوري"], (await w.Ok(w.Nodes.GetAncestorsAsync(Tenant, b.Id, default))).Select(p => p.Name));
    }

    [Fact]
    public async Task SomeoneKeptToTheirCompany_NeitherSeesNorChangesAnotherCompanysNodes()
    {
        var w = await World.BankAsync();
        var other = await w.CompanyAsync("C2");
        var mineNode = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت من", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var theirs = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت دیگری", CompanyId: other, NodeTypeId: w.Type("deputy")), default));
        var me = w.User("کاربر شرکت اول");
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, me, new SetUserAssignmentRequest(w.Company, mineNode.Id, null, null), default));

        var kept = w.NodesAs(me);
        Assert.Equal([mineNode.Id], (await w.Ok(kept.ListNodesAsync(Tenant, null, default))).Select(n => n.Id));
        Assert.Equal("not_found", (await kept.GetNodeAsync(Tenant, theirs.Id, default)).Error.Code);
        Assert.Equal("not_found", (await kept.UpdateNodeAsync(Tenant, theirs.Id, new UpdateNodeRequest("x"), default)).Error.Code);
        Assert.Equal("not_found", (await kept.CreateNodeAsync(Tenant, new CreateNodeRequest("x", CompanyId: other, NodeTypeId: w.Type("deputy")), default)).Error.Code);
    }

    // ------------------------------------------------------------------ node types and rules

    [Fact]
    public async Task NodeTypes_HaveAColourAndIcon_AndChangingRulesListsMismatches_WithoutRemovingAnything()
    {
        var w = await World.BankAsync();
        Assert.Equal("validation.error", (await w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("بد", Color: "red"), default)).Error.Code);
        Assert.Equal("validation.error", (await w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("بد", Icon: "Not An Icon"), default)).Error.Code);
        Assert.Equal("conflict", (await w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("معاونت"), default)).Error.Code);
        var branch = await w.Ok(w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("شعبه", "branch", "#C2410C", "store", false), default));
        Assert.Equal(("branch", "#c2410c", "store", false), (branch.Code, branch.Color, branch.Icon, branch.IsDomainBoundary));

        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var gd = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل نرم‌افزار", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        Assert.Empty((await w.Ok(w.Nodes.GetRulesAsync(Tenant, default))).Conflicts);

        // «اداره کل under معاونت» removed: the department is listed, and stays where it is.
        var rules = (await w.Ok(w.Nodes.GetRulesAsync(Tenant, default))).Rules.Where(r => r.ChildNodeTypeId != w.Type("gd")).ToList();
        var after = await w.Ok(w.Nodes.SetRulesAsync(Tenant, new SetNodeTypeRulesRequest(rules), default));
        var conflict = Assert.Single(after.Conflicts);
        Assert.Equal((gd.Id, it.Id), (conflict.NodeId, conflict.ParentNodeId));
        Assert.Equal(it.Id, (await w.Ok(w.Nodes.GetNodeAsync(Tenant, gd.Id, default))).ParentNodeId);

        // A type in use is not deleted (deactivate it instead); an unused one is.
        Assert.Equal("conflict", (await w.Nodes.DeleteNodeTypeAsync(Tenant, w.Type("gd"), default)).Error.Code);
        Assert.True((await w.Nodes.DeleteNodeTypeAsync(Tenant, branch.Id, default)).IsSuccess);
    }

    // ------------------------------------------------------------------ permissions

    [Fact]
    public async Task EachOperation_HasItsOwnPermission_AndTheOlderPermissionStillWorks()
    {
        var services = new ServiceCollection().AddLogging().AddOrgStructure().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        async Task<bool> Allowed(string policy, params string[] permissions) =>
            (await authorization.AuthorizeAsync(
                new ClaimsPrincipal(new ClaimsIdentity(permissions.Select(p => new Claim("permission", p)), "test")), policy)).Succeeded;

        foreach (var (policy, accepted) in OrgStructurePermissions.NodePolicies)
        {
            Assert.True(await Allowed(policy, policy), $"{policy} with itself");
            foreach (var older in accepted)
            {
                Assert.True(await Allowed(policy, older), $"{policy} with {older}");
            }

            Assert.False(await Allowed(policy, "Tasks.View"), $"{policy} with an unrelated permission");
        }

        // One operation's permission is not another's.
        Assert.False(await Allowed(OrgStructurePermissions.NodeMove, OrgStructurePermissions.NodeCreate));
        Assert.False(await Allowed(OrgStructurePermissions.ManageRules, OrgStructurePermissions.NodeUpdate));
        // The node permissions also let their holder read the structure.
        Assert.True(await Allowed(OrgStructurePermissions.ReadPolicy, OrgStructurePermissions.NodeCreate));
    }

    // ------------------------------------------------------------------ impact preview

    [Fact]
    public async Task ThePreview_ShowsWhatAChangeDoes_AndChangesNothing()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var finance = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var software = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل نرم‌افزار", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        var deputyHead = w.User("معاون فناوری");
        var financeHead = w.User("معاون مالی");
        var expert = w.User("کارشناس نرم‌افزار");
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, it.Id, new SetNodeManagerRequest(deputyHead), default));
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, finance.Id, new SetNodeManagerRequest(financeHead), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, deputyHead, new SetUserAssignmentRequest(w.Company, it.Id, null, null), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, financeHead, new SetUserAssignmentRequest(w.Company, finance.Id, null, null), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, expert, new SetUserAssignmentRequest(w.Company, software.Id, null, null), default));
        Assert.Equal(deputyHead, (await w.Service.GetManagerAsync(Tenant, expert, default)).Value!.UserId);

        var logBefore = (await w.Repo.ListOrgChangeLogsAsync(Tenant, null, 1000, default)).Count;
        var preview = await w.Ok(w.Preview.PreviewAsync(Tenant,
            new ImpactPreviewRequest([new ImpactChangeRequest("move", software.Id, finance.Id)]), default));

        // The expert's manager changes, and each deputy's people change with it.
        var forExpert = preview.People.Single(p => p.Person.UserId == expert);
        Assert.Equal((deputyHead, financeHead), (forExpert.ManagerBefore!.UserId, forExpert.ManagerAfter!.UserId));
        Assert.Contains(preview.People.Single(p => p.Person.UserId == deputyHead).SubordinatesRemoved, s => s.UserId == expert);
        Assert.Contains(preview.People.Single(p => p.Person.UserId == financeHead).SubordinatesAdded, s => s.UserId == expert);
        Assert.Equal(preview.People.Count, preview.PeopleAffected);

        // An empty new node changes nobody; a loop is refused.
        Assert.Equal(0, (await w.Ok(w.Preview.PreviewAsync(Tenant,
            new ImpactPreviewRequest([new ImpactChangeRequest("add", ParentNodeId: it.Id, Name: "اداره کل تازه")]), default))).PeopleAffected);
        Assert.Equal("validation.error", (await w.Preview.PreviewAsync(Tenant,
            new ImpactPreviewRequest([new ImpactChangeRequest("move", it.Id, software.Id)]), default)).Error.Code);

        // Nothing was saved.
        Assert.Equal(deputyHead, (await w.Service.GetManagerAsync(Tenant, expert, default)).Value!.UserId);
        Assert.Equal(it.Id, (await w.Ok(w.Nodes.GetNodeAsync(Tenant, software.Id, default))).ParentNodeId);
        Assert.Equal(logBefore, (await w.Repo.ListOrgChangeLogsAsync(Tenant, null, 1000, default)).Count);
    }

    // ------------------------------------------------------------------ the world

    internal sealed class World
    {
        private readonly OrganizationDbContext _organization;
        private readonly OrgStructureDbContext _structure;
        private readonly Users _users = new();
        private readonly bool _allowNesting;
        private readonly List<Guid> _companies = [];
        private Guid? _top;

        public World(bool allowNesting = true)
        {
            _allowNesting = allowNesting;
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            _structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            Repo = new OrgStructureRepository(_structure);
            Service = new OrgStructureService(Repo, _structure, new OrganizationUnitRepository(_organization), _users, new NoTeams());
            Nodes = NodesAs(null);
            Preview = new OrgImpactPreview(Repo, new OrganizationUnitRepository(_organization), _users, new NoTeams(), Options());
            // As upgrade-10 leaves every organization: the «company» and plain «unit» types, no rules.
            _structure.NodeTypes.Add(new Nexus.OrgStructure.Domain.NodeType(Guid.NewGuid(), Tenant, "company", "شرکت", true, 0));
            var plain = new Nexus.OrgStructure.Domain.NodeType(Guid.NewGuid(), Tenant, OrgNodeService.PlainUnitCode, "واحد", true, 1);
            _structure.NodeTypes.Add(plain);
            _structure.SaveChanges();
            PlainType = plain.Id;
        }

        public OrgStructureRepository Repo { get; }
        public IOrgStructureService Service { get; }
        public IOrgNodeService Nodes { get; }
        public IOrgImpactPreview Preview { get; }
        public Guid PlainType { get; }
        public Guid Company { get; private set; }
        private readonly Dictionary<string, Guid> _types = [];

        public Guid Type(string code) => _types[code];

        private Microsoft.Extensions.Options.IOptions<OrgStructureOptions> Options(bool limitToOwnCompany = false) =>
            Microsoft.Extensions.Options.Options.Create(new OrgStructureOptions { AllowNesting = _allowNesting, LimitToOwnCompany = limitToOwnCompany });

        /// <summary>The node API as someone kept to their own company (null: not kept).</summary>
        public IOrgNodeService NodesAs(Guid? userId)
        {
            var who = new SignedIn(userId);
            return new OrgNodeService(Repo, _structure, new OrganizationUnitRepository(_organization), _organization, _users, Options(),
                companyReach: userId is null ? null : new CompanyReachScope(Repo, who, Options(limitToOwnCompany: true)), currentUser: who);
        }

        /// <summary>A company C1 and a bank's types and rules: deputies at the top, general departments under them, committees at the top.</summary>
        public static async Task<World> BankAsync()
        {
            var w = new World();
            w.Company = await w.CompanyAsync("C1");
            w._types["deputy"] = (await w.Ok(w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("معاونت", "deputy", "#0f766e", "building-2"), default))).Id;
            w._types["gd"] = (await w.Ok(w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("اداره کل", "gd", "#0369a1", "folder-tree", false), default))).Id;
            w._types["committee"] = (await w.Ok(w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("کمیته", "committee", "#7c3aed", "users-round"), default))).Id;
            await w.Ok(w.Nodes.SetRulesAsync(Tenant, new SetNodeTypeRulesRequest([
                new NodeTypeRuleDto(null, w._types["deputy"]),
                new NodeTypeRuleDto(null, w._types["committee"]),
                new NodeTypeRuleDto(w._types["deputy"], w._types["gd"]),
            ]), default));
            return w;
        }

        public async Task<T> Ok<T>(Task<Result<T>> call)
        {
            var result = await call;
            Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error.Code}: {result.Error.Message}");
            return result.Value!;
        }

        public Guid User(string name)
        {
            var id = Guid.NewGuid();
            _users.People[id] = name;
            return id;
        }

        public async Task<Guid> CompanyAsync(string code)
        {
            var id = (await Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت " + code, code, null), default)).Value!.Id;
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

        /// <summary>A post of a new position (under the head's) in the node; its id.</summary>
        public async Task<Guid> SeatAsync(Guid nodeId, string title)
        {
            var position = (await Service.CreatePositionAsync(Tenant, new CreatePositionRequest(title, _top, _companies.ToList()), default)).Value!.Id;
            return (await Ok(Service.AddUnitPositionAsync(Tenant, nodeId, new AddUnitPositionRequest(position), default))).Id;
        }
    }

    private sealed class SignedIn(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => Tenant;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class NoTeams : IWorkTeamDirectory
    {
        public Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkTeamInfo>>([]);
    }

    private sealed class Users : IUserDirectory
    {
        public Dictionary<Guid, string> People { get; } = [];

        private UserContact Contact(Guid id) => new(id, Tenant, People[id], null, null, null, null, false, true);

        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(userIds.Where(People.ContainsKey).Select(Contact).ToList());

        public Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(People.Keys.Select(Contact).ToList());

        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
