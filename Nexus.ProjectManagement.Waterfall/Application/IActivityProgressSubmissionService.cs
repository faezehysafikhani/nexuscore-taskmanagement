using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityProgressSubmissionService
{
    Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListForProjectsAsync(ListProgressSubmissionsForProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ActivityProgressSubmissionDto>>> ListByActivityAsync(Guid activityId, CancellationToken cancellationToken);
    Task<Result<ActivityProgressSubmissionDto>> SubmitAsync(SubmitActivityProgressRequest request, CancellationToken cancellationToken);
    Task<Result<ActivityProgressSubmissionDto>> SetConfirmedProgressAsync(Guid id, SetConfirmedProgressRequest request, CancellationToken cancellationToken);
}
