using Nexus.ProjectManagement.Meetings.Application.Dtos;
using Nexus.ProjectManagement.Meetings.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Meetings.Application;

public sealed class MeetingService(
    IMeetingRepository repository,
    IMeetingUnitOfWork unitOfWork,
    TimeProvider? timeProvider = null) : IMeetingService
{
    public const int MaxParticipants = 200;
    public const int MaxMinutesLength = 20000;

    public async Task<Result<IReadOnlyList<MeetingDto>>> ListByProjectAsync(Guid tenantId, Guid projectId, MeetingStatus? status, CancellationToken cancellationToken)
    {
        var meetings = await repository.ListByProjectAsync(projectId, status, cancellationToken);
        return Result.Success<IReadOnlyList<MeetingDto>>(meetings.Where(m => m.TenantId == tenantId).Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<MeetingDto>>> ListForProjectsAsync(Guid tenantId, ListMeetingsForProjectsRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProjectIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<MeetingDto>>([]);
        }

        var meetings = await repository.ListForProjectsAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<MeetingDto>>(meetings.Where(m => m.TenantId == tenantId).Select(ToDto).ToList());
    }

    public async Task<Result<MeetingDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        return meeting is null ? NotFound() : Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> CreateAsync(Guid tenantId, Guid? callerUserId, CreateMeetingRequest request, CancellationToken cancellationToken)
    {
        var invalid = ValidateDetails(request.Title, request.Agenda, request.StartTime, request.DurationMinutes, request.Location, request.OnlineLink)
            ?? ValidateParticipants(request.Participants ?? []);
        if (invalid is not null)
        {
            return Result.Failure<MeetingDto>(invalid);
        }

        if (request.ProjectId == Guid.Empty)
        {
            return Result.Failure<MeetingDto>(Error.Validation("Project is required."));
        }

        var meeting = new Meeting(Guid.NewGuid(), tenantId, request.ProjectId, request.Title, request.MeetingDate);
        meeting.UpdateDetails(
            request.Title, request.Agenda, request.MeetingDate, request.StartTime, request.DurationMinutes,
            request.Location, request.OnlineLink, request.OrganizerUserId ?? callerUserId);
        meeting.SetParticipants((request.Participants ?? []).Select(p => (p.UserId, p.IsRequired)));

        await repository.AddAsync(meeting, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> UpdateAsync(Guid tenantId, Guid id, UpdateMeetingRequest request, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        if (meeting.Status == MeetingStatus.Cancelled)
        {
            return Result.Failure<MeetingDto>(Error.Conflict("A cancelled meeting cannot be edited; reopen it first."));
        }

        var invalid = ValidateDetails(request.Title, request.Agenda, request.StartTime, request.DurationMinutes, request.Location, request.OnlineLink);
        if (invalid is not null)
        {
            return Result.Failure<MeetingDto>(invalid);
        }

        meeting.UpdateDetails(
            request.Title, request.Agenda, request.MeetingDate, request.StartTime, request.DurationMinutes,
            request.Location, request.OnlineLink, request.OrganizerUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> ChangeStatusAsync(Guid tenantId, Guid id, ChangeMeetingStatusRequest request, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        if (meeting.Status == request.Status)
        {
            return Result.Success(ToDto(meeting));
        }

        if (!Enum.IsDefined(request.Status))
        {
            return Result.Failure<MeetingDto>(Error.Validation("Unknown meeting status."));
        }

        // Scheduled -> Held or Cancelled; Cancelled -> Scheduled (reopen); Held -> Scheduled only while no minutes exist; Held is never cancelled.
        var error = (meeting.Status, request.Status) switch
        {
            (MeetingStatus.Held, MeetingStatus.Cancelled) => "A held meeting cannot be cancelled.",
            (MeetingStatus.Held, MeetingStatus.Scheduled) when meeting.Minutes is not null => "Minutes were already recorded for this meeting, so it cannot go back to scheduled.",
            (MeetingStatus.Cancelled, MeetingStatus.Held) => "A cancelled meeting must be reopened before it can be marked as held.",
            _ => null
        };
        if (error is not null)
        {
            return Result.Failure<MeetingDto>(Error.Conflict(error));
        }

        meeting.SetStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> SetParticipantsAsync(Guid tenantId, Guid id, SetMeetingParticipantsRequest request, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        if (meeting.Status == MeetingStatus.Cancelled)
        {
            return Result.Failure<MeetingDto>(Error.Conflict("A cancelled meeting cannot be edited; reopen it first."));
        }

        var invalid = ValidateParticipants(request.Participants);
        if (invalid is not null)
        {
            return Result.Failure<MeetingDto>(invalid);
        }

        meeting.SetParticipants(request.Participants.Select(p => (p.UserId, p.IsRequired)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> SetAttendanceAsync(Guid tenantId, Guid id, Guid userId, SetAttendanceRequest request, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        if (meeting.Status != MeetingStatus.Held)
        {
            return Result.Failure<MeetingDto>(Error.Conflict("Attendance can only be recorded for a held meeting."));
        }

        if (!Enum.IsDefined(request.Attendance))
        {
            return Result.Failure<MeetingDto>(Error.Validation("Unknown attendance status."));
        }

        if (!meeting.SetAttendance(userId, request.Attendance))
        {
            return Result.Failure<MeetingDto>(Error.NotFound("Participant not found."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result<MeetingDto>> SetMinutesAsync(Guid tenantId, Guid id, Guid? callerUserId, SetMinutesRequest request, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        if (meeting.Status != MeetingStatus.Held)
        {
            return Result.Failure<MeetingDto>(Error.Conflict("Minutes can only be recorded for a held meeting."));
        }

        if (request.Minutes is { Length: > MaxMinutesLength })
        {
            return Result.Failure<MeetingDto>(Error.Validation($"The minutes are at most {MaxMinutesLength} characters."));
        }

        meeting.RecordMinutes(request.Minutes, callerUserId, (timeProvider ?? TimeProvider.System).GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(meeting));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var meeting = await FindAsync(tenantId, id, cancellationToken);
        if (meeting is null)
        {
            return Result.Failure(Error.NotFound("Meeting not found."));
        }

        if (meeting.Status == MeetingStatus.Held)
        {
            return Result.Failure(Error.Conflict("A held meeting is part of the project record and cannot be deleted."));
        }

        await repository.RemoveAsync(meeting, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Meeting?> FindAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var meeting = await repository.GetByIdAsync(id, cancellationToken);
        return meeting is not null && meeting.TenantId == tenantId ? meeting : null;
    }

    private static Result<MeetingDto> NotFound() => Result.Failure<MeetingDto>(Error.NotFound("Meeting not found."));

    private static Error? ValidateDetails(string? title, string? agenda, TimeOnly? startTime, int? durationMinutes, string? location, string? onlineLink)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("Title is required.");
        }

        if (title.Trim().Length > 200)
        {
            return Error.Validation("The title is at most 200 characters.");
        }

        if (agenda is { Length: > 4000 })
        {
            return Error.Validation("The agenda is at most 4000 characters.");
        }

        if (durationMinutes is { } minutes && (minutes < 1 || minutes > 1440))
        {
            return Error.Validation("The duration must be between 1 and 1440 minutes.");
        }

        if (durationMinutes is not null && startTime is null)
        {
            return Error.Validation("A duration needs a start time.");
        }

        if (location is { Length: > 300 })
        {
            return Error.Validation("The location is at most 300 characters.");
        }

        if (onlineLink is { Length: > 500 })
        {
            return Error.Validation("The online link is at most 500 characters.");
        }

        return null;
    }

    private static Error? ValidateParticipants(IReadOnlyList<MeetingParticipantInput> participants)
    {
        if (participants.Any(p => p.UserId == Guid.Empty))
        {
            return Error.Validation("Every participant needs a user.");
        }

        return participants.Select(p => p.UserId).Distinct().Count() > MaxParticipants
            ? Error.Validation($"A meeting has at most {MaxParticipants} participants.")
            : null;
    }

    private static MeetingDto ToDto(Meeting meeting) => new(
        meeting.Id, meeting.TenantId, meeting.ProjectId, meeting.Title, meeting.Agenda,
        meeting.MeetingDate, meeting.StartTime, meeting.DurationMinutes, meeting.Location, meeting.OnlineLink,
        meeting.OrganizerUserId, meeting.Status,
        meeting.Minutes, meeting.MinutesRecordedByUserId, meeting.MinutesRecordedAtUtc,
        meeting.Participants.Select(p => new MeetingParticipantDto(p.UserId, p.IsRequired, p.Attendance)).ToList(),
        meeting.CreatedAtUtc);
}
