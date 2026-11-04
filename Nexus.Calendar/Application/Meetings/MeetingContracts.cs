using NexusCore.SharedKernel.Results;

namespace Nexus.Calendar.Application.Meetings;

public sealed record MeetingAgenda(string Title, int DurationMinutes, Guid? PresenterUserId);

public sealed record MeetingAction(string Title, Guid? AssigneeUserId, DateOnly? DueDate, string Status = "todo");

public sealed record MeetingInput(
    string Title,
    string Type,
    DateOnly Date,
    string StartTime,
    string EndTime,
    string? Location,
    Guid OrganizerUserId,
    string? Description,
    IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyList<MeetingAgenda> Agenda,
    string? Minutes,
    IReadOnlyList<MeetingAction> Actions,
    IReadOnlyList<Guid> TaskIds,
    bool SendInvitationSms = false,
    int? Version = null);

public sealed record MeetingDto(
    Guid Id,
    Guid CreatedByUserId,
    int Version,
    MeetingInput Details,
    bool CanEdit,
    bool CanDelete,
    string? Notice = null);

public sealed record MeetingTaskOption(Guid Id, string Title, string Status, DateOnly DueDate, bool IsProject);

public sealed record MeetingTaskPage(
    IReadOnlyList<MeetingTaskOption> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record MeetingPerson(Guid Id, string Name, string PositionTitle, string Relation);

public sealed record MeetingPeopleOptions(
    bool CanCreate,
    bool CanInviteHigher,
    IReadOnlyList<MeetingPerson> People,
    string? Message = null);

public interface IMeetingPeopleScope
{
    Task<MeetingPeopleOptions> GetAsync(CancellationToken cancellationToken);
}

public interface IMeetingTaskDirectory
{
    Task<Result<MeetingTaskPage>> SearchAsync(
        string? search,
        int pageNumber,
        int pageSize,
        string? ids,
        CancellationToken cancellationToken);

    Task<bool> CanAccessAsync(Guid taskId, CancellationToken cancellationToken);
}
