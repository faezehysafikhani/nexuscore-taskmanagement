namespace NexusCore.Application.Platform.Dtos;

/// <summary>
/// One audit entry. UserDisplayName/Username name the person it concerns: the actor, or - for an
/// anonymous request such as a sign-in - the user the entry is about.
/// </summary>
public sealed record AuditLogDto(Guid Id, Guid? TenantId, Guid? UserId, string Action, string? EntityName, string? EntityId, string? Details, string? IpAddress, DateTimeOffset OccurredAtUtc, string? UserDisplayName = null, string? Username = null);

/// <summary>
/// Filters for the audit log. Search covers action, details, IP address and the user's name or
/// username; ActionPrefix narrows to one family (e.g. "identity.login" for sign-in history).
/// </summary>
public sealed record AuditLogQuery(
    Guid? TenantId,
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    string? ActionPrefix = null,
    bool SortDescending = true);
public sealed record SettingDto(Guid Id, Guid? TenantId, string Key, string Value, string Scope);
public sealed record UpsertSettingRequest(Guid? TenantId, string Key, string Value, string Scope = "System");

/// <summary>What every upload screen in the app enforces: the current admin-configured max file
/// size and the fixed list of accepted file extensions.</summary>
public sealed record UploadPolicyDto(int MaxFileSizeKb, IReadOnlyList<string> AllowedExtensions);
