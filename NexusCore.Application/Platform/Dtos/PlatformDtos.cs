namespace NexusCore.Application.Platform.Dtos;

public sealed record AuditLogDto(Guid Id, Guid? TenantId, Guid? UserId, string Action, string? EntityName, string? EntityId, string? Details, string? IpAddress, DateTimeOffset OccurredAtUtc);
public sealed record SettingDto(Guid Id, Guid? TenantId, string Key, string Value, string Scope);
public sealed record UpsertSettingRequest(Guid? TenantId, string Key, string Value, string Scope = "System");
