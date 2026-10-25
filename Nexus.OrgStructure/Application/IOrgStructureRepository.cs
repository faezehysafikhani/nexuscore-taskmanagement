using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>This module's own unit of work (see IOrganizationUnitOfWork for why each module has one).</summary>
public interface IOrgStructureUnitOfWork : IUnitOfWork;

public interface IOrgStructureRepository
{
    Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Company?> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> CompanyCodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken);
    Task AddCompanyAsync(Company company, CancellationToken cancellationToken);
    void RemoveCompany(Company company);

    Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<CompanyUnit?> GetCompanyUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken);
    Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken cancellationToken);
    void RemoveCompanyUnit(CompanyUnit link);

    Task<IReadOnlyList<Position>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Position?> GetPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> PositionTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken);
    Task AddPositionAsync(Position position, CancellationToken cancellationToken);
    void RemovePosition(Position position);

    Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UserAssignment?> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddAssignmentAsync(UserAssignment assignment, CancellationToken cancellationToken);
    void RemoveAssignment(UserAssignment assignment);

    Task<IReadOnlyList<PositionCompany>> ListPositionCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddPositionCompanyAsync(PositionCompany positionCompany, CancellationToken cancellationToken);
    void RemovePositionCompany(PositionCompany positionCompany);

    Task<IReadOnlyList<TaskPost>> ListTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid>? taskIds, IReadOnlyCollection<Guid>? postIds, CancellationToken cancellationToken);
    Task AddTaskPostAsync(TaskPost taskPost, CancellationToken cancellationToken);
    void RemoveTaskPost(TaskPost taskPost);
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PostActing>> ListPostActingsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddPostActingAsync(PostActing acting, CancellationToken cancellationToken);
    void RemovePostActing(PostActing acting);

    Task<IReadOnlyList<UnitPosition>> ListUnitPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddUnitPositionAsync(UnitPosition unitPosition, CancellationToken cancellationToken);
    void RemoveUnitPosition(UnitPosition unitPosition);

    Task<IReadOnlyList<UserPosition>> ListUserPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddUserPositionAsync(UserPosition userPosition, CancellationToken cancellationToken);
    void RemoveUserPosition(UserPosition userPosition);

    Task<IReadOnlyList<Honorific>> ListHonorificsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Honorific?> GetHonorificAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> HonorificTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken);
    Task AddHonorificAsync(Honorific honorific, CancellationToken cancellationToken);
    void RemoveHonorific(Honorific honorific);

    Task<IReadOnlyList<UserHonorific>> ListUserHonorificsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UserHonorific?> GetUserHonorificAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddUserHonorificAsync(UserHonorific userHonorific, CancellationToken cancellationToken);
    void RemoveUserHonorific(UserHonorific userHonorific);
}
