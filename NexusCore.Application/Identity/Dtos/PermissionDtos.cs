namespace NexusCore.Application.Identity.Dtos;

public sealed record PermissionDto(Guid Id, string Name, string Module, string Description);
public sealed record PermissionGroupDto(string Module, IReadOnlyList<PermissionDto> Permissions);
