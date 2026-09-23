using Microsoft.Extensions.Logging;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// What a due recurring task needs to say. Built from the task, never stored.
/// </summary>
public sealed record TaskDueNotification(
    Guid TenantId,
    Guid TaskId,
    string Title,
    string? Description,
    DateOnly DueDate,
    IReadOnlyList<Guid> RecipientUserIds,
    string? DueAtText = null);

/// <summary>
/// How this module asks for a notification to be raised.
///
/// Deliberately the module's own contract rather than a reference to Notifications: coupling
/// two business modules directly is what the architecture tests forbid. The adapter that
/// fulfils it lives in Nexus.Integrations.TaskNotifications, which is allowed to know both
/// sides - the same shape Nexus.Integrations.ProjectWorkflow already uses.
///
/// With that integration absent, the no-op below is registered and TaskManagement still runs.
/// </summary>
public interface ITaskNotificationPublisher
{
    Task PublishAsync(TaskDueNotification notification, CancellationToken cancellationToken);
}

/// <summary>Registered when no notification integration is installed.</summary>
public sealed class NullTaskNotificationPublisher(ILogger<NullTaskNotificationPublisher> logger)
    : ITaskNotificationPublisher
{
    public Task PublishAsync(TaskDueNotification notification, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "No notification integration is installed; skipping notification for task {TaskId}.",
            notification.TaskId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Looks up how to reach a user.
///
/// SMS needs a phone number and the shared identity model has no column for one, so the
/// shipped implementation returns nothing. See the docs for the exact Core change that would
/// be needed; this module must not keep its own copy of contact details.
/// </summary>
public interface IUserContactResolver
{
    Task<IReadOnlyDictionary<Guid, string>> GetPhoneNumbersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>
/// Returns no phone numbers, because NexusCore's User entity has none to return.
///
/// This is an accurate reflection of the platform today, not an unfinished stub: adding a
/// phone column to identity.Users changes a table every module shares, and the runtime creates
/// schemas with EnsureCreated, which cannot add a column to a database that already exists.
/// That is a deliberate Core migration and a deployment decision, so it is reported rather
/// than taken unilaterally. Once the column exists, replace this with a resolver that reads it.
/// </summary>
public sealed class UnavailableUserContactResolver : IUserContactResolver
{
    public Task<IReadOnlyDictionary<Guid, string>> GetPhoneNumbersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
