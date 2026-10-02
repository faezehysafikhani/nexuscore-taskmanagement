using MediatR;
using NexusCore.Application.Common;
using Microsoft.AspNetCore.Hosting.Server;
using NexusCore.Application.Security.RateLimiting;
using Notifications.Application.Commands.MarkAllAsRead;
using Notifications.Application.Commands.MarkAsRead;
using Notifications.Application.Queries.GetMyNotifications;
using Notifications.Application.Queries.GetUnreadCount;

namespace Notifications.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(
        this IEndpointRouteBuilder app)
    {
        // The caller's own notifications, in the platform's usual shapes: the list or count on
        // success, ProblemDetails with an error code otherwise (EndpointResults).
        var group = app.MapGroup("/api/notifications")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi)
            .WithTags("Notifications");

        group.MapGet("/", async (
            int? pageNumber,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new GetMyNotificationsQuery(pageNumber ?? 1, pageSize ?? 20), ct)).ToApiResult();
        });

        group.MapGet("/unread-count", async (
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new GetUnreadCountQuery(), ct)).ToApiResult();
        });

        group.MapPut("/{id:guid}/read", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new MarkAsReadCommand(id), ct)).ToApiResult();
        })
            .RequireRateLimiting(NexusRateLimitPolicies.Write);

        group.MapPut("/read-all", async (
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new MarkAllAsReadCommand(), ct)).ToApiResult();
        })
            .RequireRateLimiting(NexusRateLimitPolicies.Write);

        return app;
    }
}
