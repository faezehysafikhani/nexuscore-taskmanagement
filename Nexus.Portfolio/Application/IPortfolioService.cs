using Nexus.Portfolio.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Portfolio.Application;

public interface IPortfolioService
{
    Task<Result<PortfolioResultDto>> GetPortfolioAsync(PortfolioQuery query, CancellationToken cancellationToken);
}
