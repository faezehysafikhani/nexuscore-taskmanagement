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

    /// <summary>The last sign-in (time and address) of each user of the caller's organization, for the users report.</summary>
    Task<Result<IReadOnlyList<LastSignInDto>>> ListLastSignInsAsync(Guid? tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<LastSignInDto>>([]));

    /// <summary>The same entry, tied to a session (the sign-in it belongs to) other than the caller's own.</summary>
    Task AuditForAsync(Guid? tenantId, Guid? userId, string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken, Guid? sessionId) =>
        AuditForAsync(tenantId, userId, action, entityName, entityId, details, cancellationToken);

    Task<Result<PagedResult<AuditLogDto>>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SettingDto>>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
    Task<Result<SettingDto>> UpsertSettingAsync(UpsertSettingRequest request, CancellationToken cancellationToken);
}
