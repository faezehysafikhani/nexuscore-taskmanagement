using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public interface IWaterfallSettingsService
{
    Task<Result<WaterfallSettingsDto>> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<WaterfallSettingsDto>>> GetForProjectsAsync(WaterfallSettingsForProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<WaterfallSettingsDto>> SaveAsync(SaveWaterfallSettingsRequest request, CancellationToken cancellationToken);
}
