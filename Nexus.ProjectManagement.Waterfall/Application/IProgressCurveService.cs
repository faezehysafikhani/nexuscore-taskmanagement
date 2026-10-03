using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IProgressCurveService
{
    Task<Result<IReadOnlyList<ProgressSnapshotDto>>> ListSnapshotsAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Records the project's progress as of a date (today by default), replacing an earlier snapshot of the same date.</summary>
    Task<Result<ProgressSnapshotDto>> CreateSnapshotAsync(CreateProgressSnapshotRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteSnapshotAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<SCurveDto>> GetSCurveAsync(Guid projectId, int stepDays, CancellationToken cancellationToken);
}
