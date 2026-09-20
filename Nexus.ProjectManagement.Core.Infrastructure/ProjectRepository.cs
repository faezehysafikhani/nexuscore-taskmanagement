using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Infrastructure;

public sealed class ProjectRepository(ProjectManagementCoreDbContext dbContext) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Projects.SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

    public async Task<PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.Projects.Where(project => project.TenantId == request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query = query.Where(project =>
                project.Name.Contains(request.Search) || project.Code.Contains(request.Search));
        }

        if (request.Type is { } type)
        {
            query = query.Where(project => project.Type == type);
        }

        if (request.Status is { } status)
        {
            query = query.Where(project => project.Status == status);
        }

        if (request.OrganizationUnitId is { } organizationUnitId)
        {
            query = query.Where(project => project.OrganizationUnitId == organizationUnitId);
        }

        if (request.ManagerUserId is { } managerUserId)
        {
            query = query.Where(project => project.ManagerUserId == managerUserId);
        }

        var total = await query.CountAsync(cancellationToken);

        query = ApplySort(query, request.SortBy, request.SortDescending);

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Project>(items, request.PageNumber, request.PageSize, total);
    }

    public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Projects.AnyAsync(
            project => project.TenantId == tenantId && project.Code == code && project.Id != excludeId,
            cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        await dbContext.Projects.AddAsync(project, cancellationToken);
    }

    private static IQueryable<Project> ApplySort(IQueryable<Project> query, ProjectSortBy sortBy, bool descending) => sortBy switch
    {
        ProjectSortBy.Name => descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        ProjectSortBy.Code => descending ? query.OrderByDescending(p => p.Code) : query.OrderBy(p => p.Code),
        ProjectSortBy.StartDate => descending ? query.OrderByDescending(p => p.StartDate) : query.OrderBy(p => p.StartDate),
        ProjectSortBy.EndDate => descending ? query.OrderByDescending(p => p.EndDate) : query.OrderBy(p => p.EndDate),
        ProjectSortBy.Status => descending ? query.OrderByDescending(p => p.Status) : query.OrderBy(p => p.Status),
        _ => descending ? query.OrderByDescending(p => p.CreatedAtUtc) : query.OrderBy(p => p.CreatedAtUtc)
    };
}
