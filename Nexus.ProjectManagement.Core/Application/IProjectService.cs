using Nexus.ProjectManagement.Core.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public interface IProjectService
{
    Task<Result<PagedResult<ProjectDto>>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>A dry run of create (or, with <paramref name="existingProjectId"/>, of update): the same checks - required parts, charter rules,
    /// unique name and code - without saving anything. Success means the request would be accepted.</summary>
    Task<Result> CheckAsync(CreateProjectRequest request, Guid? existingProjectId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);
    Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Moves the project to Draft, Active, OnHold or Completed. An archived project stays archived.</summary>
    Task<Result<ProjectDto>> ChangeStatusAsync(Guid id, ChangeProjectStatusRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the project for good. Refused (409) while any installed module still holds data about it.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
