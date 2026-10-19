using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Meetings.Application.Dtos;
using Nexus.ProjectManagement.Meetings.Domain;
using Nexus.ProjectManagement.Meetings.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class MeetingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    private static (MeetingService Service, MeetingDbContext Db) Create()
    {
        var options = new DbContextOptionsBuilder<MeetingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new MeetingDbContext(options);
        return (new MeetingService(new MeetingRepository(db), db), db);
    }

    private static CreateMeetingRequest NewMeeting(string title = "Weekly control", params MeetingParticipantInput[] participants) =>
        new(Tenant, Project, title, "Agenda", new DateOnly(2026, 10, 20), new TimeOnly(10, 30), 60, "Room 2", null, null, participants);

    [Fact]
    public async Task Create_StoresTheMeeting_WithTheCallerAsOrganizerAndTheParticipants()
    {
        var (service, _) = Create();
        var created = await service.CreateAsync(Tenant, Alice, NewMeeting("Weekly control", new MeetingParticipantInput(Bob), new MeetingParticipantInput(Carol, false)), default);

        Assert.True(created.IsSuccess);
        Assert.Equal(Alice, created.Value!.OrganizerUserId);
        Assert.Equal(MeetingStatus.Scheduled, created.Value.Status);
        Assert.Equal(2, created.Value.Participants.Count);
        Assert.False(created.Value.Participants.Single(p => p.UserId == Carol).IsRequired);
        Assert.All(created.Value.Participants, p => Assert.Equal(AttendanceStatus.Pending, p.Attendance));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RequiresATitle(string title)
    {
        var (service, _) = Create();
        var created = await service.CreateAsync(Tenant, Alice, NewMeeting(title), default);
        Assert.False(created.IsSuccess);
        Assert.Equal("validation.error", created.Error.Code);
    }

    [Fact]
    public async Task Create_RejectsADurationWithoutAStartTimeAndOutOfRangeDurations()
    {
        var (service, _) = Create();
        var noStart = await service.CreateAsync(Tenant, Alice, NewMeeting() with { StartTime = null }, default);
        var tooLong = await service.CreateAsync(Tenant, Alice, NewMeeting() with { DurationMinutes = 2000 }, default);
        var zero = await service.CreateAsync(Tenant, Alice, NewMeeting() with { DurationMinutes = 0 }, default);
        Assert.All(new[] { noStart, tooLong, zero }, result => Assert.False(result.IsSuccess));
    }

    [Fact]
    public async Task Participants_AreReplacedAsASet_AndAnAttendeeWhoStaysKeepsTheirAttendance()
    {
        var (service, _) = Create();
        var id = (await service.CreateAsync(Tenant, Alice, NewMeeting("M", new MeetingParticipantInput(Bob), new MeetingParticipantInput(Carol)), default)).Value!.Id;
        await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default);
        await service.SetAttendanceAsync(Tenant, id, Bob, new SetAttendanceRequest(AttendanceStatus.Attended), default);

        var updated = await service.SetParticipantsAsync(Tenant, id, new SetMeetingParticipantsRequest([new(Bob, false), new(Alice), new(Alice)]), default);

        Assert.True(updated.IsSuccess);
        Assert.Equal(new[] { Alice, Bob }.OrderBy(x => x), updated.Value!.Participants.Select(p => p.UserId).OrderBy(x => x));
        Assert.Equal(AttendanceStatus.Attended, updated.Value.Participants.Single(p => p.UserId == Bob).Attendance);
        Assert.False(updated.Value.Participants.Single(p => p.UserId == Bob).IsRequired);
    }

    [Fact]
    public async Task Attendance_AndMinutes_NeedAHeldMeeting()
    {
        var (service, _) = Create();
        var id = (await service.CreateAsync(Tenant, Alice, NewMeeting("M", new MeetingParticipantInput(Bob)), default)).Value!.Id;

        var attendance = await service.SetAttendanceAsync(Tenant, id, Bob, new SetAttendanceRequest(AttendanceStatus.Attended), default);
        var minutes = await service.SetMinutesAsync(Tenant, id, Alice, new SetMinutesRequest("Decisions"), default);
        Assert.Equal("conflict", attendance.Error.Code);
        Assert.Equal("conflict", minutes.Error.Code);

        await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default);
        var unknown = await service.SetAttendanceAsync(Tenant, id, Carol, new SetAttendanceRequest(AttendanceStatus.Attended), default);
        Assert.Equal("not_found", unknown.Error.Code);

        var recorded = await service.SetMinutesAsync(Tenant, id, Alice, new SetMinutesRequest("  Decisions  "), default);
        Assert.True(recorded.IsSuccess);
        Assert.Equal("Decisions", recorded.Value!.Minutes);
        Assert.Equal(Alice, recorded.Value.MinutesRecordedByUserId);
        Assert.NotNull(recorded.Value.MinutesRecordedAtUtc);

        var cleared = await service.SetMinutesAsync(Tenant, id, Alice, new SetMinutesRequest(" "), default);
        Assert.Null(cleared.Value!.Minutes);
        Assert.Null(cleared.Value.MinutesRecordedAtUtc);
    }

    [Fact]
    public async Task Status_FollowsTheAllowedTransitions()
    {
        var (service, _) = Create();
        var id = (await service.CreateAsync(Tenant, Alice, NewMeeting(), default)).Value!.Id;

        Assert.True((await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Cancelled), default)).IsSuccess);
        Assert.Equal("conflict", (await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default)).Error.Code);
        Assert.Equal("conflict", (await service.UpdateAsync(Tenant, id, new UpdateMeetingRequest("New", null, new DateOnly(2026, 10, 21), null, null, null, null, null), default)).Error.Code);
        Assert.True((await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Scheduled), default)).IsSuccess);

        Assert.True((await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default)).IsSuccess);
        Assert.Equal("conflict", (await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Cancelled), default)).Error.Code);

        // Back to scheduled only while there are no minutes.
        Assert.True((await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Scheduled), default)).IsSuccess);
        await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default);
        await service.SetMinutesAsync(Tenant, id, Alice, new SetMinutesRequest("Decisions"), default);
        Assert.Equal("conflict", (await service.ChangeStatusAsync(Tenant, id, new ChangeMeetingStatusRequest(MeetingStatus.Scheduled), default)).Error.Code);
    }

    [Fact]
    public async Task Delete_IsRefusedForAHeldMeeting_AndRemovesTheOthersWithTheirParticipants()
    {
        var (service, db) = Create();
        var scheduled = (await service.CreateAsync(Tenant, Alice, NewMeeting("A", new MeetingParticipantInput(Bob)), default)).Value!.Id;
        var held = (await service.CreateAsync(Tenant, Alice, NewMeeting("B", new MeetingParticipantInput(Bob)), default)).Value!.Id;
        await service.ChangeStatusAsync(Tenant, held, new ChangeMeetingStatusRequest(MeetingStatus.Held), default);

        Assert.Equal("conflict", (await service.DeleteAsync(Tenant, held, default)).Error.Code);
        Assert.True((await service.DeleteAsync(Tenant, scheduled, default)).IsSuccess);
        Assert.Equal("not_found", (await service.GetAsync(Tenant, scheduled, default)).Error.Code);
        Assert.Single(await db.MeetingParticipants.ToListAsync());
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeOrChangeTheMeeting()
    {
        var (service, _) = Create();
        var id = (await service.CreateAsync(Tenant, Alice, NewMeeting(), default)).Value!.Id;
        var stranger = Guid.NewGuid();

        Assert.Equal("not_found", (await service.GetAsync(stranger, id, default)).Error.Code);
        Assert.Equal("not_found", (await service.ChangeStatusAsync(stranger, id, new ChangeMeetingStatusRequest(MeetingStatus.Held), default)).Error.Code);
        Assert.Equal("not_found", (await service.DeleteAsync(stranger, id, default)).Error.Code);
        Assert.Empty((await service.ListByProjectAsync(stranger, Project, null, default)).Value!);
    }

    [Fact]
    public async Task Lists_AreFilteredByProjectAndStatus_NewestFirst()
    {
        var (service, _) = Create();
        var other = Guid.NewGuid();
        await service.CreateAsync(Tenant, Alice, NewMeeting("old") with { MeetingDate = new DateOnly(2026, 9, 1) }, default);
        var later = (await service.CreateAsync(Tenant, Alice, NewMeeting("new") with { MeetingDate = new DateOnly(2026, 11, 1) }, default)).Value!.Id;
        await service.CreateAsync(Tenant, Alice, NewMeeting("elsewhere") with { ProjectId = other }, default);
        await service.ChangeStatusAsync(Tenant, later, new ChangeMeetingStatusRequest(MeetingStatus.Held), default);

        var all = (await service.ListByProjectAsync(Tenant, Project, null, default)).Value!;
        Assert.Equal(new[] { "new", "old" }, all.Select(m => m.Title));
        Assert.Equal(new[] { "new" }, (await service.ListByProjectAsync(Tenant, Project, MeetingStatus.Held, default)).Value!.Select(m => m.Title));
        Assert.Equal(3, (await service.ListForProjectsAsync(Tenant, new ListMeetingsForProjectsRequest([Project, other, Project]), default)).Value!.Count);
        Assert.Empty((await service.ListForProjectsAsync(Tenant, new ListMeetingsForProjectsRequest([]), default)).Value!);
    }
}
