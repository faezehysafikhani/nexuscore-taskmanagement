using MediatR;
using NexusCore.SharedKernel.Results;

namespace Notifications.Application.Commands.MarkAsRead;

public sealed record MarkAsReadCommand(Guid NotificationId)
    : IRequest<Result>;