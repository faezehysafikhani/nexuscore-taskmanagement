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

    /// <summary>Whether another project of the tenant already has this name (ignoring case). A repository written before this existed says no.</summary>
    Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludeId, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>The next free automatic project code for the tenant ("P0001", "P0002"...), or null when this repository does not generate them.</summary>
    Task<string?> NextCodeAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>Deletes the project row. A repository written before deletion existed need not implement it (deleting then reports as unsupported).</summary>
    Task RemoveAsync(Project project, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This project repository does not support deleting projects.");
}
