using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

/// <summary>
/// Brings an existing deployment's progress schema up to the current model. The module's tables
/// are created by ModuleSchemaInitializer.EnsureCreatedAsync, which only creates tables when the
/// database has none - it never adds a table that a later release introduced (DelayReasons). A
/// host that already runs EnsureCreatedAsync for this module can call this right after it on
/// every start; it is a no-op once the table exists. Hosts that manage the schema by script
/// instead apply docs/upgrade/2026-10-03-add-delay-reasons.sql, which does the same thing.
/// </summary>
public static class ProgressSchemaUpgrade
{
    // Kept identical to the DDL EF generates for DelayReasonConfiguration; a test compares the two.
    private const string EnsureDelayReasonsSql = """
        IF SCHEMA_ID(N'progress') IS NULL EXEC(N'CREATE SCHEMA [progress];');

        IF OBJECT_ID(N'[progress].[DelayReasons]', N'U') IS NULL
            CREATE TABLE [progress].[DelayReasons] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ProjectId] uniqueidentifier NOT NULL,
                [RegisterDate] date NOT NULL,
                [RootCause] int NOT NULL,
                [Description] nvarchar(2000) NOT NULL,
                [TimeImpactDays] int NULL,
                [CostImpact] decimal(18,0) NULL,
                [CorrectiveAction] nvarchar(2000) NULL,
                [ApprovalStatus] int NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_DelayReasons] PRIMARY KEY ([Id])
            );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DelayReasons_ProjectId' AND object_id = OBJECT_ID(N'[progress].[DelayReasons]'))
            CREATE INDEX [IX_DelayReasons_ProjectId] ON [progress].[DelayReasons] ([ProjectId]);
        """;

    public static Task EnsureCurrentAsync(DbContext progressDbContext, CancellationToken cancellationToken) =>
        progressDbContext.Database.ExecuteSqlRawAsync(EnsureDelayReasonsSql, cancellationToken);
}
