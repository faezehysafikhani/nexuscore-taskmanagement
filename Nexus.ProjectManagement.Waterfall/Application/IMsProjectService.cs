using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IMsProjectService
{
    /// <summary>
    /// Builds the project's WBS and dependencies from an MS Project XML file. A project that
    /// already has activities is refused (Conflict) unless <paramref name="replaceExisting"/> is
    /// true, which first deletes every existing activity and dependency of the project.
    /// </summary>
    Task<Result<MsProjectImportResultDto>> ImportAsync(
        Guid tenantId, Guid projectId, Stream xml, bool replaceExisting, CancellationToken cancellationToken);

    /// <summary>The project's calculated schedule as an MS Project XML file.</summary>
    Task<Result<MsProjectExportDto>> ExportAsync(Guid projectId, CancellationToken cancellationToken);
}
