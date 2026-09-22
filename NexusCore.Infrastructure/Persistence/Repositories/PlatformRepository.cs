using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class PlatformRepository(NexusCoreDbContext dbContext) : IPlatformRepository
{
    public async Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
        await dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

    public async Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogQuery filter, CancellationToken cancellationToken)
    {
        var query = dbContext.AuditLogs.AsNoTracking().AsQueryable();
        if (filter.TenantId is { } tenantId)
        {
            // A sign-in attempt with an unknown name belongs to no tenant; sign-in history still
            // has to show it.
            var includeAnonymous = filter.ActionPrefix is not null && filter.ActionPrefix.StartsWith("identity.login", StringComparison.Ordinal);
            query = includeAnonymous
                ? query.Where(log => log.TenantId == tenantId || log.TenantId == null)
                : query.Where(log => log.TenantId == tenantId);
        }

        if (filter.ActionPrefix is { } prefix)
        {
            query = query.Where(log => log.Action.StartsWith(prefix));
        }

        if (filter.Search is { } term)
        {
            // Users whose name or username matches, as actor or as the subject of the entry.
            var matchingUserIds = await dbContext.Users
                .Where(user => user.DisplayName.Contains(term) || (user.Username != null && user.Username.Contains(term)) || (user.PhoneNumber != null && user.PhoneNumber.Contains(term)))
                .Select(user => user.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            var matchingIdTexts = matchingUserIds.Select(id => id.ToString()).ToList();

            query = query.Where(log =>
                log.Action.Contains(term)
                || (log.Details != null && log.Details.Contains(term))
                || (log.IpAddress != null && log.IpAddress.Contains(term))
                || (log.UserId != null && matchingUserIds.Contains(log.UserId.Value))
                || (log.EntityName == "User" && log.EntityId != null && matchingIdTexts.Contains(log.EntityId)));
        }

        var total = await query.CountAsync(cancellationToken);
        var ordered = filter.SortDescending ? query.OrderByDescending(log => log.OccurredAtUtc) : query.OrderBy(log => log.OccurredAtUtc);
        var items = await ordered
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        // Name the person each entry concerns: the actor, else the user the entry is about.
        Guid? SubjectOf(AuditLog log) =>
            log.UserId ?? (log.EntityName == "User" && Guid.TryParse(log.EntityId, out var id) ? id : null);
        var subjectIds = items.Select(SubjectOf).OfType<Guid>().Distinct().ToList();
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => subjectIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName, user.Username })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        var rows = items.Select(log =>
        {
            var subject = SubjectOf(log) is { } id && users.TryGetValue(id, out var user) ? user : null;
            return new AuditLogDto(log.Id, log.TenantId, log.UserId, log.Action, log.EntityName, log.EntityId, log.Details,
                log.IpAddress, log.OccurredAtUtc, subject?.DisplayName, subject?.Username);
        }).ToList();

        return new PagedResult<AuditLogDto>(rows, filter.PageNumber, filter.PageSize, total);
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
