using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class PlatformRepository(NexusCoreDbContext dbContext) : IPlatformRepository
{
    public async Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
        await dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

    public async Task<PagedResult<AuditLog>> ListAuditLogsAsync(Guid? tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.AuditLogs.AsNoTracking().AsQueryable();
        if (tenantId.HasValue)
        {
            query = query.Where(log => log.TenantId == tenantId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.OccurredAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, pageNumber, pageSize, total);
    }

    public async Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken) =>
        await dbContext.Settings.AsNoTracking()
            .Where(setting => setting.TenantId == tenantId || setting.TenantId == null)
            .OrderBy(setting => setting.Scope)
            .ThenBy(setting => setting.Key)
            .ToListAsync(cancellationToken);

    public Task<SystemSetting?> FindSettingAsync(Guid? tenantId, string key, string scope, CancellationToken cancellationToken) =>
        dbContext.Settings.SingleOrDefaultAsync(setting => setting.TenantId == tenantId && setting.Key == key && setting.Scope == scope, cancellationToken);

    public async Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken) =>
        await dbContext.Settings.AddAsync(setting, cancellationToken);
}
