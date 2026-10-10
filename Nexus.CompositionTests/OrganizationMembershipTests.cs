using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class OrganizationMembershipTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private sealed class Fixture
    {
        public OrganizationDbContext Db { get; }
        public OrganizationService Units { get; }
        public OrganizationMembershipService Members { get; }

        public Fixture()
        {
            Db = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var units = new OrganizationUnitRepository(Db);
            var members = new OrganizationMemberRepository(Db);
            Units = new OrganizationService(units, Db, members);
            Members = new OrganizationMembershipService(members, units, Db);
        }

        public async Task<OrganizationUnitDto> UnitAsync(string name, Guid? parent = null) =>
            (await Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, name, name.ToUpperInvariant(), parent), default)).Value!;
    }

    [Fact]
    public async Task EveryUnit_CarriesItsFullPath()
    {
        var f = new Fixture();
        var org = await f.UnitAsync("Organization");
        var eng = await f.UnitAsync("Engineering", org.Id);
        var civil = await f.UnitAsync("Civil", eng.Id);

        Assert.Equal("Organization > Engineering > Civil", civil.Path);
        var listed = (await f.Units.ListAsync(Tenant, default)).Value!;
        Assert.Equal("Organization", listed.Single(u => u.Id == org.Id).Path);
        Assert.Equal("Organization > Engineering", (await f.Units.GetAsync(eng.Id, default)).Value!.Path);
    }

    [Fact]
    public async Task TheList_CanBeLimitedToActiveUnits_AndStillIncludesEverythingByDefault()
    {
        var f = new Fixture();
        var a = await f.UnitAsync("A");
        await f.UnitAsync("B");
        await f.Units.DeactivateAsync(a.Id, default);

        Assert.Equal(2, (await f.Units.ListAsync(Tenant, default)).Value!.Count);
        Assert.Equal(["B"], (await f.Units.ListAsync(Tenant, default, activeOnly: true)).Value!.Select(u => u.Name));
    }

    [Fact]
    public async Task AUnit_CannotBeMovedUnderItself_OrUnderItsOwnDescendant_OrAMissingParent()
    {
        var f = new Fixture();
        var org = await f.UnitAsync("Organization");
        var eng = await f.UnitAsync("Engineering", org.Id);
        var civil = await f.UnitAsync("Civil", eng.Id);
        UpdateOrganizationUnitRequest Move(OrganizationUnitDto unit, Guid? parent) => new(unit.Name, unit.Code, parent, null, true);

        Assert.Equal("validation.error", (await f.Units.UpdateAsync(org.Id, Move(org, civil.Id), default)).Error.Code); // under its grandchild
        Assert.Equal("validation.error", (await f.Units.UpdateAsync(org.Id, Move(org, eng.Id), default)).Error.Code);   // under its child
        Assert.Equal("validation.error", (await f.Units.UpdateAsync(eng.Id, Move(eng, eng.Id), default)).Error.Code);   // under itself
        Assert.Equal("validation.error", (await f.Units.UpdateAsync(eng.Id, Move(eng, Guid.NewGuid()), default)).Error.Code); // nowhere

        var moved = (await f.Units.UpdateAsync(civil.Id, Move(civil, org.Id), default)).Value!;
        Assert.Equal("Organization > Civil", moved.Path);
    }

    [Fact]
    public async Task ACodeIsAssignedWhenNoneIsGiven_AndNeverCollides()
    {
        var f = new Fixture();
        await f.Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, "Taken", "U0002"), default); // occupies the code the second unit would get

        var first = (await f.Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, "A"), default)).Value!;
        var second = (await f.Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, "B"), default)).Value!;

        Assert.NotEqual(first.Code, second.Code);
        Assert.All(new[] { first.Code, second.Code }, code => Assert.Matches(@"^U\d{4}$", code));
        Assert.Equal("validation.error", (await f.Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, " "), default)).Error.Code);
        Assert.Equal("conflict", (await f.Units.CreateAsync(new CreateOrganizationUnitRequest(Tenant, "C", "U0002"), default)).Error.Code);
    }

    [Fact]
    public async Task DeactivatingAUnit_TakesItsWholeBranch_ButNotItsSiblings()
    {
        var f = new Fixture();
        var org = await f.UnitAsync("Organization");
        var eng = await f.UnitAsync("Engineering", org.Id);
        var civil = await f.UnitAsync("Civil", eng.Id);
        var fin = await f.UnitAsync("Finance", org.Id);

        Assert.True((await f.Units.DeactivateAsync(eng.Id, default)).IsSuccess);

        var active = (await f.Units.ListAsync(Tenant, default, activeOnly: true)).Value!.Select(u => u.Name);
        Assert.Equal(["Finance", "Organization"], active.Order());
        Assert.False((await f.Units.GetAsync(civil.Id, default)).Value!.IsActive);
        Assert.True((await f.Units.GetAsync(fin.Id, default)).Value!.IsActive);
    }

    [Fact]
    public async Task ABranchWithPeopleInIt_CannotBeDeactivated_UntilTheyAreMoved()
    {
        var f = new Fixture();
        var org = await f.UnitAsync("Organization");
        var eng = await f.UnitAsync("Engineering", org.Id);
        var civil = await f.UnitAsync("Civil", eng.Id);
        await f.Members.SetUserUnitAsync(Tenant, Alice, civil.Id, default);

        var refused = await f.Units.DeactivateAsync(eng.Id, default);
        Assert.Equal("conflict", refused.Error.Code);
        Assert.True((await f.Units.GetAsync(civil.Id, default)).Value!.IsActive);

        await f.Members.SetUserUnitAsync(Tenant, Alice, org.Id, default);
        Assert.True((await f.Units.DeactivateAsync(eng.Id, default)).IsSuccess);
    }

    [Fact]
    public async Task APersonIsInAtMostOneUnit_AndMovingThemReplacesThePlacement()
    {
        var f = new Fixture();
        var org = await f.UnitAsync("Organization");
        var eng = await f.UnitAsync("Engineering", org.Id);

        var first = (await f.Members.SetUserUnitAsync(Tenant, Alice, org.Id, default)).Value!;
        Assert.Equal("Organization", first.UnitPath);
        var moved = (await f.Members.SetUserUnitAsync(Tenant, Alice, eng.Id, default)).Value!;
        Assert.Equal("Organization > Engineering", moved.UnitPath);

        var all = (await f.Members.ListAsync(Tenant, null, default)).Value!;
        var only = Assert.Single(all);
        Assert.Equal(eng.Id, only.UnitId);
    }

    [Fact]
    public async Task TheMemberList_CanBeScopedToOneUnit_AndNeverCrossesTenants()
    {
        var f = new Fixture();
        var a = await f.UnitAsync("A");
        var b = await f.UnitAsync("B");
        await f.Members.SetUserUnitAsync(Tenant, Alice, a.Id, default);
        await f.Members.SetUserUnitAsync(Tenant, Bob, b.Id, default);

        Assert.Equal([Alice], (await f.Members.ListAsync(Tenant, a.Id, default)).Value!.Select(m => m.UserId));
        Assert.Empty((await f.Members.ListAsync(Guid.NewGuid(), null, default)).Value!);
    }

    [Fact]
    public async Task SettingNoUnit_TakesThePersonOutOfTheChart_AndRepeatingItIsHarmless()
    {
        var f = new Fixture();
        var a = await f.UnitAsync("A");
        await f.Members.SetUserUnitAsync(Tenant, Alice, a.Id, default);

        Assert.Null((await f.Members.SetUserUnitAsync(Tenant, Alice, null, default)).Value);
        Assert.Null((await f.Members.SetUserUnitAsync(Tenant, Alice, null, default)).Value);
        Assert.Empty((await f.Members.ListAsync(Tenant, null, default)).Value!);
    }

    [Fact]
    public async Task PeopleCannotBePlacedInAMissingOrInactiveOrForeignUnit()
    {
        var f = new Fixture();
        var a = await f.UnitAsync("A");
        await f.Units.DeactivateAsync(a.Id, default);

        Assert.Equal("validation.error", (await f.Members.SetUserUnitAsync(Tenant, Alice, Guid.NewGuid(), default)).Error.Code);
        Assert.Equal("conflict", (await f.Members.SetUserUnitAsync(Tenant, Alice, a.Id, default)).Error.Code);

        var live = await f.UnitAsync("Live");
        Assert.Equal("validation.error", (await f.Members.SetUserUnitAsync(Guid.NewGuid(), Alice, live.Id, default)).Error.Code); // another tenant's unit
    }

    private static OrganizationDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "organization", "2026-10-10-add-organization-members.sql", typeof(OrganizationSchemaUpgrade), ["OrganizationUnitMembers"]);
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-10-add-organization-members.sql", typeof(OrganizationSchemaUpgrade));
    }
}
