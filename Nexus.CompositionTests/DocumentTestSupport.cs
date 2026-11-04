using NexusCore.Application.Files;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>A signed-in user of one organization, for services that read the organization from the caller.</summary>
internal sealed class TenantUser(Guid tenantId) : ICurrentUserContext
{
    public Guid? UserId { get; } = Guid.NewGuid();
    public Guid? TenantId { get; } = tenantId;
    public string? Email => null;
    public string? IpAddress => null;
}

internal sealed class UnlimitedUploads : IUploadPolicyReader
{
    public Task<int> GetMaxFileSizeKbAsync(Guid? tenantId, CancellationToken cancellationToken) => Task.FromResult(int.MaxValue);
}
