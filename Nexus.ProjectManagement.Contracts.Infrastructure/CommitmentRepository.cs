using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public sealed class CommitmentRepository(ContractsDbContext dbContext) : ICommitmentRepository
{
    public Task<ContractMilestone?> GetMilestoneAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContractMilestones.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractMilestone>> ListMilestonesAsync(Guid contractId, CancellationToken cancellationToken) =>
        await dbContext.ContractMilestones.Where(m => m.ContractId == contractId).ToListAsync(cancellationToken);

    public async Task AddMilestoneAsync(ContractMilestone milestone, CancellationToken cancellationToken) =>
        await dbContext.ContractMilestones.AddAsync(milestone, cancellationToken);

    public Task RemoveMilestoneAsync(ContractMilestone milestone, CancellationToken cancellationToken)
    {
        dbContext.ContractMilestones.Remove(milestone);
        return Task.CompletedTask;
    }

    public Task<ContractObligation?> GetObligationAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContractObligations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractObligation>> ListObligationsAsync(Guid contractId, CancellationToken cancellationToken) =>
        await dbContext.ContractObligations.Where(o => o.ContractId == contractId).ToListAsync(cancellationToken);

    public async Task AddObligationAsync(ContractObligation obligation, CancellationToken cancellationToken) =>
        await dbContext.ContractObligations.AddAsync(obligation, cancellationToken);

    public Task RemoveObligationAsync(ContractObligation obligation, CancellationToken cancellationToken)
    {
        dbContext.ContractObligations.Remove(obligation);
        return Task.CompletedTask;
    }

    public Task<ContractLink?> GetLinkAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContractLinks.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContractLink>> ListLinksAsync(Guid contractId, CancellationToken cancellationToken) =>
        await dbContext.ContractLinks.Where(l => l.ContractId == contractId).ToListAsync(cancellationToken);

    public async Task AddLinkAsync(ContractLink link, CancellationToken cancellationToken) =>
        await dbContext.ContractLinks.AddAsync(link, cancellationToken);

    public Task RemoveLinkAsync(ContractLink link, CancellationToken cancellationToken)
    {
        dbContext.ContractLinks.Remove(link);
        return Task.CompletedTask;
    }

    public async Task RemoveLinksOfSubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        dbContext.ContractLinks.RemoveRange(await dbContext.ContractLinks.Where(l => l.SubjectId == subjectId).ToListAsync(cancellationToken));

    public async Task RemoveAllOfContractAsync(Guid contractId, CancellationToken cancellationToken)
    {
        dbContext.ContractLinks.RemoveRange(await dbContext.ContractLinks.Where(l => l.ContractId == contractId).ToListAsync(cancellationToken));
        dbContext.ContractMilestones.RemoveRange(await dbContext.ContractMilestones.Where(m => m.ContractId == contractId).ToListAsync(cancellationToken));
        dbContext.ContractObligations.RemoveRange(await dbContext.ContractObligations.Where(o => o.ContractId == contractId).ToListAsync(cancellationToken));
    }
}
