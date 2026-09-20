using Nexus.ProjectManagement.Progress.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Progress.Application;

public interface IProgressService
{
    Task<Result<IReadOnlyList<ProgressUpdateDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ProgressUpdateDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProgressUpdateDto>> CreateAsync(CreateProgressUpdateRequest request, CancellationToken cancellationToken);
    Task<Result<ProgressUpdateDto>> UpdateAsync(Guid id, UpdateProgressUpdateRequest request, CancellationToken cancellationToken);
    Task<Result<ProgressUpdateDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
