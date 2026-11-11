using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whether an organization may add one more user (its plan's user limit, an expired membership). Registered by whatever
/// module knows the plan; with none registered nobody is limited. Identity asks, and never knows what a plan is.
/// </summary>
public interface IUserQuota
{
    /// <summary>Null when one more user may be added; otherwise why not.</summary>
    Task<Error?> CheckCanAddUserAsync(Guid tenantId, CancellationToken cancellationToken);
}
