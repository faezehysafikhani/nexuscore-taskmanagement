namespace Nexus.StrategyManagement.Application.Dtos;

public sealed record StrategyDto(Guid Id, Guid TenantId, string Name, string? Description, decimal Weight, Guid? ParentStrategyId);

public sealed record CreateStrategyRequest(Guid TenantId, string Name, string? Description, decimal Weight, Guid? ParentStrategyId);

public sealed record UpdateStrategyRequest(string Name, string? Description, decimal Weight, Guid? ParentStrategyId);
