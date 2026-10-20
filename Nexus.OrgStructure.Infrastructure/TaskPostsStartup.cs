using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.OrgStructure.Application;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>
/// With tasks given to posts: on start-up, gives tasks that have no posts yet to their people's
/// posts and brings every task up to date with its posts' holders. Does nothing when it is off;
/// a failure is logged and never stops the host.
/// </summary>
public sealed class TaskPostsStartup(IServiceScopeFactory scopes, ILogger<TaskPostsStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var directory = scope.ServiceProvider.GetRequiredService<TaskPostDirectory>();
            if (!directory.Enabled)
            {
                return;
            }

            foreach (var tenantId in await scope.ServiceProvider.GetRequiredService<IOrgStructureRepository>().ListTenantIdsAsync(cancellationToken))
            {
                await directory.ConvertTasksAsync(tenantId, cancellationToken);
                await directory.SyncHoldersAsync(tenantId, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bringing tasks up to date with their posts failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
