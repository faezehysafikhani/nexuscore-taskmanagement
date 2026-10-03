using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityDependencyService
{
    Task<Result<IReadOnlyList<ActivityDependencyDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ActivityDependencyDto>> CreateAsync(CreateActivityDependencyRequest request, CancellationToken cancellationToken);
    Task<Result<ActivityDependencyDto>> UpdateAsync(Guid id, UpdateActivityDependencyRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
