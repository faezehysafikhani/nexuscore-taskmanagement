using MediatR;
using NexusCore.SharedKernel.Results;

namespace Notifications.Application.Commands.MarkAllAsRead;

public sealed record MarkAllAsReadCommand()
    : IRequest<Result>;