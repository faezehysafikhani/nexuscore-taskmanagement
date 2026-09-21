/*
    Upgrade an EXISTING chat database (the "Chat" connection string) for direct messages with
    attachments. Not needed when the chat database does not exist yet: the host creates it
    with the current schema on first start.

    Safe to run more than once. Take a backup first, then:

        sqlcmd -S <server> -d <chat database> -E -C -b -i 2026-09-21-upgrade-existing-chat-database.sql

    Equivalent EF Core migration: ChatDbContext 20260921105457_AddDirectMessagingAndAttachments.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Messages', N'AttachmentFileName') IS NULL
    ALTER TABLE [dbo].[Messages] ADD [AttachmentFileName] nvarchar(260) NULL;
IF COL_LENGTH(N'dbo.Messages', N'AttachmentContentType') IS NULL
    ALTER TABLE [dbo].[Messages] ADD [AttachmentContentType] nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.Messages', N'AttachmentSizeBytes') IS NULL
    ALTER TABLE [dbo].[Messages] ADD [AttachmentSizeBytes] bigint NULL;
IF COL_LENGTH(N'dbo.Messages', N'AttachmentStorageKey') IS NULL
    ALTER TABLE [dbo].[Messages] ADD [AttachmentStorageKey] nvarchar(300) NULL;
IF COL_LENGTH(N'dbo.Conversations', N'DirectKey') IS NULL
    ALTER TABLE [dbo].[Conversations] ADD [DirectKey] nvarchar(65) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Messages_ConversationId_SentAt' AND object_id = OBJECT_ID(N'dbo.Messages'))
    CREATE INDEX [IX_Messages_ConversationId_SentAt] ON [dbo].[Messages] ([ConversationId], [SentAt]);

-- Existing duplicate read rows would block the unique index: keep one per message and user.
;WITH ranked AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY [MessageId], [UserId] ORDER BY [ReadAt]) AS rn
    FROM [dbo].[MessageReads]
    WHERE [MessageId] IS NOT NULL AND [UserId] IS NOT NULL)
DELETE FROM ranked WHERE rn > 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MessageReads_MessageId_UserId' AND object_id = OBJECT_ID(N'dbo.MessageReads'))
    CREATE UNIQUE INDEX [IX_MessageReads_MessageId_UserId] ON [dbo].[MessageReads] ([MessageId], [UserId]) WHERE [MessageId] IS NOT NULL AND [UserId] IS NOT NULL;

-- Direct conversations from before this release have no DirectKey, so the pair's history would
-- not be found. Give each pair's oldest direct conversation its key - the same value
-- Conversation.BuildDirectKey computes: both ids as 32 lowercase hex digits, the smaller first
-- (Guid.CompareTo order equals the order of the canonical lowercase string).
;WITH pairs AS (
    SELECT c.[Id],
           c.[CreatedAt],
           MIN(LOWER(CONVERT(nvarchar(36), p.[UserId]))) AS low_id,
           MAX(LOWER(CONVERT(nvarchar(36), p.[UserId]))) AS high_id
    FROM [dbo].[Conversations] c
    JOIN [dbo].[ConversationParticipants] p ON p.[ConversationId] = c.[Id]
    WHERE c.[Type] = 0 AND c.[DirectKey] IS NULL AND p.[UserId] IS NOT NULL
    GROUP BY c.[Id], c.[CreatedAt]
    HAVING COUNT(DISTINCT p.[UserId]) = 2),
keyed AS (
    SELECT [Id],
           REPLACE(low_id, N'-', N'') + N':' + REPLACE(high_id, N'-', N'') AS direct_key,
           ROW_NUMBER() OVER (PARTITION BY low_id, high_id ORDER BY [CreatedAt]) AS rn
    FROM pairs)
UPDATE c
SET [DirectKey] = k.direct_key
FROM [dbo].[Conversations] c
JOIN keyed k ON k.[Id] = c.[Id]
WHERE k.rn = 1
  AND NOT EXISTS (SELECT 1 FROM [dbo].[Conversations] x WHERE x.[DirectKey] = k.direct_key);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_DirectKey' AND object_id = OBJECT_ID(N'dbo.Conversations'))
    CREATE UNIQUE INDEX [IX_Conversations_DirectKey] ON [dbo].[Conversations] ([DirectKey]) WHERE [DirectKey] IS NOT NULL;
GO

COMMIT TRANSACTION;
GO

PRINT N'Chat upgrade complete.';
