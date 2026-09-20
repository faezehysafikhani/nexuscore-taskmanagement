using MediatR;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Common.Dtos;

namespace Notifications.Application.Queries.GetMyNotifications;

public sealed record GetMyNotificationsQuery(
    int PageNumber = 1,
    int PageSize = 20)
    : IRequest<Result<List<NotificationDto>>>;