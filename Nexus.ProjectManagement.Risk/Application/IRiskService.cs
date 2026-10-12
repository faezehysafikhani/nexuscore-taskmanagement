using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskService
{
    Task<Result<IReadOnlyList<RiskDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    /// <summary>The risks of several projects in one call.</summary>
    Task<Result<IReadOnlyList<RiskDto>>> ListForProjectsAsync(ListRisksForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>Moves a risk's response along (Planning, InProgress, OnHold, Completed). Only an approved risk has a response to report on.</summary>
    Task<Result<RiskDto>> ChangeResponseStatusAsync(Guid id, ChangeRiskResponseStatusRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<RiskSettingsDto>> GetSettingsAsync(Guid projectId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<IReadOnlyList<RiskSettingsDto>>> GetSettingsForProjectsAsync(RiskSettingsForProjectsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<RiskSettingsDto>> SaveSettingsAsync(SaveRiskSettingsRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<RiskMatrixDto>> GetMatrixAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<RiskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RiskDto>> CreateAsync(CreateRiskRequest request, CancellationToken cancellationToken);
    Task<Result<RiskDto>> UpdateAsync(Guid id, UpdateRiskRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RiskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
