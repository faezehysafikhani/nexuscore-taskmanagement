using Nexus.Portfolio.Application.Dtos;
using Nexus.ProjectManagement.Core.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Portfolio.Application;

public interface IPortfolioService
{
    Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken cancellationToken);

    /// <summary>The projects the caller may see, in full - the same visibility rule as the portfolio, with the portfolio's filters
    /// (only the project ones apply). Archived projects are left out unless asked for.</summary>
    Task<Result<IReadOnlyList<ProjectDto>>> GetVisibleProjectsAsync(PortfolioQuery query, bool includeArchived, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This portfolio service does not list visible projects.");
}
