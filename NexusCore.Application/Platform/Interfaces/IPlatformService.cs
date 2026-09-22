using NexusCore.Application.Platform.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Interfaces;

public interface IPlatformService
{
    Task AuditAsync(string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken);

    /// <summary>
    /// Records an entry for a known tenant and user, for requests that are not signed in yet
    /// (sign-in itself): the request context has no user to take them from.
    /// </summary>
    Task AuditForAsync(Guid? tenantId, Guid? userId, string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken);

    Task<Result<PagedResult<AuditLogDto>>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SettingDto>>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<SettingDto>> UpsertSettingAsync(UpsertSettingRequest request, CancellationToken cancellationToken);
}
