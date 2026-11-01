using NexusCore.Application.Platform.Dtos;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Interfaces;

public interface IPlatformRepository
{
    Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken);
    Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken);
    /// <summary>The newest sign-in of every user of the tenant that has one in the audit log.</summary>
    Task<IReadOnlyList<LastSignInDto>> ListLastSignInsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LastSignInDto>>([]);
    Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<SystemSetting?> FindSettingAsync(Guid? tenantId, string key, string scope, CancellationToken cancellationToken);
    Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken);
}
