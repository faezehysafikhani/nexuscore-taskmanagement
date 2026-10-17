using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Identity.Interfaces;
using Notifications.Application.Abstractions;

namespace Nexus.Integrations.TaskNotifications;

/// <summary>
/// Tells each person added to a work team (or user group) that they were added, as an in-app
/// notification. Replaces the Core's do-nothing default.
/// </summary>
public sealed class TeamMembershipNotifier(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamMembershipNotifier> logger) : IUserGroupMembershipNotifier
{
    public const string Title = "عضویت در تیم کاری";
    public const string Category = "team.member_added";

    public async Task MembersAddedAsync(UserGroupMembersAdded change, CancellationToken cancellationToken)
    {
        var message = MessageFor(change);
        foreach (var userId in change.UserIds)
        {
            // A fresh scope per person: a failed insert stays in its DbContext and would
            // otherwise be saved again with the next one. One failure costs no one else theirs.
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                    new NotificationRequest(userId, Title, message, "Info", change.TenantId, Category, change.GroupId),
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Team membership notification for user {UserId}, group {GroupId} was not stored.", userId, change.GroupId);
            }
        }
    }

    public static string MessageFor(UserGroupMembersAdded change)
    {
        var kind = change.IsPersonalTeam ? "تیم کاری" : "گروه کاربری";
        return string.IsNullOrWhiteSpace(change.AddedByName)
            ? $"شما به {kind} «{change.GroupName}» اضافه شدید."
            : $"«{change.AddedByName}» شما را به {kind} «{change.GroupName}» اضافه کرد.";
    }
}
