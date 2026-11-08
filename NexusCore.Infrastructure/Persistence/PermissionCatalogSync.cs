using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Persistence;

/// <summary>
/// At start-up, brings the permissions table in step with the installed modules' catalogues and gives
/// the built-in Administrator role every permission - the same thing DefaultDataSeeder does, but
/// always, also where Database:SeedOnStartup is off. Only adds: nothing is removed or changed except
/// the Administrator's grants, which the seeder resets to "everything" anyway. It never creates the
/// tenant, the role or the account, and does nothing when the tables are not there yet.
/// A failure is logged and never stops the host.
/// </summary>
public sealed class PermissionCatalogSync(IServiceScopeFactory scopes, ILogger<PermissionCatalogSync> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Syncing the permission catalogue with the database failed; the Administrator role keeps what it had.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var catalog = scope.ServiceProvider.GetServices<IPermissionCatalog>()
            .SelectMany(c => c.GetPermissions())
            .DistinctBy(p => p.Name)
            .ToList();

        // Without the built-in role there is nothing to give them to (a fresh database is seeded or scripted first).
        var adminRole = await dbContext.Roles.IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(role => role.Id == DefaultDataSeeder.AdminRoleId, cancellationToken);
        if (adminRole is null)
        {
            return;
        }

        var known = await dbContext.Permissions.Select(p => p.Name).ToListAsync(cancellationToken);
        var knownNames = known.ToHashSet(StringComparer.Ordinal);
        var missing = catalog.Where(p => !knownNames.Contains(p.Name)).ToList();
        foreach (var permission in missing)
        {
            dbContext.Permissions.Add(new Permission(DefaultDataSeeder.CreateStableGuid(permission.Name), permission.Name, permission.Module, permission.Description));
        }

        if (missing.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var allIds = await dbContext.Permissions.Select(p => p.Id).ToListAsync(cancellationToken);
        var held = adminRole.Permissions.Select(p => p.PermissionId).ToHashSet();
        if (allIds.Any(id => !held.Contains(id)))
        {
            adminRole.SetPermissions(allIds);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Gave the Administrator role {Added} permission(s) it did not have yet.", allIds.Count(id => !held.Contains(id)));
        }
    }
}
