using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Documents.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-documents schema up to the current model. The
/// module's tables are created by ModuleSchemaInitializer.EnsureCreatedAsync, which never adds a
/// table or a column to a schema that already exists - so the version history added later
/// (ProjectDocumentVersions and ProjectDocuments.CurrentVersion) has to be patched in. A host
/// that already runs EnsureCreatedAsync for this module can call this right after it on every
/// start; it is a no-op once everything exists. Hosts that manage the schema by script instead
/// apply docs/upgrade/2026-10-03-add-document-versions.sql, which does the same thing.
/// </summary>
public static class DocumentsSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'project_documents') IS NULL EXEC(N'CREATE SCHEMA [project_documents];');",

        """
        IF OBJECT_ID(N'[project_documents].[ProjectDocumentVersions]', N'U') IS NULL
            CREATE TABLE [project_documents].[ProjectDocumentVersions] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [DocumentId] uniqueidentifier NOT NULL,
                [VersionNumber] int NOT NULL,
                [StorageKey] nvarchar(300) NOT NULL,
                [FileName] nvarchar(260) NOT NULL,
                [ContentType] nvarchar(150) NOT NULL,
                [SizeBytes] bigint NOT NULL,
                [Comment] nvarchar(1000) NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_ProjectDocumentVersions] PRIMARY KEY ([Id])
            );
        """,

        """
        IF COL_LENGTH(N'project_documents.ProjectDocuments', N'CurrentVersion') IS NULL
            ALTER TABLE [project_documents].[ProjectDocuments] ADD [CurrentVersion] int NOT NULL CONSTRAINT [DF_ProjectDocuments_CurrentVersion] DEFAULT 1;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectDocumentVersions_DocumentId_VersionNumber' AND object_id = OBJECT_ID(N'[project_documents].[ProjectDocumentVersions]'))
            CREATE UNIQUE INDEX [IX_ProjectDocumentVersions_DocumentId_VersionNumber] ON [project_documents].[ProjectDocumentVersions] ([DocumentId], [VersionNumber]);
        """,

        // Every document that already exists becomes "version 1 of 1", pointing at the file it
        // already has - nothing is copied or moved in storage.
        """
        INSERT INTO [project_documents].[ProjectDocumentVersions]
            ([Id], [TenantId], [DocumentId], [VersionNumber], [StorageKey], [FileName], [ContentType], [SizeBytes], [Comment], [CreatedAtUtc], [CreatedByUserId], [ModifiedAtUtc], [ModifiedByUserId])
        SELECT NEWID(), d.[TenantId], d.[Id], 1, d.[StorageKey], d.[FileName], d.[ContentType], d.[SizeBytes], NULL, d.[CreatedAtUtc], d.[CreatedByUserId], NULL, NULL
        FROM [project_documents].[ProjectDocuments] AS d
        WHERE NOT EXISTS (SELECT 1 FROM [project_documents].[ProjectDocumentVersions] AS v WHERE v.[DocumentId] = d.[Id]);
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext documentsDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await documentsDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await documentsDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
