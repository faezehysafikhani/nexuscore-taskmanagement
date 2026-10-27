using NexusCore.Application.Identity.Permissions;
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
            // Only the caller's own organization, unless they may see every organization.
            TenantId = currentUserContext.ResolveTenant(query.TenantId),
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            ActionPrefix = string.IsNullOrWhiteSpace(query.ActionPrefix) ? null : query.ActionPrefix.Trim(),
        };
        var page = await repository.ListAuditLogsAsync(safe, cancellationToken);

        // Failed sign-ins by an unknown name belong to no organization, so every organization's
        // administrator is shown them. What was typed in that field is sometimes a password, and
        // is not theirs to read: those entries keep the time, the address and the action only.
        if (currentUserContext.UserId is not null && !currentUserContext.HasPermission(IdentityPermissions.TenantsManageAll)
            && page.Items.Any(entry => entry.TenantId is null))
        {
            page = page with
            {
                Items = page.Items.Select(entry => entry.TenantId is null ? entry with { Details = null } : entry).ToList()
            };
        }

        return Result.Success(page);
    }

    // Settings that have a screen and a permission of their own (SMS channels and quiet hours,
    // LDAP, reminders) hold secrets or drive outgoing traffic. They are changed and read only
    // through those endpoints; the generic settings API must not be a way around them. The
    // branding (BrandingService) points at stored files and is checked there.
    private static readonly string[] ReservedKeyPrefixes = ["Notifications.", "Identity.", "TaskManagement.", BrandingService.KeyPrefix];

    private static bool IsReserved(string key) =>
        ReservedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public async Task<Result<IReadOnlyList<SettingDto>>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var settings = await repository.ListSettingsAsync(currentUserContext.ResolveTenant(tenantId), cancellationToken);
        return Result.Success<IReadOnlyList<SettingDto>>(settings.Where(setting => !IsReserved(setting.Key)).Select(ToSettingDto).ToList());
    }

    public async Task<Result<SettingDto>> UpsertSettingAsync(UpsertSettingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Result.Failure<SettingDto>(Error.Validation("Setting key is required."));
        }

        if (IsReserved(request.Key.Trim()))
        {
            return Result.Failure<SettingDto>(Error.Forbidden("This setting is managed on its own screen and cannot be changed here."));
        }

        // A system-wide setting (no tenant) or another organization's needs tenants.manage_all.
        if (!currentUserContext.CanAccessTenant(request.TenantId))
        {
            return Result.Failure<SettingDto>(Error.Forbidden("You can only change the settings of your own organization."));
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
