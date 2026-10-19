using Nexus.ProjectManagement.Meetings.Domain;

namespace Nexus.ProjectManagement.Meetings.Application.Dtos;

public sealed record MeetingParticipantDto(Guid UserId, bool IsRequired, AttendanceStatus Attendance);

public sealed record MeetingDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, string? Agenda,
    DateOnly MeetingDate, TimeOnly? StartTime, int? DurationMinutes, string? Location, string? OnlineLink,
    Guid? OrganizerUserId, MeetingStatus Status,
    string? Minutes, Guid? MinutesRecordedByUserId, DateTimeOffset? MinutesRecordedAtUtc,
    IReadOnlyList<MeetingParticipantDto> Participants, DateTimeOffset CreatedAtUtc);

public sealed record MeetingParticipantInput(Guid UserId, bool IsRequired = true);

/// <summary>TenantId and OrganizerUserId are filled in from the signed-in user by the endpoint when left empty.</summary>
public sealed record CreateMeetingRequest(
    Guid TenantId, Guid ProjectId, string Title, string? Agenda,
    DateOnly MeetingDate, TimeOnly? StartTime, int? DurationMinutes, string? Location, string? OnlineLink,
    Guid? OrganizerUserId, IReadOnlyList<MeetingParticipantInput>? Participants);

public sealed record UpdateMeetingRequest(
    string Title, string? Agenda, DateOnly MeetingDate, TimeOnly? StartTime, int? DurationMinutes,
    string? Location, string? OnlineLink, Guid? OrganizerUserId);

public sealed record ChangeMeetingStatusRequest(MeetingStatus Status);

public sealed record SetMeetingParticipantsRequest(IReadOnlyList<MeetingParticipantInput> Participants);

public sealed record SetAttendanceRequest(AttendanceStatus Attendance);

public sealed record SetMinutesRequest(string? Minutes);

public sealed record ListMeetingsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
