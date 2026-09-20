namespace NexusCore.Application.Identity.Queries;

public sealed record ListUsersQuery(Guid? TenantId, int PageNumber = 1, int PageSize = 20, string? Search = null);
public sealed record ListRolesQuery(Guid? TenantId);
public sealed record ListTenantsQuery();
public sealed record ListPermissionsQuery();
