namespace NexusCore.Application.Identity.Dtos;

public sealed record TenantDto(Guid Id, string Name, string Slug, string? Description, bool IsActive);
public sealed record CreateTenantRequest(string Name, string Slug, string? Description);
