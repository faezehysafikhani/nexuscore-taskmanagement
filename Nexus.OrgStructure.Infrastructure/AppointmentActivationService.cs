using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.OrgStructure.Application;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>
/// With tasks given to posts: an appointment from a later day (or one ending on a later day) changes
/// who holds the post when that day comes. Every few minutes this moves the open tasks of the posts
/// whose appointments started or ended since the last look to their holders then, as a change made
/// at once does. Does nothing when tasks are not given to posts; a failure is logged and never stops the host.
/// </summary>
public sealed class AppointmentActivationService(IServiceScopeFactory scopes, ILogger<AppointmentActivationService> logger, TimeProvider? clock = null)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TaskPostsStartup brings every post up to date at start-up: only what happens after it counts.
        var since = Now;
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var until = Now;
            try
            {
                await RunOnceAsync(since, until, stoppingToken);
                since = until;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Moving tasks to the holders of appointments that started or ended failed; it is tried again.");
            }
        }
    }

    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    /// <summary>One look: the posts whose appointments started or ended in (since, until].</summary>
    public async Task RunOnceAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<TaskPostDirectory>();
        if (!directory.Enabled)
        {
            return;
        }

        var repository = scope.ServiceProvider.GetRequiredService<IOrgStructureRepository>();
        bool In(DateTimeOffset? at) => at is { } t && t > since && t <= until;
        foreach (var tenantId in await repository.ListTenantIdsAsync(cancellationToken))
        {
            var posts = (await repository.ListUserPositionHistoryAsync(tenantId, null, null, cancellationToken))
                .Where(a => In(a.StartDate) || In(a.EndDate))
                .Select(a => a.UnitPositionId).Distinct().ToList();
            if (posts.Count > 0)
            {
                await directory.SyncHoldersAsync(tenantId, posts, cancellationToken);
            }
        }
    }
}
