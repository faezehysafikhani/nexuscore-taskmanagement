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

    public async Task<Result<PagedResult<AuditLogDto>>> ListAuditLogsAsync(Guid? tenantId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var logs = await repository.ListAuditLogsAsync(tenantId, Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100), cancellationToken);
        return Result.Success(new PagedResult<AuditLogDto>(
            logs.Items.Select(ToAuditLogDto).ToList(),
            logs.PageNumber,
            logs.PageSize,
            logs.TotalCount));
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

    private static AuditLogDto ToAuditLogDto(AuditLog log) =>
        new(log.Id, log.TenantId, log.UserId, log.Action, log.EntityName, log.EntityId, log.Details, log.IpAddress, log.OccurredAtUtc);

    private static SettingDto ToSettingDto(SystemSetting setting) =>
        new(setting.Id, setting.TenantId, setting.Key, setting.Value, setting.Scope);
}
