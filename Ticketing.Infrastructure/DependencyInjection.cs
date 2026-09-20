using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTicketingInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
    {
        services.AddDbContext<TicketingDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Ticket")));

        services.AddScoped<ITicketingDbContext>(
            provider =>
                provider.GetRequiredService<TicketingDbContext>());

        return services;
    }
}