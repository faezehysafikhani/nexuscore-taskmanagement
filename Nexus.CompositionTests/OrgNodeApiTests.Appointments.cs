using Microsoft.Extensions.DependencyInjection;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 9: appointments over time. A post's holder changes in one save; the past stays as history;
/// a later start takes effect on its day; everything that asks who holds a post sees only the one on now.
/// </summary>
public sealed partial class OrgNodeApiTests
{
    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset At { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => At;
    }

    private static async Task<(World W, Clock Clock, IOrgAppointmentService Appointments, Guid Post, Guid Old, Guid New)> PostWithHolderAsync()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var w = new World(clock);
        w.Company = await w.CompanyAsync("C1");
        var unit = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company), default));
        var post = await w.SeatAsync(unit.Id, "کارشناس");
        var (old, fresh) = (w.User("کارشناس قبلی"), w.User("کارشناس تازه"));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, unit.Id, null, null, [post]), default));
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, fresh, new SetUserAssignmentRequest(w.Company, unit.Id, null, null), default));
        return (w, clock, (IOrgAppointmentService)w.Service, post, old, fresh);
    }

    private static async Task<Guid?> HolderAsync(World w, Guid post) =>
        (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post).Holders.SingleOrDefault()?.UserId;

    [Fact]
    public async Task ASwap_IsOneChange_TheOldAppointmentStaysAsHistory_AndOpenTasksMove()
    {
        var (w, clock, appointments, post, old, fresh) = await PostWithHolderAsync();
        var task = Guid.NewGuid();
        await w.Repo.AddTaskPostAsync(new TaskPost(Guid.NewGuid(), Tenant, task, post, 0, old), default);
        await w.SaveAsync();

        clock.At = clock.At.AddDays(10);
        var made = await w.Ok(appointments.AppointAsync(Tenant, post, new AppointRequest(fresh), default));
        Assert.True(made.IsActive);
        Assert.Equal(fresh, await HolderAsync(w, post));

        // The history: both, newest first; the old one ended when the new one started.
        var history = await w.Ok(appointments.GetPostHistoryAsync(Tenant, post, default));
        Assert.Equal([fresh, old], history.Select(a => a.Person.UserId));
        Assert.Equal(history[0].StartDate, history[1].EndDate);
        Assert.False(history[1].IsActive);
        Assert.Single(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)));

        // The open task went to the new holder; who it was given to before is kept as its history.
        Assert.Equal(fresh, Assert.Single(await w.Repo.ListTaskPostsAsync(Tenant, [task], null, default)).HolderUserId);
        Assert.Contains(w.Updater.Applied, change => change.TaskId == task && change.FromUserId == old && change.ToUserId == fresh);

        // Again for the same person: refused; from a past day: refused.
        Assert.Equal("conflict", (await appointments.AppointAsync(Tenant, post, new AppointRequest(fresh), default)).Error.Code);
        Assert.Equal("validation.error", (await appointments.AppointAsync(Tenant, post, new AppointRequest(old, clock.At.AddDays(-1)), default)).Error.Code);
    }

    [Fact]
    public async Task AnAppointmentFromALaterDay_TakesEffectOnItsDay()
    {
        var (w, clock, appointments, post, old, fresh) = await PostWithHolderAsync();
        var start = clock.At.AddDays(7);
        var later = await w.Ok(appointments.AppointAsync(Tenant, post, new AppointRequest(fresh, start), default));
        Assert.False(later.IsActive);
        Assert.Equal(old, await HolderAsync(w, post)); // until then: as it was
        Assert.Equal("conflict", (await appointments.AppointAsync(Tenant, post, new AppointRequest(old, start.AddDays(1)), default)).Error.Code);
        // Nobody else can be given the post through the old way either.
        var third = w.User("سوم");
        Assert.Equal("conflict", (await w.Service.SetAssignmentAsync(Tenant, third, new SetUserAssignmentRequest(w.Company, null, null, null, [post]), default)).Error.Code);

        var task = Guid.NewGuid();
        await w.Repo.AddTaskPostAsync(new TaskPost(Guid.NewGuid(), Tenant, task, post, 0, old), default);
        await w.SaveAsync();

        clock.At = start.AddMinutes(1);
        Assert.Equal(fresh, await HolderAsync(w, post));
        // The background look moves the open task on the day.
        var services = new ServiceCollection().AddLogging()
            .AddSingleton(w.TaskPosts).AddSingleton<IOrgStructureRepository>(w.Repo).BuildServiceProvider();
        var activation = new AppointmentActivationService(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AppointmentActivationService>>(), clock);
        await activation.RunOnceAsync(start.AddMinutes(-5), clock.At, default);
        Assert.Equal(fresh, Assert.Single(await w.Repo.ListTaskPostsAsync(Tenant, [task], null, default)).HolderUserId);
    }

    [Fact]
    public async Task EndingAnAppointment_LeavesThePostVacant_AndTheRecordStays()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        clock.At = clock.At.AddDays(1);
        var ended = await w.Ok(appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default));
        Assert.Equal(clock.At, ended.EndDate);
        Assert.Null(await HolderAsync(w, post));
        Assert.Single(await w.Ok(appointments.GetPostHistoryAsync(Tenant, post, default)));
        Assert.Equal("not_found", (await appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default)).Error.Code);

        // The old way of freeing a post (taking it off someone's list) also ends, never deletes.
        clock.At = clock.At.AddDays(1);
        await w.Ok(appointments.AppointAsync(Tenant, post, new AppointRequest(old), default));
        await Done(w.Service.RemoveAssignmentAsync(Tenant, old, default));
        Assert.Null(await HolderAsync(w, post));
        Assert.Equal(2, (await w.Ok(appointments.GetPostHistoryAsync(Tenant, post, default))).Count);
    }

    [Fact]
    public async Task TodaysAppointments_WithNoDates_AreHeldAsBefore()
    {
        // As the upgrade leaves the data: rows with no start and no end.
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var post = await w.SeatAsync(it.Id, "کارشناس");
        var person = w.User("کارشناس");
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, person, new SetUserAssignmentRequest(w.Company, it.Id, null, null), default));
        var row = new UserPosition(Guid.NewGuid(), Tenant, person, post);
        w.Context.UserPositions.Add(row); // straight in, as old data: no dates
        await w.SaveAsync();
        Assert.Null(row.StartDate);
        Assert.Equal(person, await HolderAsync(w, post));
        Assert.True(row.IsActiveAt(DateTimeOffset.MinValue.AddDays(1)));
    }
}
