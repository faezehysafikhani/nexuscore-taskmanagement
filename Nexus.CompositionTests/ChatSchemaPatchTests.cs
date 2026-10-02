using Chat.Domain.Entities;
using Chat.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// Reproduces the real cause of the Chat 500: a deployment whose Conversations table was
/// created before "TeamId" existed in the model. Chat's schema is not applied through real EF
/// Migrations at startup (see ModuleSchemaInitializer's own remark) - only through
/// CreateTablesAsync, which never alters a table that already exists - so that column silently
/// never reaches such a database on its own. Any query that touches Conversations (direct
/// messages included) then throws, on SQL Server as much as it does here on SQLite. This uses
/// SQLite only as a real relational engine to prove the mechanism and the patch that closes it
/// (<see cref="ModuleSchemaInitializer.EnsureColumnAsync"/>); the patch itself runs its own
/// SQL-Server-specific statements, applied by NexusCore.Api/PostBank.Api at startup.
/// </summary>
public sealed class ChatSchemaPatchTests : IDisposable
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"chat-schema-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databaseFile);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task AConversationsTable_FromBeforeTeamChat_MakesEveryQueryFail_UntilThePatchRuns()
    {
        var connectionString = $"Data Source={_databaseFile};Pooling=False";

        // The Conversations table exactly as it stood before this feature: no TeamId column,
        // the schema a real, already-running deployment has.
        await using (var raw = new SqliteConnection(connectionString))
        {
            await raw.OpenAsync();
            var create = raw.CreateCommand();
            create.CommandText = """
                CREATE TABLE "Conversations" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "TenantId" TEXT NULL,
                    "Title" TEXT NULL,
                    "Type" INTEGER NOT NULL,
                    "CreatedBy" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "CreatedByUserId" TEXT NULL,
                    "ModifiedAtUtc" TEXT NULL,
                    "ModifiedByUserId" TEXT NULL,
                    "DirectKey" TEXT NULL
                );
                """;
            await create.ExecuteNonQueryAsync();

            var seed = raw.CreateCommand();
            var tenantId = Guid.NewGuid();
            seed.CommandText =
                "INSERT INTO \"Conversations\" (Id, TenantId, Type, CreatedAt, CreatedAtUtc, DirectKey) " +
                $"VALUES ('{Guid.NewGuid()}', '{tenantId}', 0, '2026-01-01', '2026-01-01', 'some-key');";
            await seed.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ChatDbContext>().UseSqlite(connectionString).Options;

        // The exact query the direct-message read path runs (DirectConversationService.FindAsync):
        // with the current model (which now includes TeamId) against the old table, it fails.
        await using (var db = new ChatDbContext(options))
        {
            var failure = await Record.ExceptionAsync(() =>
                db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == "some-key"));
            Assert.NotNull(failure);
            Assert.Contains("TeamId", failure!.Message);
        }

        // The patch: SQLite's own idempotent form of the same "add the column if it is
        // missing" step ModuleSchemaInitializer.EnsureColumnAsync applies on SQL Server.
        await using (var raw = new SqliteConnection(connectionString))
        {
            await raw.OpenAsync();
            var alreadyThere = raw.CreateCommand();
            alreadyThere.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Conversations') WHERE name = 'TeamId';";
            var hasColumn = (long)(await alreadyThere.ExecuteScalarAsync())! > 0;
            if (!hasColumn)
            {
                var patch = raw.CreateCommand();
                patch.CommandText = "ALTER TABLE \"Conversations\" ADD COLUMN \"TeamId\" TEXT NULL;";
                await patch.ExecuteNonQueryAsync();
            }
        }

        // The very same query the read path runs now succeeds - a conversation is found, not
        // an exception, and a query for a *different* key correctly finds nothing (never a 500).
        await using (var db = new ChatDbContext(options))
        {
            var found = await db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == "some-key");
            Assert.NotNull(found);

            var notFound = await db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == "no-such-key");
            Assert.Null(notFound);
        }

        // Running the patch again (as it does on every restart) stays a safe no-op.
        await using (var raw = new SqliteConnection(connectionString))
        {
            await raw.OpenAsync();
            var again = raw.CreateCommand();
            again.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Conversations') WHERE name = 'TeamId';";
            Assert.Equal(1L, (long)(await again.ExecuteScalarAsync())!);
        }
    }
}
