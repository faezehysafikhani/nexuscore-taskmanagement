using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

public sealed partial class OrgNodeApiTests
{
    [Fact]
    public async Task UserAssignment_DateOnlyPeriod_IsActiveUntilExactlyTehranMidnight()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        clock.At = clock.At.AddHours(1);
        await w.Ok(appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default));
        var seat = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post);
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(3.5));
        var end = start.AddDays(1);
        clock.At = start;
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, seat.UnitId, seat.PositionId, null,
            [post], StartDate: start, EndDate: end), default));
        Assert.Equal(old, await HolderAsync(w, post));
        Assert.Contains(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);

        clock.At = end.AddTicks(-1);
        Assert.Equal(old, await HolderAsync(w, post));
        clock.At = end;
        Assert.Null(await HolderAsync(w, post));
        Assert.DoesNotContain(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);
        Assert.All(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)), a => Assert.False(a.IsActive));
        clock.At = end.AddDays(1);
        Assert.Null(await HolderAsync(w, post));
        var saved = (await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default))).Single(a => a.StartDate == start);
        Assert.Equal(end, saved.EndDate);
    }

    [Fact]
    public async Task UserAssignment_WithoutEndDate_RemainsActive()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        clock.At = clock.At.AddHours(1);
        await w.Ok(appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default));
        var seat = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post);
        clock.At = clock.At.AddDays(1);
        var start = clock.At.AddHours(-1);
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, seat.UnitId, seat.PositionId, null,
            [post], StartDate: start), default));
        clock.At = clock.At.AddYears(1);
        Assert.Equal(old, await HolderAsync(w, post));
        Assert.Contains(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);
        var saved = (await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default))).Single(a => a.IsActive);
        Assert.Equal(start, saved.StartDate);
        Assert.Null(saved.EndDate);
    }

    [Theory]
    [InlineData(1, 2)] // Future start is rejected by the user-placement form's API.
    [InlineData(0, 0)] // Equal dates.
    [InlineData(0, -1)] // Reversed dates.
    public async Task UserAssignment_InvalidDates_DoNotChangeTheCurrentAppointment(int startDays, int endDays)
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        var seat = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post);
        var result = await w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, seat.UnitId, seat.PositionId, null,
            [post], StartDate: clock.At.AddDays(startDays), EndDate: clock.At.AddDays(endDays)), default);
        Assert.True(result.IsFailure);
        Assert.Equal("validation.error", result.Error.Code);
        Assert.Equal(old, await HolderAsync(w, post));
        Assert.Single(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)));
    }

    [Fact]
    public async Task RemovingUserPlacement_EndsTheAppointment_AndRemovesAssignableHolder()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        Assert.Contains(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);
        clock.At = clock.At.AddDays(1);
        await Done(w.Service.RemoveAssignmentAsync(Tenant, old, default));
        Assert.Null(await HolderAsync(w, post));
        Assert.DoesNotContain(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);
        var vacant = (await w.TaskPosts.ListPostsAsync(Tenant, default)).Single(p => p.PostId == post);
        Assert.Null(vacant.HolderUserId);
        var history = Assert.Single(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)));
        Assert.False(history.IsActive);
        Assert.Equal(clock.At, history.EndDate);
    }

    [Fact]
    public async Task UserAssignment_WithPeriod_ExpiresAndKeepsHistory_ThenAddsANewRow()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        clock.At = clock.At.AddDays(1);
        await w.Ok(appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default));
        Assert.Null(await HolderAsync(w, post));
        Assert.DoesNotContain(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);

        var current = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post);
        clock.At = clock.At.AddDays(1);
        var start = clock.At; var end = start.AddDays(10);
        await w.Ok(w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, current.UnitId, current.PositionId, null,
            [post], StartDate: start, EndDate: end), default));
        var rows = await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default));
        Assert.Equal(2, rows.Count); Assert.Single(rows, a => a.IsActive);
        Assert.Contains(rows, a => a.StartDate == start && a.EndDate == end);

        clock.At = end;
        Assert.Null(await HolderAsync(w, post));
        Assert.DoesNotContain(old, (await w.TaskPosts.GetMainPostsAsync(Tenant, [old], default)).Keys);
        Assert.Equal(2, (await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default))).Count);
    }

    [Fact]
    public async Task UserAssignment_RejectsOverlappingHistoryAndReversedPeriod()
    {
        var (w, clock, appointments, post, old, _) = await PostWithHolderAsync();
        var current = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post);
        clock.At = clock.At.AddDays(1);
        await w.Ok(appointments.EndAppointmentAsync(Tenant, post, new EndAppointmentRequest(), default));
        var start = clock.At.AddHours(-1);
        var overlap = await w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, current.UnitId, current.PositionId, null,
            [post], StartDate: start, EndDate: clock.At.AddDays(1)), default);
        Assert.True(overlap.IsFailure);
        var reversed = await w.Service.SetAssignmentAsync(Tenant, old, new SetUserAssignmentRequest(w.Company, current.UnitId, current.PositionId, null,
            [post], StartDate: clock.At, EndDate: clock.At), default);
        Assert.True(reversed.IsFailure);
        Assert.Single(await w.Ok(appointments.GetUserHistoryAsync(Tenant, old, default)));
    }
}
