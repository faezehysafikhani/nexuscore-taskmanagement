using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NexusCore.Application.Security;

/// <summary>
/// Adds the organization condition to every entity of a DbContext that has a TenantId: rows of
/// another organization are not returned, whatever the query says. A DbContext calls this at the
/// end of OnModelCreating with a lambda that reads its own tenant property, e.g.
/// <c>modelBuilder.ApplyTenantFilters(() =&gt; TenantFilterId)</c>; EF reads the value per context
/// instance, so one cached model serves every organization.
///
/// An entity that already has a query filter keeps it - it must include the tenant condition
/// itself - and an entity that is shared on purpose is listed in <c>except</c>.
/// </summary>
public static class TenantQueryFilters
{
    /// <param name="filterTenantId">Reads the context's tenant; null means "do not filter".</param>
    /// <param name="except">Entities shared across organizations on purpose (they must be named).</param>
    /// <param name="alsoNullTenant">Entities whose rows without a tenant are system-wide and stay visible.</param>
    public static void ApplyTenantFilters(
        this ModelBuilder modelBuilder,
        Expression<Func<Guid?>> filterTenantId,
        IReadOnlyCollection<Type>? except = null,
        IReadOnlyCollection<Type>? alsoNullTenant = null)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;
            if (entityType.BaseType is not null || entityType.IsOwned() || entityType.FindPrimaryKey() is null
                || entityType.GetQueryFilter() is not null || except?.Contains(clrType) == true)
            {
                continue;
            }

            var tenantProperty = clrType.GetProperty("TenantId");
            if (tenantProperty is null || (tenantProperty.PropertyType != typeof(Guid) && tenantProperty.PropertyType != typeof(Guid?)))
            {
                continue;
            }

            var entity = Expression.Parameter(clrType, "entity");
            var tenant = Expression.Convert(Expression.Property(entity, tenantProperty), typeof(Guid?));
            var nothing = Expression.Constant(null, typeof(Guid?));
            var wanted = filterTenantId.Body;

            // no tenant to filter by  ||  the row is the organization's own  [ ||  the row is system-wide ]
            Expression body = Expression.OrElse(
                Expression.Equal(wanted, nothing),
                Expression.Equal(tenant, wanted));
            if (alsoNullTenant?.Contains(clrType) == true)
            {
                body = Expression.OrElse(body, Expression.Equal(tenant, nothing));
            }

            // Metadata only: unlike modelBuilder.Entity(type) it cannot pull ignored navigations back in.
            entityType.SetQueryFilter(Expression.Lambda(body, entity));
        }
    }

    /// <summary>The entities of a model that carry a TenantId and have no tenant filter - for the test that keeps new tables from slipping through.</summary>
    public static IReadOnlyList<Type> UnfilteredTenantEntities(DbContext context) =>
        context.Model.GetEntityTypes()
            .Where(type => type.BaseType is null && !type.IsOwned() && type.FindPrimaryKey() is not null
                           && type.ClrType.GetProperty("TenantId") is { } property
                           && (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                           && type.GetQueryFilter() is null)
            .Select(type => type.ClrType)
            .ToList();
}
