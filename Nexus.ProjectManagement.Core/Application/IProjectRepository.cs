using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken);
    Task AddAsync(Project project, CancellationToken cancellationToken);

    /// <summary>Deletes the project row. A repository written before deletion existed need not implement it (deleting then reports as unsupported).</summary>
    Task RemoveAsync(Project project, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This project repository does not support deleting projects.");
}
