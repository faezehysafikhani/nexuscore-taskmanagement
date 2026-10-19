using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocumentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "knowledge");

            // Bootstrap the existing Knowledge table only when it is missing.
            migrationBuilder.Sql(
                """
        IF OBJECT_ID(N'[knowledge].[KnowledgeDocuments]', N'U') IS NULL
        BEGIN
            CREATE TABLE [knowledge].[KnowledgeDocuments]
            (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [Title] nvarchar(200) NOT NULL,
                [Description] nvarchar(2000) NULL,
                [DocumentType] int NOT NULL,
                [StorageKey] nvarchar(300) NOT NULL,
                [FileName] nvarchar(260) NOT NULL,
                [ContentType] nvarchar(150) NOT NULL,
                [SizeBytes] bigint NOT NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [CreatedByUserId] uniqueidentifier NULL,
                [ModifiedAtUtc] datetimeoffset NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                CONSTRAINT [PK_KnowledgeDocuments] PRIMARY KEY ([Id])
            );
        END;
        """);

            migrationBuilder.Sql(
                """
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_KnowledgeDocuments_TenantId_Title'
              AND object_id = OBJECT_ID(N'[knowledge].[KnowledgeDocuments]')
        )
        BEGIN
            CREATE INDEX [IX_KnowledgeDocuments_TenantId_Title]
            ON [knowledge].[KnowledgeDocuments] ([TenantId], [Title]);
        END;
        """);

            // Create only if missing. This also tolerates the table having been
            // created by the existing local schema initializer before this migration.
            migrationBuilder.Sql(
                """
        IF OBJECT_ID(N'[knowledge].[KnowledgeDocumentLinks]', N'U') IS NULL
        BEGIN
            CREATE TABLE [knowledge].[KnowledgeDocumentLinks]
            (
                [Id] uniqueidentifier NOT NULL,
                [DocumentId] uniqueidentifier NOT NULL,
                [SubjectType] nvarchar(100) NOT NULL,
                [SubjectId] uniqueidentifier NOT NULL,

                CONSTRAINT [PK_KnowledgeDocumentLinks]
                    PRIMARY KEY ([Id]),

                CONSTRAINT [FK_KnowledgeDocumentLinks_KnowledgeDocuments_DocumentId]
                    FOREIGN KEY ([DocumentId])
                    REFERENCES [knowledge].[KnowledgeDocuments] ([Id])
                    ON DELETE CASCADE
            );
        END;
        """);

            migrationBuilder.Sql(
                """
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_KnowledgeDocumentLinks_DocumentId_SubjectType_SubjectId'
              AND object_id = OBJECT_ID(N'[knowledge].[KnowledgeDocumentLinks]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_KnowledgeDocumentLinks_DocumentId_SubjectType_SubjectId]
            ON [knowledge].[KnowledgeDocumentLinks]
                ([DocumentId], [SubjectType], [SubjectId]);
        END;
        """);

            migrationBuilder.Sql(
                """
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_KnowledgeDocumentLinks_SubjectType_SubjectId'
              AND object_id = OBJECT_ID(N'[knowledge].[KnowledgeDocumentLinks]')
        )
        BEGIN
            CREATE INDEX [IX_KnowledgeDocumentLinks_SubjectType_SubjectId]
            ON [knowledge].[KnowledgeDocumentLinks] ([SubjectType], [SubjectId]);
        END;
        """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
        IF OBJECT_ID(N'[knowledge].[KnowledgeDocumentLinks]', N'U') IS NOT NULL
            DROP TABLE [knowledge].[KnowledgeDocumentLinks];
        """);
        }
    }
}
