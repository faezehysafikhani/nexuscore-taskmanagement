namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Told when an account is disabled, after it is saved. Optional modules release what the person
/// held there (an organization structure: their posts, so their tasks go to the vacant posts).
/// None by default.
/// </summary>
public interface IUserDeactivationListener
{
    Task UserDeactivatedAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
