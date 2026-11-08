using Nexus.ProjectManagement.Core.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public interface IProjectProgressService
{
    /// <summary>Planned and actual progress of the given projects, read from whichever planning modules are installed. A project that does
    /// not exist (or is outside the caller's tenant) is left out.</summary>
    Task<Result<IReadOnlyList<ProjectProgressDto>>> GetAsync(Guid tenantId, ProjectProgressRequest request, CancellationToken cancellationToken);
}
