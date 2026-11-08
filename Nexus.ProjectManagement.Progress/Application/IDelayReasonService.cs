using Nexus.ProjectManagement.Progress.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IDelayReasonService
{
    Task<Result<IReadOnlyList<DelayReasonDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The items of several projects in one call.</summary>
    Task<Result<IReadOnlyList<DelayReasonDto>>> ListForProjectsAsync(ListDelayReasonsForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<Result<DelayReasonDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<DelayReasonDto>> CreateAsync(CreateDelayReasonRequest request, CancellationToken cancellationToken);
    Task<Result<DelayReasonDto>> UpdateAsync(Guid id, UpdateDelayReasonRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<DelayReasonDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
