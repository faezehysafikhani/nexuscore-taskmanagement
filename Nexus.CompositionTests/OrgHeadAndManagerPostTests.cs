using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

/// <summary>1.5: the company head's mark and the manager's post of a unit. Without marks the old rules decide, as the golden tests show.</summary>
public sealed class OrgHeadAndManagerPostTests
{
    private static async Task<(OrgGoldenTests.GoldenWorld World, Guid Company)> StartAsync()
    {
        var world = new OrgGoldenTests.GoldenWorld();
        return (world, await world.CompanyAsync("C1"));
    }

    [Fact]
    public async Task APostMadeForThePositionWithNoSuperior_IsMarkedTheHeads_AndOthersAreNot()
    {
        var (world, company) = await StartAsync();
        var member = await world.PositionAsync("عضو هیئت");
        var head = (await world.Service.AddCompanyPositionAsync(OrgGoldenTests.TenantId, company, new AddUnitPositionRequest(world.TopPosition), default)).Value!.Id;
        var board = (await world.Service.AddCompanyPositionAsync(OrgGoldenTests.TenantId, company, new AddUnitPositionRequest(member), default)).Value!.Id;
        Assert.True(world.Structure.UnitPositions.Single(p => p.Id == head).IsCompanyHead);
        Assert.False(world.Structure.UnitPositions.Single(p => p.Id == board).IsCompanyHead);
    }

    [Fact]
    public async Task AMarkedPost_IsTheHeadsPost_EvenForAPositionWithASuperior_AndItsHolderIsTheHead()
    {
        var (world, company) = await StartAsync();
        var chairman = await world.PositionAsync("رئیس هیئت مدیره");
        var seat = (await world.Service.AddCompanyPositionAsync(OrgGoldenTests.TenantId, company, new AddUnitPositionRequest(chairman), default)).Value!.Id;
        var post = world.Structure.UnitPositions.Single(p => p.Id == seat);
        Assert.False(post.IsCompanyHead);
        post.SetCompanyHead(true);
        await world.Structure.SaveChangesAsync();

        var user = world.User("رئیس");
        Assert.True((await world.Service.SetAssignmentAsync(OrgGoldenTests.TenantId, user, new SetUserAssignmentRequest(company, null, chairman, null, [seat]), default)).IsSuccess);
        var chart = (await world.Service.GetChartAsync(OrgGoldenTests.TenantId, null, default)).Value!.Single();
        Assert.Equal(user, chart.Manager!.UserId);
        Assert.True(chart.Positions!.Single(p => p.UnitPositionId == seat).IsHead);
    }

    [Fact]
    public async Task TheHolderOfAUnitsManagerPost_IsItsManager_OnlyWhenTheUnitHasNoManagerSet()
    {
        var (world, company) = await StartAsync();
        var head = world.User("رئیس");
        await world.HeadAsync(company, head);
        var manager = world.User("مدیر");
        var staff = world.User("کارمند");
        var unit = world.Unit("اداره", null);
        Assert.True((await world.Service.LinkUnitAsync(OrgGoldenTests.TenantId, unit.Id, new LinkUnitRequest(company), default)).IsSuccess);
        var managerSeat = await world.SeatAsync(unit.Id, "مدیر اداره");
        var staffSeat = await world.SeatAsync(unit.Id, "کارمند اداره");
        async Task PlaceAsync(Guid user, Guid seat) =>
            Assert.True((await world.Service.SetAssignmentAsync(OrgGoldenTests.TenantId, user, new SetUserAssignmentRequest(company, unit.Id, null, null, [seat]), default)).IsSuccess);
        await PlaceAsync(staff, staffSeat);
        async Task<Guid?> ManagerOfStaff() => (await world.Service.GetManagerAsync(OrgGoldenTests.TenantId, staff, default)).Value?.UserId;

        // nobody holds the manager's post and it is not marked: the company head, as before
        Assert.Equal(head, await ManagerOfStaff());

        await PlaceAsync(manager, managerSeat);
        world.Structure.UnitPositions.Single(p => p.Id == managerSeat).SetManagerPost(true);
        await world.Structure.SaveChangesAsync();
        Assert.Equal(manager, await ManagerOfStaff());

        // a manager set on the unit itself wins
        var explicitManager = world.User("مدیر تعیین‌شده");
        var stored = world.Org.OrganizationUnits.Single(u => u.Id == unit.Id);
        stored.Update(stored.Name, explicitManager, true);
        await world.Org.SaveChangesAsync();
        Assert.Equal(explicitManager, await ManagerOfStaff());
    }
}
