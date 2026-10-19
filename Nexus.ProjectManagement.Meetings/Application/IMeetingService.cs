using Nexus.ProjectManagement.Meetings.Application.Dtos;
using Nexus.ProjectManagement.Meetings.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Meetings.Application;

public interface IMeetingService
{
    Task<Result<IReadOnlyList<MeetingDto>>> ListByProjectAsync(Guid tenantId, Guid projectId, MeetingStatus? status, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<MeetingDto>>> ListForProjectsAsync(Guid tenantId, ListMeetingsForProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> CreateAsync(Guid tenantId, Guid? callerUserId, CreateMeetingRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> UpdateAsync(Guid tenantId, Guid id, UpdateMeetingRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> ChangeStatusAsync(Guid tenantId, Guid id, ChangeMeetingStatusRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> SetParticipantsAsync(Guid tenantId, Guid id, SetMeetingParticipantsRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> SetAttendanceAsync(Guid tenantId, Guid id, Guid userId, SetAttendanceRequest request, CancellationToken cancellationToken);
    Task<Result<MeetingDto>> SetMinutesAsync(Guid tenantId, Guid id, Guid? callerUserId, SetMinutesRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}
