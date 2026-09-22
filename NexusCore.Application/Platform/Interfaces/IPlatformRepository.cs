using NexusCore.Application.Platform.Dtos;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Interfaces;

public interface IPlatformRepository
{
    Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken);
    Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<SystemSetting?> FindSettingAsync(Guid? tenantId, string key, string scope, CancellationToken cancellationToken);
    Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken);
}
