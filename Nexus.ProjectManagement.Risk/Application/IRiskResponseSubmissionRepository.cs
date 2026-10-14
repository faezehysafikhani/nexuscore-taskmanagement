using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskResponseSubmissionRepository
{
    Task<RiskResponseSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<RiskResponseSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RiskResponseSubmission>> ListByRiskAsync(Guid riskId, CancellationToken cancellationToken);
    Task AddAsync(RiskResponseSubmission submission, CancellationToken cancellationToken);
}
