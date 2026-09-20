using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall.Domain;

public sealed record ActivityProgressUpdated(Guid ActivityId, Guid TenantId, Guid ProjectId, decimal PlannedProgress, decimal ActualProgress) : DomainEvent;
