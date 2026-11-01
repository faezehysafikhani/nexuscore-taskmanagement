namespace NexusCore.SharedKernel.Interfaces;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? IpAddress { get; }

    /// <summary>The session (<c>sid</c> claim) of the access token in use; null when there is none.</summary>
    Guid? SessionId => null;

    /// <summary>The caller's User-Agent header (as sent; not trusted for anything but display).</summary>
    string? UserAgent => null;

    /// <summary>
    /// Whether the signed-in user holds a permission. The answer reflects the user's current
    /// grants (re-read on every request), not the ones copied into the token at sign-in.
    /// </summary>
    bool HasPermission(string permission) => false;
}
