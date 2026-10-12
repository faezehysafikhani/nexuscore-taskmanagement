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
    /// <summary>Sets the actual progress alone (the stored planned progress is untouched). A service written before this existed reports it as unsupported.</summary>
    Task<Result<ActivityDto>> UpdateActualProgressAsync(Guid id, UpdateActivityActualProgressRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This activity service cannot set actual progress alone.");

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Deletes the activity together with everything under it (and their dependency links). A service written before this
    /// existed reports it as unsupported.</summary>
    Task<Result> DeleteWithSubActivitiesAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This activity service cannot delete an activity with its sub-activities.");
    Task<Result<ActivityDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
