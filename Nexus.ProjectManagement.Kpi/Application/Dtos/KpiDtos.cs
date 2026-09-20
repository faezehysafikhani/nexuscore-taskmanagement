using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Application.Dtos;

public sealed record KpiDefinitionDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid DeliverableId, KpiType Type,
    string Description, string? Formula, decimal? TargetValue);

public sealed record CreateKpiDefinitionRequest(
    Guid TenantId, Guid ProjectId, Guid DeliverableId, KpiType Type,
    string Description, string? Formula, decimal? TargetValue);

public sealed record UpdateKpiDefinitionRequest(string Description, string? Formula, decimal? TargetValue);
