using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Identity;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>Organizational teams inside units, and titles before people's names (Nexus.OrgStructure).</summary>
public sealed class OrgTeamsAndTitlesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // ------------------------------------------------------------------ teams

    [Fact]
    public async Task AddingSomeoneToATeam_PutsThemInItsUnit_AndTheChartShowsThemInTheTeam()
    {
        var world = new World();
        var head = world.User("مدیر فناوری");
        var dev = world.User("برنامه‌نویس");
        var loner = world.User("بدون تیم");
        var tech = world.Unit("معاونت فناوری", head);
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.SetAssignmentAsync(Tenant, loner, new SetUserAssignmentRequest(company, tech.Id, null, null), default);

        var team = (await world.Service.CreateTeamAsync(Tenant, new CreateOrgTeamRequest(tech.Id, "تیم فرانت‌اند", null, null), default)).Value!;
        var placed = await world.Service.SetUserTeamAsync(Tenant, dev, new SetUserTeamRequest(team.Id), default);

        Assert.True(placed.IsSuccess);
        Assert.Equal(company, placed.Value!.CompanyId);
        Assert.Equal(tech.Id, placed.Value.UnitId);
        Assert.Equal(team.Id, placed.Value.TeamId);
        Assert.Equal(head, placed.Value.Manager!.UserId); // the unit's manager, as before - a team does not change it

        var unit = Assert.Single(Assert.Single((await world.Service.GetChartAsync(Tenant, null, default)).Value!).Units);
        var chartTeam = Assert.Single(unit.Teams);
        Assert.Equal(dev, Assert.Single(chartTeam.Members).UserId);
        Assert.Equal(loner, Assert.Single(unit.Members).UserId); // in the unit, in no team
    }

    [Fact]
    public async Task MovingToAnotherUnit_LeavesTheTeam_AndDeletingATeamKeepsItsPeopleInTheUnit()
    {
        var world = new World();
        var dev = world.User("برنامه‌نویس");
        var tech = world.Unit("معاونت فناوری");
        var ops = world.Unit("معاونت عملیات");
        var company = await world.CompanyAsync("C1");
        await world.Service.LinkUnitAsync(Tenant, tech.Id, new LinkUnitRequest(company), default);
        await world.Service.LinkUnitAsync(Tenant, ops.Id, new LinkUnitRequest(company), default);
        var team = (await world.Service.CreateTeamAsync(Tenant, new CreateOrgTeamRequest(tech.Id, "تیم بک‌اند", null, null), default)).Value!;
        await world.Service.SetUserTeamAsync(Tenant, dev, new SetUserTeamRequest(team.Id), default);

        var moved = await world.Service.SetAssignmentAsync(Tenant, dev, new SetUserAssignmentRequest(company, ops.Id, null, null), default);
        Assert.Null(moved.Value!.TeamId);

        await world.Service.SetUserTeamAsync(Tenant, dev, new SetUserTeamRequest(team.Id), default);
        Assert.True((await world.Service.DeleteTeamAsync(Tenant, team.Id, default)).IsSuccess);

        var after = (await world.Service.GetAssignmentAsync(Tenant, dev, default)).Value!;
        Assert.Equal(tech.Id, after.UnitId);
        Assert.Null(after.TeamId);
    }

    [Fact]
    public async Task ATeamOfAUnitWithNoCompany_CannotTakePeople_AndNamesAreUniquePerUnit()
    {
        var world = new World();
        var dev = world.User("برنامه‌نویس");
        var unlinked = world.Unit("واحد بی‌شرکت");
        var team = (await world.Service.CreateTeamAsync(Tenant, new CreateOrgTeamRequest(unlinked.Id, "تیم", null, null), default)).Value!;

        Assert.False((await world.Service.SetUserTeamAsync(Tenant, dev, new SetUserTeamRequest(team.Id), default)).IsSuccess);
        Assert.False((await world.Service.CreateTeamAsync(Tenant, new CreateOrgTeamRequest(unlinked.Id, " تیم ", null, null), default)).IsSuccess);
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

    private sealed class World
    {
        private readonly OrganizationDbContext _organization;
        private readonly Directory _users = new();

        public World()
        {
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            var repository = new OrgStructureRepository(structure);
            Service = new OrgStructureService(repository, structure, new OrganizationUnitRepository(_organization), _users);
            Titles = new HonorificService(repository, structure, _users, _users);
        }

        public IOrgStructureService Service { get; }
        public IHonorificService Titles { get; }

        public string NameOf(Guid userId) => _users.Names[userId];

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

        public async Task<Guid> CompanyAsync(string code) =>
            (await Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت " + code, code, null, null), default)).Value!.Id;
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
