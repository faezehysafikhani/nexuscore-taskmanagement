using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityService
{
    Task<Result<IReadOnlyList<ActivityDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ActivityDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ActivityDto>> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken);
    Task<Result<ActivityDto>> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken);
    Task<Result<ActivityDto>> UpdateProgressAsync(Guid id, UpdateActivityProgressRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ActivityDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
