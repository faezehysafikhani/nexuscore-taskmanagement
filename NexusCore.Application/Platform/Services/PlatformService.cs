using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Platform.Services;

public sealed class PlatformService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUserContext) : IPlatformService
{
    public async Task AuditAsync(string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            Guid.NewGuid(),
            currentUserContext.TenantId,
            currentUserContext.UserId,
            action,
            entityName,
            entityId,
            details,
            currentUserContext.IpAddress);

        await repository.AddAuditLogAsync(auditLog, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task AuditForAsync(Guid? tenantId, Guid? userId, string action, string? entityName, string? entityId, string? details, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(Guid.NewGuid(), tenantId, userId, action, entityName, entityId, details, currentUserContext.IpAddress);
        await repository.AddAuditLogAsync(auditLog, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<PagedResult<AuditLogDto>>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        var safe = query with
        {
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            ActionPrefix = string.IsNullOrWhiteSpace(query.ActionPrefix) ? null : query.ActionPrefix.Trim(),
        };
        return Result.Success(await repository.ListAuditLogsAsync(safe, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var settings = await repository.ListSettingsAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<SettingDto>>(settings.Select(ToSettingDto).ToList());
    }

    public async Task<Result<SettingDto>> UpsertSettingAsync(UpsertSettingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Result.Failure<SettingDto>(Error.Validation("Setting key is required."));
        }

        var setting = await repository.FindSettingAsync(request.TenantId, request.Key, request.Scope, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting(Guid.NewGuid(), request.TenantId, request.Key, request.Value, request.Scope);
            await repository.AddSettingAsync(setting, cancellationToken);
        }
        else
        {
            setting.UpdateValue(request.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await AuditAsync("settings.upsert", nameof(SystemSetting), setting.Id.ToString(), setting.Key, cancellationToken);
        return Result.Success(ToSettingDto(setting));
    }

    private static SettingDto ToSettingDto(SystemSetting setting) =>
        new(setting.Id, setting.TenantId, setting.Key, setting.Value, setting.Scope);
}
