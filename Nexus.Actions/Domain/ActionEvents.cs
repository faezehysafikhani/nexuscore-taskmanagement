using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions.Domain;

public sealed record ActionCreated(Guid ActionId, Guid TenantId, string Title, Guid? ProjectId) : DomainEvent;
