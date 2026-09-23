namespace NexusCore.SharedKernel.Interfaces;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? IpAddress { get; }

    /// <summary>
    /// Whether the signed-in user holds a permission. The answer reflects the user's current
    /// grants (re-read on every request), not the ones copied into the token at sign-in.
    /// </summary>
    bool HasPermission(string permission) => false;
}
