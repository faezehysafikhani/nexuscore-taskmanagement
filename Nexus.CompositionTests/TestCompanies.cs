using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// The system holds one company, so the service refuses a second. Tests about several companies (reach, groups,
/// «other company») need them as data: the first is made through the service, the rest are stored directly.
/// </summary>
internal static class TestCompanies
{
    public static async Task<Guid> MakeAsync(
        IOrgStructureService service, IOrgStructureRepository repository, IUnitOfWork unitOfWork,
        Guid tenantId, int madeSoFar, string name, string code)
    {
        if (madeSoFar == 0)
        {
            return (await service.CreateCompanyAsync(tenantId, new CreateCompanyRequest(name, code, null), default)).Value!.Id;
        }

        return await StoreAsync(repository, unitOfWork, tenantId, name, code);
    }

    /// <summary>A company put straight into the store, past the one-company rule.</summary>
    public static async Task<Guid> StoreAsync(IOrgStructureRepository repository, IUnitOfWork unitOfWork, Guid tenantId, string name, string code)
    {
        var company = new Company(Guid.NewGuid(), tenantId, name, code, null, managerUserId: null);
        await repository.AddCompanyAsync(company, default);
        await unitOfWork.SaveChangesAsync();
        return company.Id;
    }
}
