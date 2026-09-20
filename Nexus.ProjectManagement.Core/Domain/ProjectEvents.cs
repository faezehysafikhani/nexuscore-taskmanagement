using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Core.Domain;

public sealed record ProjectCreated(Guid ProjectId, Guid TenantId, string Name, ProjectType Type) : DomainEvent;

public sealed record ProjectSubmittedForApproval(Guid ProjectId, Guid TenantId) : DomainEvent;
