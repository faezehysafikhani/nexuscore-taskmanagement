using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Infrastructure;

public sealed class OrgStructureRepository(OrgStructureDbContext dbContext) : IOrgStructureRepository
{
    public async Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Companies.Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<Company?> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Companies.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> CompanyCodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Companies.AnyAsync(x => x.TenantId == tenantId && x.Code == code && x.Id != excludeId, cancellationToken);

    public async Task AddCompanyAsync(Company company, CancellationToken cancellationToken) =>
        await dbContext.Companies.AddAsync(company, cancellationToken);

    public void RemoveCompany(Company company) => dbContext.Companies.Remove(company);

    public async Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.CompanyUnits.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<CompanyUnit?> GetCompanyUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken) =>
        dbContext.CompanyUnits.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UnitId == unitId, cancellationToken);

    public async Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken cancellationToken) =>
        await dbContext.CompanyUnits.AddAsync(link, cancellationToken);

    public void RemoveCompanyUnit(CompanyUnit link) => dbContext.CompanyUnits.Remove(link);

    public async Task<IReadOnlyList<Position>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Positions.Where(x => x.TenantId == tenantId).OrderBy(x => x.Title).ToListAsync(cancellationToken);

    public Task<Position?> GetPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> PositionTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Positions.AnyAsync(x => x.TenantId == tenantId && x.Title == title && x.Id != excludeId, cancellationToken);

    public async Task AddPositionAsync(Position position, CancellationToken cancellationToken) =>
        await dbContext.Positions.AddAsync(position, cancellationToken);

    public void RemovePosition(Position position) => dbContext.Positions.Remove(position);

    public async Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UserAssignments.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<UserAssignment?> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserAssignments.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);

    public async Task AddAssignmentAsync(UserAssignment assignment, CancellationToken cancellationToken) =>
        await dbContext.UserAssignments.AddAsync(assignment, cancellationToken);

    public void RemoveAssignment(UserAssignment assignment) => dbContext.UserAssignments.Remove(assignment);

    public async Task<IReadOnlyList<UnitPosition>> ListUnitPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UnitPositions.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);

    public async Task AddUnitPositionAsync(UnitPosition unitPosition, CancellationToken cancellationToken) =>
        await dbContext.UnitPositions.AddAsync(unitPosition, cancellationToken);

    public void RemoveUnitPosition(UnitPosition unitPosition) => dbContext.UnitPositions.Remove(unitPosition);

    public async Task<IReadOnlyList<UserPosition>> ListUserPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UserPositions.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task AddUserPositionAsync(UserPosition userPosition, CancellationToken cancellationToken) =>
        await dbContext.UserPositions.AddAsync(userPosition, cancellationToken);

    public void RemoveUserPosition(UserPosition userPosition) => dbContext.UserPositions.Remove(userPosition);

    public async Task<IReadOnlyList<Honorific>> ListHonorificsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Honorifics.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync(cancellationToken);

    public Task<Honorific?> GetHonorificAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Honorifics.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> HonorificTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Honorifics.AnyAsync(x => x.TenantId == tenantId && x.Title == title && x.Id != excludeId, cancellationToken);

    public async Task AddHonorificAsync(Honorific honorific, CancellationToken cancellationToken) =>
        await dbContext.Honorifics.AddAsync(honorific, cancellationToken);

    public void RemoveHonorific(Honorific honorific) => dbContext.Honorifics.Remove(honorific);

    public async Task<IReadOnlyList<UserHonorific>> ListUserHonorificsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UserHonorifics.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<UserHonorific?> GetUserHonorificAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserHonorifics.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);

    public async Task AddUserHonorificAsync(UserHonorific userHonorific, CancellationToken cancellationToken) =>
        await dbContext.UserHonorifics.AddAsync(userHonorific, cancellationToken);

    public void RemoveUserHonorific(UserHonorific userHonorific) => dbContext.UserHonorifics.Remove(userHonorific);
}
