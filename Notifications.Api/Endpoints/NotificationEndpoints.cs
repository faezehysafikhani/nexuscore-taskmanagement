using MediatR;
using Microsoft.AspNetCore.Hosting.Server;
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
        var group = app.MapGroup("/api/notifications")
            .RequireAuthorization()
            .WithTags("Notifications");

        group.MapGet("/", async (
            int? pageNumber,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(
                new GetMyNotificationsQuery(pageNumber ?? 1, pageSize ?? 20), ct));
        });

        group.MapGet("/unread-count", async (
            ISender sender,
            CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(
                new GetUnreadCountQuery(), ct));
        });

        group.MapPut("/{id:guid}/read", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(
                new MarkAsReadCommand(id), ct));
        });

        group.MapPut("/read-all", async (
            ISender sender,
            CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(
                new MarkAllAsReadCommand(), ct));
        });

        return app;
    }
}
