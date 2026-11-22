using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using NexusCore.Infrastructure.Persistence;

namespace TMPB_API;

/// <summary>
/// The Organization and OrgStructure modules keep their schema with EF Core migrations, but their
/// first migration is a baseline over tables that used to be created at start-up: a new database
/// cannot be built from the migrations alone. So a database without the module's tables gets them
/// from the current model, with every migration recorded as applied; a database that has them gets
/// only its pending migrations. Never creates a table or column that already exists.
/// </summary>
internal static class OrgStructureSchema
{
    public static async Task EnsureAsync(DbContext db, string probeTable, CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, probeTable, cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        await ModuleSchemaInitializer.EnsureCreatedAsync(db, cancellationToken);

        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet();
        foreach (var migration in db.Database.GetMigrations().Where(id => !applied.Contains(id)))
        {
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())), cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(DbContext db, string table, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN OBJECT_ID(@table, N'U') IS NULL THEN 0 ELSE 1 END";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
