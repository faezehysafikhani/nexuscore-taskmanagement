using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// Columns added to Notifications after the table was first created. Hosts create the module's
/// tables with EnsureCreated, which never adds a column to an existing table, so the module adds
/// its own on start-up - idempotent, a no-op once they are there. The migration that adds them
/// runs the same statements, so a database kept by migrations and one kept this way agree.
/// </summary>
public static class NotificationSchema
{
    public const string AddContextColumnsSql = """
        IF OBJECT_ID(N'[dbo].[Notifications]') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.Notifications', N'Category') IS NULL ALTER TABLE [dbo].[Notifications] ADD [Category] nvarchar(64) NULL;
            IF COL_LENGTH(N'dbo.Notifications', N'ReferenceId') IS NULL ALTER TABLE [dbo].[Notifications] ADD [ReferenceId] uniqueidentifier NULL;
            IF COL_LENGTH(N'dbo.Notifications', N'ActorUserId') IS NULL ALTER TABLE [dbo].[Notifications] ADD [ActorUserId] uniqueidentifier NULL;
            IF COL_LENGTH(N'dbo.Notifications', N'ActorName') IS NULL ALTER TABLE [dbo].[Notifications] ADD [ActorName] nvarchar(200) NULL;
        END
        """;
}

/// <summary>Applies <see cref="NotificationSchema"/> when the host starts (SQL Server only).</summary>
public sealed class NotificationSchemaPatcher(IServiceScopeFactory scopes, ILogger<NotificationSchemaPatcher> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlRawAsync(NotificationSchema.AddContextColumnsSql, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not add the notification context columns; apply the Notifications migrations.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
