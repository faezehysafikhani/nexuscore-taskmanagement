using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public sealed class RiskRepository(RiskManagementDbContext dbContext) : IRiskRepository
{
    public Task<Risk?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Risks.SingleOrDefaultAsync(risk => risk.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Risk>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Risks
            .Where(risk => risk.ProjectId == projectId)
            .OrderByDescending(risk => risk.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Risk risk, CancellationToken cancellationToken)
    {
        await dbContext.Risks.AddAsync(risk, cancellationToken);
    }
}
