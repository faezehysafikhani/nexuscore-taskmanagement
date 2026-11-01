using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Identity.Sessions;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// Every minute: marks the sessions whose idle or absolute limit passed as ended (so the audit
/// log says so and the lists are right), and removes old data: closed sessions after 90 days,
/// refresh tokens of ended sessions after 7 days, used or lapsed sign-in tickets. The checks on
/// requests never wait for this; it only keeps the records tidy.
/// </summary>
public sealed class SessionMaintenanceService(IServiceScopeFactory scopes, ILogger<SessionMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ISessionService>().ExpireDueSessionsAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The database may be down for a moment; the next tick tries again.
                    logger.LogWarning(exception, "Session maintenance failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
