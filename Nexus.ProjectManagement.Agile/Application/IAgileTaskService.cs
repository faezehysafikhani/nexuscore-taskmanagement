using Nexus.ProjectManagement.Agile.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Agile.Application;

public interface IAgileTaskService
{
    Task<Result<IReadOnlyList<AgileTaskDto>>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> CreateAsync(CreateAgileTaskRequest request, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> UpdateAsync(Guid id, UpdateAgileTaskRequest request, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> ChangeStatusAsync(Guid id, ChangeAgileTaskStatusRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<AgileTaskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
