using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IActivityDependencyService
{
    Task<Result<IReadOnlyList<ActivityDependencyDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<ActivityDependencyDto>> CreateAsync(CreateActivityDependencyRequest request, CancellationToken cancellationToken);
    Task<Result<ActivityDependencyDto>> UpdateAsync(Guid id, UpdateActivityDependencyRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sets the activity's single predecessor (replacing the links that led into it), or clears them. Returns the links that
    /// now lead into the activity. A service written before this existed reports it as unsupported.</summary>
    Task<Result<IReadOnlyList<ActivityDependencyDto>>> SetPredecessorAsync(SetActivityPredecessorRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This dependency service cannot set a single predecessor.");
}
