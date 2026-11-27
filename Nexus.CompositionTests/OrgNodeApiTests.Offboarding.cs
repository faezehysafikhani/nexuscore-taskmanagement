using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 10: a person leaving frees (or hands over) every post they hold in one change, drops the
/// appointments that had not started, and leaves the company without a head only when nobody takes
/// the head's post; a role can move to someone else now or on a day.
/// </summary>
public sealed partial class OrgNodeApiTests
{
    private static async Task<(World W, Clock Clock, IOrgAppointmentService Appointments, Guid First, Guid Second, Guid Old, Guid New)> TwoPostsAsync()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var w = new World(clock);
        w.Company = await w.CompanyAsync("C1");
        var unit = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company), default));
        var first = await w.SeatAsync(unit.Id, "کارشناس ارشد");
        var second = await w.SeatAsync(unit.Id, "کارشناس");
        var (old, fresh) = (w.User("کارمند در حال خروج"), w.User("جانشین"));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, unit.Id, null, null, [first, second]), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, fresh, new SetUserAssignmentRequest(w.Company, unit.Id, null, null), default));
        return (w, clock, (IOrgAppointmentService)w.Service, first, second, old, fresh);
    }

    [Fact]
    public async Task Leaving_FreesEveryPost_AndLeavesNoTaskWithTheHolder()
    {
        var (w, clock, appointments, first, second, old, _) = await TwoPostsAsync();
        var task = Guid.NewGuid();
        await w.Repo.AddTaskPostAsync(new TaskPost(Guid.NewGuid(), Tenant, task, first, 0, old), default);
        await w.SaveAsync();

        clock.At = clock.At.AddDays(3);
        var left = await w.Ok(appointments.OffboardUserAsync(Tenant, old, new OffboardUserRequest(), default));

        // Both posts are free; the record stays as history, ending on the day.
        Assert.Equal(new[] { first, second }.Order(), left.Ended.Select(a => a.UnitPositionId).Order());
        Assert.All(left.Ended, a => Assert.Equal(clock.At, a.EndDate));
        Assert.Empty(left.Transferred);
        Assert.Null(await HolderAsync(w, first));
        Assert.Null(await HolderAsync(w, second));
        Assert.Null(Assert.Single(await w.Repo.ListTaskPostsAsync(Tenant, [task], null, default)).HolderUserId);
        Assert.Equal(2, (await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default))).Count);

        // Nothing left to leave from.
        Assert.Equal("validation.error", (await appointments.OffboardUserAsync(Tenant, old, new OffboardUserRequest(), default)).Error.Code);
    }

    [Fact]
    public async Task Leaving_HandsTheChosenPostsToASuccessor_AndFreesTheRest()
    {
        var (w, clock, appointments, first, second, old, fresh) = await TwoPostsAsync();
        clock.At = clock.At.AddDays(1);

        var left = await w.Ok(appointments.OffboardUserAsync(Tenant, old, new OffboardUserRequest(fresh, [first]), default));

        Assert.Equal(fresh, Assert.Single(left.Transferred).Person.UserId);
        Assert.Equal(first, left.Transferred[0].UnitPositionId);
        Assert.Equal(second, Assert.Single(left.Ended).UnitPositionId);
        Assert.Equal(fresh, await HolderAsync(w, first));
        Assert.Null(await HolderAsync(w, second));

        // The past is kept: who held the post before the successor.
        var history = await w.Ok(appointments.GetPostHistoryAsync(Tenant, first, default));
        Assert.Equal([fresh, old], history.Select(a => a.Person.UserId));
    }

    [Fact]
    public async Task Leaving_RefusesASuccessorWhoHasNoPlaceThere_AndChangesNothing()
    {
        var (w, _, appointments, first, second, old, _) = await TwoPostsAsync();
        var stranger = w.User("بی‌جا");

        var refused = await appointments.OffboardUserAsync(Tenant, old, new OffboardUserRequest(stranger), default);
        Assert.Equal("validation.error", refused.Error.Code);
        Assert.Equal(old, await HolderAsync(w, first));
        Assert.Equal(old, await HolderAsync(w, second));
        Assert.All(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)), a => Assert.True(a.IsActive));
    }

    [Fact]
    public async Task Leaving_DropsTheAppointmentsThatHadNotStarted_AndThePresentHolderStaysOn()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var w = new World(clock);
        w.Company = await w.CompanyAsync("C1");
        var unit = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company), default));
        var own = await w.SeatAsync(unit.Id, "کارشناس ارشد");
        var theirs = await w.SeatAsync(unit.Id, "کارشناس");
        var (leaving, staying) = (w.User("در حال خروج"), w.User("ماندنی"));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, leaving, new SetUserAssignmentRequest(w.Company, unit.Id, null, null, [own]), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, staying, new SetUserAssignmentRequest(w.Company, unit.Id, null, null, [theirs]), default));
        var appointments = (IOrgAppointmentService)w.Service;
        await w.Ok(appointments.AppointAsync(Tenant, theirs, new AppointRequest(leaving, clock.At.AddDays(9)), default));

        var left = await w.Ok(appointments.OffboardUserAsync(Tenant, leaving, new OffboardUserRequest(), default));

        Assert.Equal(own, Assert.Single(left.Ended).UnitPositionId);
        Assert.Equal(theirs, Assert.Single(left.Cancelled).UnitPositionId);
        // Whoever was to end on that day stays on: it is not happening any more.
        Assert.Equal(staying, await HolderAsync(w, theirs));
        Assert.Null(Assert.Single(await w.Ok(appointments.GetPostHistoryAsync(Tenant, theirs, default))).EndDate);
    }

    [Fact]
    public async Task ACompanyHeadWhoLeaves_LeavesTheCompanyWithoutAHead()
    {
        var (w, _, appointments, _, _, old, fresh) = await TwoPostsAsync();
        var headPosition = (await w.Ok(w.Service.ListPositionsAsync(Tenant, default))).Single(p => p.ParentPositionId is null);
        var headPost = await w.Ok(w.Service.AddCompanyPositionAsync(Tenant, w.Company, new AddUnitPositionRequest(headPosition.Id), default));
        await w.Ok(appointments.AppointAsync(Tenant, headPost.Id, new AppointRequest(old), default));
        var company = (await w.Repo.ListCompaniesAsync(Tenant, default)).Single(c => c.Id == w.Company);
        company.SetHead(old);
        await w.SaveAsync();

        // The successor takes the head's post: the company keeps a head.
        var left = await w.Ok(appointments.OffboardUserAsync(Tenant, old, new OffboardUserRequest(fresh, [headPost.Id]), default));
        Assert.Empty(left.HeadlessCompanyIds);
        Assert.Equal(fresh, (await w.Repo.ListCompaniesAsync(Tenant, default)).Single(c => c.Id == w.Company).ManagerUserId);

        // Nobody takes it: no head.
        var again = await w.Ok(appointments.OffboardUserAsync(Tenant, fresh, new OffboardUserRequest(), default));
        Assert.Equal(w.Company, Assert.Single(again.HeadlessCompanyIds));
        Assert.Null((await w.Repo.ListCompaniesAsync(Tenant, default)).Single(c => c.Id == w.Company).ManagerUserId);
    }

    [Fact]
    public async Task Transfer_MovesThePostsToSomeoneElse_NowOrOnADay()
    {
        var (w, clock, appointments, first, second, old, fresh) = await TwoPostsAsync();
        clock.At = clock.At.AddDays(2);

        Assert.Equal("validation.error", (await appointments.TransferPostsAsync(Tenant, old, new TransferPostsRequest(fresh, clock.At.AddDays(-1)), default)).Error.Code);
        Assert.Equal("validation.error", (await appointments.TransferPostsAsync(Tenant, old, new TransferPostsRequest(old), default)).Error.Code);
        Assert.Equal("validation.error", (await appointments.TransferPostsAsync(Tenant, old, new TransferPostsRequest(fresh, PostIds: [Guid.NewGuid()]), default)).Error.Code);

        var now = await w.Ok(appointments.TransferPostsAsync(Tenant, old, new TransferPostsRequest(fresh, PostIds: [first]), default));
        Assert.True(Assert.Single(now.Transferred).IsActive);
        Assert.Equal(fresh, await HolderAsync(w, first));
        Assert.Equal(old, await HolderAsync(w, second)); // only the one asked for

        // A later day: theirs until then, as an appointment from that day.
        var later = await w.Ok(appointments.TransferPostsAsync(Tenant, old, new TransferPostsRequest(fresh, clock.At.AddDays(5)), default));
        Assert.False(Assert.Single(later.Transferred).IsActive);
        Assert.Equal(old, await HolderAsync(w, second));

        clock.At = clock.At.AddDays(6);
        Assert.Equal(fresh, await HolderAsync(w, second));
    }
}
