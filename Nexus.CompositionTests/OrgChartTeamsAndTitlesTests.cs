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

/// <summary>Work teams in the org chart, and titles before people's names (Nexus.OrgStructure).</summary>
public sealed class OrgChartTeamsAndTitlesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // ------------------------------------------------------------------ whom a user may give tasks to

    /// <summary>
    /// The company «C1»: head; deputies tech (head d1; senior s1 above expert e2) and finance (head d2;
    /// expert e3 with no position); and someone with no place.
    /// </summary>
    private static async Task<(World World, Dictionary<string, Guid> People)> HierarchyAsync()
    {
        var world = new World();
        var p = new[] { "head", "d1", "d2", "s1", "e2", "e3", "nowhere" }.ToDictionary(n => n, n => world.User(n));
        var tech = world.Unit("معاونت فناوری", p["d1"]);
        var finance = world.Unit("معاونت مالی", p["d2"]);
        var company = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت", "C1", null, p["head"]), default)).Value!.Id;
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, finance.Id, new LinkUnitRequest(company), default);
        var senior = await world.SeatAsync(tech.Id, "کارشناس ارشد");
        var expert = await world.SeatAsync(tech.Id, "کارشناس");
        foreach (var (who, unit, seats) in new[]
                 {
                     ("d1", tech.Id, Array.Empty<Guid>()), ("s1", tech.Id, [senior]), ("e2", tech.Id, [expert]),
                     ("d2", finance.Id, []), ("e3", finance.Id, []),
                 })
        {
            Assert.True((await world.Service.SetAssignmentAsync(Tenant, p[who], new SetUserAssignmentRequest(company, unit, null, null, seats), default)).IsSuccess);
        }

        return (world, p);
    }

    private static async Task<string[]> BlockedForAsync(World world, Dictionary<string, Guid> people, string who, bool on = true, params string[] permissions)
    {
        var limit = await world.AssignmentScope(people[who], on, permissions).GetLimitAsync(Tenant, people[who], default);
        return limit is null ? ["(not limited)"] : people.Where(x => limit.BlockedUserIds.Contains(x.Value)).Select(x => x.Key).OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task TasksGoToOnesOwnLevelAndBelow_NeverAbove()
    {
        var (world, p) = await HierarchyAsync();

        // The head: everyone is below.
        Assert.Empty(await BlockedForAsync(world, p, "head"));
        // A deputy's head: their own people and the other deputies' heads - not the head, not
        // another deputy's experts.
        Assert.Equal(["e3", "head"], await BlockedForAsync(world, p, "d1"));
        // An expert: experts of other deputies, and the lower position of their own deputy.
        Assert.Equal(["d1", "d2", "head"], await BlockedForAsync(world, p, "s1"));
        // The lower position may not give work to the higher one of the same deputy.
        Assert.Equal(["d1", "d2", "head", "s1"], await BlockedForAsync(world, p, "e2"));
        // Nobody may be blocked from giving work to someone with no place yet.
        Assert.DoesNotContain("nowhere", await BlockedForAsync(world, p, "e2"));
    }

    [Fact]
    public async Task TheLimit_IsOffByDefault_AndNotForThePermission_OrSomeoneWithNoPlace()
    {
        var (world, p) = await HierarchyAsync();

        Assert.Equal(["(not limited)"], await BlockedForAsync(world, p, "e2", on: false));
        Assert.Equal(["(not limited)"], await BlockedForAsync(world, p, "e2", true, OrgStructurePermissions.AssignTasksToAnyLevel));
        Assert.Equal(["(not limited)"], await BlockedForAsync(world, p, "nowhere"));
        Assert.Contains(new OrgStructurePermissionCatalog().GetPermissions(), x => x.Name == OrgStructurePermissions.AssignTasksToAnyLevel);
        Assert.DoesNotContain(OrgStructurePermissions.All, x => x.Name == OrgStructurePermissions.AssignTasksToAnyLevel);
    }

    // ------------------------------------------------------------------ positions of a unit

    [Fact]
    public async Task AUnitsPositions_ShowInItsOrder_VacantOnesToo_WithWhoHoldsThem()
    {
        var world = new World();
        var head = world.User("رئیس فناوری");
        var senior = world.User("کارشناس ارشد یک");
        var newcomer = world.User("تازه‌وارد");
        var tech = world.Unit("معاونت فناوری");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        var manager = await world.SeatAsync(tech.Id, "مدیر فناوری");
        var expert = await world.SeatAsync(tech.Id, "کارشناس");
        var seniorSeat = await world.SeatAsync(tech.Id, "کارشناس ارشد");

        // Expert last: the order is the unit's own.
        Assert.True((await world.Service.OrderUnitPositionsAsync(Tenant, tech.Id, new OrderUnitPositionsRequest([manager, seniorSeat, expert]), default)).IsSuccess);
        await world.Service.SetAssignmentAsync(Tenant, head, new SetUserAssignmentRequest(company, tech.Id, null, null, [manager]), default);
        await world.Service.SetAssignmentAsync(Tenant, senior, new SetUserAssignmentRequest(company, tech.Id, null, null, [seniorSeat]), default);
        await world.Service.SetAssignmentAsync(Tenant, newcomer, new SetUserAssignmentRequest(company, tech.Id, null, null), default);

        var unit = Assert.Single(Assert.Single((await world.Service.GetChartAsync(Tenant, null, default)).Value!).Units);
        var positions = unit.Positions!;
        Assert.Equal(["مدیر فناوری", "کارشناس ارشد", "کارشناس"], positions.Select(p => p.Title));
        Assert.Equal(head, Assert.Single(positions[0].Holders).UserId);
        Assert.Equal(senior, Assert.Single(positions[1].Holders).UserId);
        Assert.Empty(positions[2].Holders); // vacant, and still shown
        Assert.Equal(newcomer, Assert.Single(unit.Members).UserId); // in the unit, with no position yet
        Assert.Equal(3, unit.PeopleCount);
    }

    [Fact]
    public async Task APerson_MayHoldSeveralPositions_InSeveralUnits()
    {
        var world = new World();
        var person = world.User("چندسمتی");
        var tech = world.Unit("معاونت فناوری");
        var ops = world.Unit("معاونت عملیات");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, ops.Id, new LinkUnitRequest(company), default);
        var opsSeat = await world.SeatAsync(ops.Id, "سرپرست عملیات");
        var techSeat = await world.SeatAsync(tech.Id, "کارشناس ارشد");

        var placed = (await world.Service.SetAssignmentAsync(
            Tenant, person, new SetUserAssignmentRequest(company, tech.Id, null, null, [opsSeat, techSeat]), default)).Value!;

        // The main position is the one in their own unit; both are listed, the main one first.
        Assert.Equal("کارشناس ارشد", placed.PositionTitle);
        Assert.Equal([techSeat, opsSeat], placed.Positions!.Select(p => p.UnitPositionId));
        var units = Assert.Single((await world.Service.GetChartAsync(Tenant, null, default)).Value!).Units.ToDictionary(u => u.UnitId);
        var inTech = Assert.Single(Assert.Single(units[tech.Id].Positions!).Holders);
        Assert.Equal(person, inTech.UserId);
        Assert.Equal("کارشناس ارشد، سرپرست عملیات", inTech.PositionTitle);
        Assert.Equal(person, Assert.Single(Assert.Single(units[ops.Id].Positions!).Holders).UserId);
        Assert.Equal(1, units[ops.Id].PeopleCount);

        // Sending the list again replaces it.
        placed = (await world.Service.SetAssignmentAsync(
            Tenant, person, new SetUserAssignmentRequest(company, tech.Id, null, null, [opsSeat]), default)).Value!;
        Assert.Equal(opsSeat, Assert.Single(placed.Positions!).UnitPositionId);
        Assert.Equal("سرپرست عملیات", placed.PositionTitle);
    }

    [Fact]
    public async Task PlacingTheOldWay_GivesTheUnitThePosition_AndMovesTheMainOneOnly()
    {
        var world = new World();
        var person = world.User("کارمند");
        var tech = world.Unit("معاونت فناوری");
        var ops = world.Unit("معاونت عملیات");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, ops.Id, new LinkUnitRequest(company), default);
        var expert = await world.PositionAsync("کارشناس");
        var senior = await world.PositionAsync("کارشناس ارشد");
        var extra = await world.SeatAsync(ops.Id, "عضو کمیته");

        // A client that knows only one position: the unit gets it, and the person holds it.
        await world.Service.SetAssignmentAsync(Tenant, person, new SetUserAssignmentRequest(company, tech.Id, expert, null), default);
        await world.Service.SetAssignmentAsync(Tenant, person, new SetUserAssignmentRequest(company, tech.Id, expert, null, [(await world.SeatOfAsync(tech.Id, expert)), extra]), default);
        Assert.Equal(2, (await world.Service.GetAssignmentAsync(Tenant, person, default)).Value!.Positions!.Count);

        // Changing the main position the old way moves it; the other position stays.
        var placed = (await world.Service.SetAssignmentAsync(Tenant, person, new SetUserAssignmentRequest(company, tech.Id, senior, null), default)).Value!;
        Assert.Equal(["کارشناس ارشد", "عضو کمیته"], placed.Positions!.Select(p => p.Title));
        var techPositions = (await world.Service.ListUnitPositionsAsync(Tenant, tech.Id, default)).Value!;
        Assert.Equal(["کارشناس", "کارشناس ارشد"], techPositions.Select(p => p.Title));
        Assert.Empty(techPositions[0].Holders); // the old one stays in the unit, now vacant
    }

    [Fact]
    public async Task APositionWithHolders_CannotBeRemoved_AndRemovingAPlaceFreesItsPositions()
    {
        var world = new World();
        var person = world.User("کارمند");
        var tech = world.Unit("معاونت فناوری");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        var seat = await world.SeatAsync(tech.Id, "کارشناس");
        var positionId = (await world.Service.ListUnitPositionsAsync(Tenant, tech.Id, default)).Value![0].PositionId;
        await world.Service.SetAssignmentAsync(Tenant, person, new SetUserAssignmentRequest(company, tech.Id, null, null, [seat]), default);

        Assert.Equal("conflict", (await world.Service.RemoveUnitPositionAsync(Tenant, seat, default)).Error.Code);
        Assert.Equal("conflict", (await world.Service.AddUnitPositionAsync(Tenant, tech.Id, new AddUnitPositionRequest(positionId), default)).Error.Code);
        Assert.Equal("conflict", (await world.Service.DeletePositionAsync(Tenant, positionId, default)).Error.Code);
        Assert.Equal("validation.error", (await world.Service.OrderUnitPositionsAsync(Tenant, tech.Id, new OrderUnitPositionsRequest([]), default)).Error.Code);
        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(
            Tenant, person, new SetUserAssignmentRequest(company, tech.Id, null, null, [Guid.NewGuid()]), default)).Error.Code);

        Assert.True((await world.Service.RemoveAssignmentAsync(Tenant, person, default)).IsSuccess);
        Assert.Empty((await world.Service.ListUnitPositionsAsync(Tenant, tech.Id, default)).Value![0].Holders);
        Assert.True((await world.Service.RemoveUnitPositionAsync(Tenant, seat, default)).IsSuccess);
        Assert.True((await world.Service.DeletePositionAsync(Tenant, positionId, default)).IsSuccess);
    }

    [Fact]
    public async Task AnInactivePosition_CannotBeGivenToAUnit_OrNewlyToAPerson()
    {
        var world = new World();
        var person = world.User("کارمند");
        var tech = world.Unit("معاونت فناوری");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        var seat = await world.SeatAsync(tech.Id, "کارشناس");
        var positionId = (await world.Service.ListUnitPositionsAsync(Tenant, tech.Id, default)).Value![0].PositionId;
        await world.Service.UpdatePositionAsync(Tenant, positionId, new UpdatePositionRequest("کارشناس", false), default);

        Assert.Equal("validation.error", (await world.Service.SetAssignmentAsync(
            Tenant, person, new SetUserAssignmentRequest(company, tech.Id, null, null, [seat]), default)).Error.Code);
        var other = world.Unit("معاونت عملیات");
        Assert.Equal("validation.error", (await world.Service.AddUnitPositionAsync(Tenant, other.Id, new AddUnitPositionRequest(positionId), default)).Error.Code);
    }

    // ------------------------------------------------------------------ work teams in the chart

    [Fact]
    public async Task AWorkTeam_IsListedUnderItsBuildersUnit_AndPeopleWithoutAPositionAreTheUnitsMembers()
    {
        var world = new World();
        var builder = world.User("سازنده");
        var dev = world.User("برنامه‌نویس");
        var opsPerson = world.User("کارمند عملیات");
        var loner = world.User("بدون تیم");
        var tech = world.Unit("معاونت فناوری");
        var ops = world.Unit("معاونت عملیات");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, ops.Id, new LinkUnitRequest(company), default);
        foreach (var (user, unit) in new[] { (builder, tech), (dev, tech), (loner, tech), (opsPerson, ops) })
        {
            await world.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(company, unit.Id, null, null), default);
        }

        world.Team("تیم توسعه", builder, builder, dev, opsPerson);
        world.Team("تیم پشتیبانی", builder, dev); // a person may be in several teams

        var units = Assert.Single((await world.Service.GetChartAsync(Tenant, null, default)).Value!).Units.ToDictionary(u => u.UnitId);
        var techTeams = units[tech.Id].Teams;
        Assert.Equal(["تیم توسعه", "تیم پشتیبانی"], techTeams.Select(t => t.Name));
        Assert.Equal(builder, techTeams[0].Owner.UserId);
        Assert.Equal(3, techTeams[0].Members.Count); // from any unit
        Assert.Equal(dev, Assert.Single(techTeams[1].Members).UserId);
        // The chart shows positions, not teams: everyone of a unit who holds none of its positions
        // is listed as its member, in a team or not.
        Assert.Equal(new[] { builder, dev, loner }.OrderBy(world.NameOf), units[tech.Id].Members.Select(m => m.UserId));
        Assert.Empty(units[ops.Id].Teams);
        Assert.Equal(opsPerson, Assert.Single(units[ops.Id].Members).UserId);
        Assert.Equal(3, units[tech.Id].PeopleCount);
        Assert.Equal(1, units[ops.Id].PeopleCount);
        Assert.Empty((await world.Service.ListTeamsOutsideUnitsAsync(Tenant, default)).Value!);
    }

    [Fact]
    public async Task TeamsOfBuildersInNoUnit_AreListedOnTheirOwn()
    {
        var world = new World();
        var admin = world.User("مدیر سیستم");
        var member = world.User("عضو");
        world.Team("تیم توسعه‌دهندگان", admin, admin, member);

        var outside = Assert.Single((await world.Service.ListTeamsOutsideUnitsAsync(Tenant, default)).Value!);
        Assert.Equal("تیم توسعه‌دهندگان", outside.Name);
        Assert.Equal(admin, outside.Members[0].UserId); // the builder first
    }

    [Fact]
    public async Task UnplacedUsers_AreThoseWithNoPlaceYet()
    {
        var world = new World();
        var placed = world.User("دارای جایگاه");
        var unplaced = world.User("بی‌جایگاه");
        var company = await world.CompanyAsync("C1");
        await world.Service.SetAssignmentAsync(Tenant, placed, new SetUserAssignmentRequest(company, null, null, null), default);

        Assert.Equal([unplaced], (await world.Service.ListUnplacedUsersAsync(Tenant, default)).Value!.Select(u => u.UserId));
    }

    // ------------------------------------------------------------------ titles

    [Fact]
    public async Task GivingATitle_PutsItBeforeTheName_AndRenamingTheTitleRenamesEveryHolder()
    {
        var world = new World();
        var user = world.User("علی محمدی");
        var mr = (await world.Titles.CreateAsync(Tenant, new CreateHonorificRequest("آقای"), default)).Value!;
        var engineer = (await world.Titles.CreateAsync(Tenant, new CreateHonorificRequest("جناب مهندس"), default)).Value!;

        await world.Titles.SetUserTitleAsync(Tenant, user, new SetUserTitleRequest(mr.Id), default);
        Assert.Equal("آقای علی محمدی", world.NameOf(user));

        await world.Titles.SetUserTitleAsync(Tenant, user, new SetUserTitleRequest(engineer.Id), default);
        Assert.Equal("جناب مهندس علی محمدی", world.NameOf(user));

        await world.Titles.UpdateAsync(Tenant, engineer.Id, new UpdateHonorificRequest("مهندس", 2, true), default);
        Assert.Equal("مهندس علی محمدی", world.NameOf(user));
        Assert.Equal("مهندس", (await world.Titles.GetUserTitleAsync(Tenant, user, default)).Value!.Title);

        await world.Titles.SetUserTitleAsync(Tenant, user, new SetUserTitleRequest(null), default);
        Assert.Equal("علی محمدی", world.NameOf(user));
    }

    [Fact]
    public async Task ATitleInUse_CannotBeDeleted_AndAnInactiveOneCannotBeNewlyGiven()
    {
        var world = new World();
        var user = world.User("مریم حسینی");
        var other = world.User("سارا");
        var mrs = (await world.Titles.CreateAsync(Tenant, new CreateHonorificRequest("خانم"), default)).Value!;
        await world.Titles.SetUserTitleAsync(Tenant, user, new SetUserTitleRequest(mrs.Id), default);

        Assert.False((await world.Titles.DeleteAsync(Tenant, mrs.Id, default)).IsSuccess);

        await world.Titles.UpdateAsync(Tenant, mrs.Id, new UpdateHonorificRequest("خانم", 1, false), default);
        Assert.False((await world.Titles.SetUserTitleAsync(Tenant, other, new SetUserTitleRequest(mrs.Id), default)).IsSuccess);
        Assert.True((await world.Titles.SetUserTitleAsync(Tenant, user, new SetUserTitleRequest(mrs.Id), default)).IsSuccess); // kept by its holder
        Assert.Equal(1, Assert.Single((await world.Titles.ListAsync(Tenant, default)).Value!).UserCount);
    }

    [Theory]
    [InlineData("علی", "محمدی", "آقای علی محمدی", "آقای", "علی محمدی")]
    [InlineData(null, null, "آقای علی محمدی", "آقای", "علی محمدی")]
    [InlineData(null, null, "علی محمدی", null, "علی محمدی")]
    [InlineData(null, null, "آقایان", "آقای", "آقایان")] // only a whole leading word is a title
    public void TheNameUnderATitle_ComesFromFirstAndLastName_OrTheDisplayNameWithoutTheOldTitle(
        string? first, string? last, string displayName, string? previousTitle, string expected) =>
        Assert.Equal(expected, UserTitleWriter.ComposeName(first, last, displayName, previousTitle));

    // ------------------------------------------------------------------ test world

    // ------------------------------------------------------------------ whom a team builder may add

    [Fact]
    public async Task ATeamBuilder_AddsTheirOwnDeputysPeople_Only()
    {
        var world = new World();
        var builder = world.User("سازنده");
        var colleague = world.User("همکار");
        var other = world.User("معاونت دیگر");
        var direct = world.User("زیر شرکت");
        var tech = world.Unit("معاونت فناوری");
        var ops = world.Unit("معاونت عملیات");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, ops.Id, new LinkUnitRequest(company), default);
        await world.Service.SetAssignmentAsync(Tenant, builder, new SetUserAssignmentRequest(company, tech.Id, null, null), default);
        await world.Service.SetAssignmentAsync(Tenant, colleague, new SetUserAssignmentRequest(company, tech.Id, null, null), default);
        await world.Service.SetAssignmentAsync(Tenant, other, new SetUserAssignmentRequest(company, ops.Id, null, null), default);
        await world.Service.SetAssignmentAsync(Tenant, direct, new SetUserAssignmentRequest(company, null, null, null), default);

        var limit = await world.MemberScope(builder).GetLimitAsync(Tenant, builder, default);

        Assert.NotNull(limit);
        Assert.Equal("معاونت فناوری", limit.ScopeName);
        Assert.Equal(new[] { builder, colleague }.Order(), limit.AllowedUserIds.Order());
    }

    [Fact]
    public async Task WithThePermission_OrInNoDeputy_ATeamBuilderMayAddAnyone()
    {
        var world = new World();
        var builder = world.User("سازنده");
        var manager = world.User("مدیرعامل");
        var tech = world.Unit("معاونت فناوری");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.SetAssignmentAsync(Tenant, builder, new SetUserAssignmentRequest(company, tech.Id, null, null), default);
        await world.Service.SetAssignmentAsync(Tenant, manager, new SetUserAssignmentRequest(company, null, null, null), default);
        var unplaced = world.User("بدون جایگاه");

        Assert.Null(await world.MemberScope(builder, OrgStructurePermissions.TeamMembersFromAnyUnit).GetLimitAsync(Tenant, builder, default));
        Assert.Null(await world.MemberScope(manager).GetLimitAsync(Tenant, manager, default));
        Assert.Null(await world.MemberScope(unplaced).GetLimitAsync(Tenant, unplaced, default));
    }

    [Fact]
    public void ThePermissionForOtherDeputies_IsListed_ButGivesNoAccessToTheStructure()
    {
        Assert.Contains(new OrgStructurePermissionCatalog().GetPermissions(), p => p.Name == OrgStructurePermissions.TeamMembersFromAnyUnit);
        Assert.DoesNotContain(OrgStructurePermissions.All, p => p.Name == OrgStructurePermissions.TeamMembersFromAnyUnit);
    }

    private sealed class World
    {
        private readonly OrganizationDbContext _organization;
        private readonly OrgStructureRepository _repository;
        private readonly Directory _users = new();
        private readonly WorkTeams _teams = new();

        public World()
        {
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            var repository = _repository = new OrgStructureRepository(structure);
            Service = new OrgStructureService(repository, structure, new OrganizationUnitRepository(_organization), _users, _teams);
            Titles = new HonorificService(repository, structure, _users, _users);
        }

        public IOrgStructureService Service { get; }
        public IHonorificService Titles { get; }

        public string NameOf(Guid userId) => _users.Names[userId];

        /// <summary>Whom <paramref name="signedIn"/> may add to their work teams, holding <paramref name="permissions"/>.</summary>
        public IWorkTeamMemberScope MemberScope(Guid signedIn, params string[] permissions) =>
            new DeputyWorkTeamMemberScope(_repository, new OrganizationUnitRepository(_organization), new SignedIn(signedIn, permissions));

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

        public async Task<Guid> PositionAsync(string title) =>
            (await Service.CreatePositionAsync(Tenant, new CreatePositionRequest(title), default)).Value!.Id;

        /// <summary>Gives the unit a position (created with this title) and returns the unit position's id.</summary>
        public async Task<Guid> SeatAsync(Guid unitId, string title) =>
            (await Service.AddUnitPositionAsync(Tenant, unitId, new AddUnitPositionRequest(await PositionAsync(title)), default)).Value!.Id;

        public async Task<Guid> SeatOfAsync(Guid unitId, Guid positionId) =>
            (await Service.ListUnitPositionsAsync(Tenant, unitId, default)).Value!.Single(p => p.PositionId == positionId).Id;

        public async Task<Guid> CompanyAsync(string code) =>
            (await Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت " + code, code, null, null), default)).Value!.Id;
    }

    private sealed class SignedIn(Guid userId, string[] permissions) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => Tenant;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class WorkTeams : IWorkTeamDirectory
    {
        public List<WorkTeamInfo> Teams { get; } = [];

        public Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkTeamInfo>>(Teams);
    }

    /// <summary>No work teams at all (tests about other parts of the structure).</summary>
    public sealed class NoWorkTeams : IWorkTeamDirectory
    {
        public Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkTeamInfo>>([]);
    }

    /// <summary>The identity side: users' names, and the title writer working on them like the real one.</summary>
    private sealed class Directory : IUserDirectory, IUserTitleWriter
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
