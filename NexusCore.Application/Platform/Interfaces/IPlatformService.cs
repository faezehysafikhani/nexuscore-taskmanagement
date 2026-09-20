using NexusCore.Application.Platform.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Interfaces;

public interface IPlatformService
{
    Task AuditAsync(string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken);
    Task<Result<PagedResult<AuditLogDto>>> ListAuditLogsAsync(Guid? tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SettingDto>>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<SettingDto>> UpsertSettingAsync(UpsertSettingRequest request, CancellationToken cancellationToken);
}
