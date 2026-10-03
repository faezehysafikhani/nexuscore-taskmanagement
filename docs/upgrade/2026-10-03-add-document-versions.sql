/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) with full version
    history for project documents: every file ever uploaded for a document is kept, and the
    document always shows its newest one.

    What it does, in order:
      1. creates [project_documents].[ProjectDocumentVersions] (one row per uploaded file);
      2. adds [project_documents].[ProjectDocuments].[CurrentVersion] int NOT NULL DEFAULT 1;
      3. creates the indexes;
      4. back-fills one version-1 row for every existing document, copied from the file it
         already has, so no existing document starts with an empty history.

    Nothing is changed or dropped and no stored file is touched. Existing documents keep working
    exactly as before: they become "version 1 of 1".

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    adds a table or a column to a schema that already exists. A host that prefers code can call
    Nexus.ProjectManagement.Documents.Infrastructure.DocumentsSchemaUpgrade.EnsureCurrentAsync on
    startup instead; it runs the same statements in the same order. Safe to run more than once.

    Apply this BEFORE deploying the build that contains document versions: that build selects the
    new column, so a ProjectDocuments table without it fails every query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-document-versions.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'project_documents') IS NULL EXEC(N'CREATE SCHEMA [project_documents];');
GO

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
GO

IF COL_LENGTH(N'project_documents.ProjectDocuments', N'CurrentVersion') IS NULL
    ALTER TABLE [project_documents].[ProjectDocuments] ADD [CurrentVersion] int NOT NULL CONSTRAINT [DF_ProjectDocuments_CurrentVersion] DEFAULT 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectDocumentVersions_DocumentId_VersionNumber' AND object_id = OBJECT_ID(N'[project_documents].[ProjectDocumentVersions]'))
    CREATE UNIQUE INDEX [IX_ProjectDocumentVersions_DocumentId_VersionNumber] ON [project_documents].[ProjectDocumentVersions] ([DocumentId], [VersionNumber]);
GO

INSERT INTO [project_documents].[ProjectDocumentVersions]
    ([Id], [TenantId], [DocumentId], [VersionNumber], [StorageKey], [FileName], [ContentType], [SizeBytes], [Comment], [CreatedAtUtc], [CreatedByUserId], [ModifiedAtUtc], [ModifiedByUserId])
SELECT NEWID(), d.[TenantId], d.[Id], 1, d.[StorageKey], d.[FileName], d.[ContentType], d.[SizeBytes], NULL, d.[CreatedAtUtc], d.[CreatedByUserId], NULL, NULL
FROM [project_documents].[ProjectDocuments] AS d
WHERE NOT EXISTS (SELECT 1 FROM [project_documents].[ProjectDocumentVersions] AS v WHERE v.[DocumentId] = d.[Id]);
GO

COMMIT TRANSACTION;
GO
