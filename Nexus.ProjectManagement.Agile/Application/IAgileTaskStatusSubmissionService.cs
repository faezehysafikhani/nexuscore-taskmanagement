using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileTaskStatusSubmissionService
{
    Task<Result<IReadOnlyList<AgileTaskStatusSubmissionDto>>> ListForProjectsAsync(ListAgileForProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AgileTaskStatusSubmissionDto>>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result<AgileTaskStatusSubmissionDto>> SubmitAsync(SubmitAgileTaskStatusRequest request, CancellationToken cancellationToken);
}
