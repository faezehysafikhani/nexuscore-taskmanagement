using Chat.Application.Abstractions;
using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ChatDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Chat")));

        services.AddScoped<IChatDbContext>(
            provider => provider.GetRequiredService<ChatDbContext>());

        return services;
    }
}