using Nexus.OrgStructure.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Company groups (phase 6.3): a company may sit under a parent company (a holding). Only a view of
/// the companies: who sees which company (CompanyReachScope), managers and everything else do not change.
/// </summary>
public interface IOrgCompanyGroupService
{
    Task<Result<CompanyDto>> SetParentCompanyAsync(Guid tenantId, Guid companyId, SetParentCompanyRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<CompanyGroupDto>>> GetCompanyGroupsAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed partial class OrgStructureService : IOrgCompanyGroupService
{
    public async Task<Result<CompanyDto>> SetParentCompanyAsync(Guid tenantId, Guid companyId, SetParentCompanyRequest request, CancellationToken cancellationToken)
    {
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(c => c.Id);
        if (!companies.TryGetValue(companyId, out var company) || !await IsMineAsync(tenantId, companyId, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        if (request.ParentCompanyId is { } parentId)
        {
            if (!companies.ContainsKey(parentId) || !await IsMineAsync(tenantId, parentId, cancellationToken))
            {
                return Result.Failure<CompanyDto>(Error.Validation("شرکت مادر پیدا نشد."));
            }

            // Not itself, nor a company below it.
            var seen = new HashSet<Guid>();
            for (Guid? current = parentId; current is { } id && seen.Add(id); current = companies.GetValueOrDefault(id)?.ParentCompanyId)
            {
                if (id == companyId)
                {
                    return Result.Failure<CompanyDto>(Error.Conflict("شرکت را نمی‌شود زیر خودش یا زیر شرکتی که زیرمجموعه‌ی آن است گذاشت."));
                }
            }
        }

        company.SetGroup(request.ParentCompanyId, company.TemplateId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<CompanyGroupDto>>> GetCompanyGroupsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken))
            .Where(c => mine is null || mine.Contains(c.Id)).ToDictionary(c => c.Id);
        var seen = new HashSet<Guid>();
        CompanyGroupDto Make(Domain.Company company) => new(company.Id, company.Name, company.Code, company.IsActive,
            companies.Values.Where(c => c.ParentCompanyId == company.Id && seen.Add(c.Id)).OrderBy(c => c.Name).Select(Make).ToList());

        // A company at the top: no parent, or a parent this user does not see (or that is gone).
        var tops = companies.Values.Where(c => c.ParentCompanyId is not { } p || !companies.ContainsKey(p)).OrderBy(c => c.Name).ToList();
        seen.UnionWith(tops.Select(c => c.Id));
        return Result.Success<IReadOnlyList<CompanyGroupDto>>(tops.Select(Make).ToList());
    }
}
