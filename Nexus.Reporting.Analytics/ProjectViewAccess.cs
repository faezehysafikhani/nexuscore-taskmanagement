using Nexus.Portfolio.Application;
using Nexus.Portfolio.Application.Dtos;

namespace Nexus.Reporting.Analytics;

/// <summary>
/// Whether a person who does not hold Reporting.ViewAll may look at one project's figures. It is the same
/// rule as the portfolio's - the projects they own or manage, those they are a team member of, the
/// units they manage, and what waits for their approval - so a project manager sees their own team's
/// projects and nobody else's. Without a portfolio the answer is always no (nothing is widened).
/// </summary>
public interface IProjectViewAccess
{
    Task<bool> CanViewAsync(Guid tenantId, Guid userId, Guid projectId, CancellationToken cancellationToken);
}

/// <summary>Asks the portfolio, which owns the visibility rule, instead of repeating it here.</summary>
public sealed class PortfolioProjectViewAccess(IPortfolioService? portfolio = null) : IProjectViewAccess
{
    public async Task<bool> CanViewAsync(Guid tenantId, Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        if (portfolio is null)
        {
            return false;
        }

        try
        {
            var visible = await portfolio.GetVisibleProjectsAsync(
                new PortfolioQuery(tenantId, userId, ViewAll: false, OrganizationUnitId: null, Status: null),
                includeArchived: true, cancellationToken);
            return visible.IsSuccess && visible.Value!.Any(project => project.Id == projectId && project.TenantId == tenantId);
        }
        catch (NotSupportedException)
        {
            // A portfolio that cannot list visible projects grants nothing.
            return false;
        }
    }
}
