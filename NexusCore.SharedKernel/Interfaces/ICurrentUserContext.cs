namespace NexusCore.SharedKernel.Interfaces;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? IpAddress { get; }
}
