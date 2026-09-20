using Nexus.ProjectManagement.Core.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public interface IProjectService
{
    Task<Result<PagedResult<ProjectDto>>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);
    Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
