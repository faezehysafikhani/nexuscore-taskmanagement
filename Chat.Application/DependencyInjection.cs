using Chat.Application.Conversations.Commands.CreateDirectConversation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddChatApplication(this IServiceCollection services)
    {
        services.AddMediatR(typeof(CreateDirectConversationCommandHandler).Assembly);

        return services;
    }
}