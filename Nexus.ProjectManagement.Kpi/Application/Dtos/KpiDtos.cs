using Nexus.ProjectManagement.Kpi.Domain;

namespace Nexus.ProjectManagement.Kpi.Application.Dtos;

public sealed record KpiDefinitionDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid DeliverableId, KpiType Type,
    string Description, string? Formula, decimal? TargetValue);

public sealed record CreateKpiDefinitionRequest(
    Guid TenantId, Guid ProjectId, Guid DeliverableId, KpiType Type,
    string Description, string? Formula, decimal? TargetValue);

public sealed record UpdateKpiDefinitionRequest(string Description, string? Formula, decimal? TargetValue);

/// <param name="RecordedByUserId">Who entered the value (the audit field CreatedByUserId).</param>
public sealed record KpiMeasurementDto(
    Guid Id, Guid KpiDefinitionId, Guid ProjectId, DateOnly MeasuredOn, decimal Value, string? Note,
    Guid? RecordedByUserId, DateTimeOffset RecordedAtUtc);

public sealed record RecordKpiMeasurementRequest(DateOnly MeasuredOn, decimal Value, string? Note);

public sealed record UpdateKpiMeasurementRequest(DateOnly MeasuredOn, decimal Value, string? Note);
