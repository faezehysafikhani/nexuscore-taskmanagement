/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) to sign-in by
    username or mobile number, with Telegram removed.

    What it does:
      * Brings every mobile number into the canonical form the application now uses
        (09xxxxxxxxx for Iranian mobiles, +<digits> for other countries). A value that cannot be
        recognised is left as it is - never deleted.
      * Stops, changing nothing, if two users of one tenant then have the same mobile number:
        which account keeps the number is for a person to decide.
      * Makes mobile numbers unique per tenant (like usernames); makes email optional (still
        unique when present).
      * Removes identity.Users.TelegramChatId / NotifyTelegram and the Telegram section of the
        stored notification settings (including the encrypted bot token).

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never
    changes an existing table. Apply after 2026-09-22-add-task-and-subtask-times.sql, with a
    backup first. Safe to run more than once.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-09-22-signin-by-username-or-phone.sql

    Equivalent EF Core migration, for databases managed with `dotnet ef database update`
    (do not run both):
      NexusCoreDbContext        20260922073044_SignInByUsernameOrPhoneAndRemoveTelegram
      TaskManagementDbContext   20260922073048_RemoveTelegramFromUserModel (no schema change)
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered indexes below
GO

BEGIN TRANSACTION;
GO

-- 1. Canonical mobile numbers (same rules as NexusCore.Domain.Identity.PhoneNumber).
UPDATE u SET [PhoneNumber] = n.[Canonical]
FROM [identity].[Users] AS u
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(u.[PhoneNumber], NCHAR(1776), N'0'), NCHAR(1632), N'0'), NCHAR(1777), N'1'), NCHAR(1633), N'1'), NCHAR(1778), N'2'), NCHAR(1634), N'2'), NCHAR(1779), N'3'), NCHAR(1635), N'3'), NCHAR(1780), N'4'), NCHAR(1636), N'4'), NCHAR(1781), N'5'), NCHAR(1637), N'5'), NCHAR(1782), N'6'), NCHAR(1638), N'6'), NCHAR(1783), N'7'), NCHAR(1639), N'7'), NCHAR(1784), N'8'), NCHAR(1640), N'8'), NCHAR(1785), N'9'), NCHAR(1641), N'9'), N' ', N''), N'-', N''), N'(', N''), N')', N''), N'.', N''), NCHAR(8204), N''), NCHAR(8206), N''), NCHAR(8207), N''), NCHAR(160), N'') AS [V]) AS c
CROSS APPLY (SELECT CASE
    WHEN c.[V] LIKE N'+980%' THEN SUBSTRING(c.[V], 4, 40)
    WHEN c.[V] LIKE N'+98%' THEN N'0' + SUBSTRING(c.[V], 4, 40)
    WHEN c.[V] LIKE N'00980%' THEN SUBSTRING(c.[V], 5, 40)
    WHEN c.[V] LIKE N'0098%' THEN N'0' + SUBSTRING(c.[V], 5, 40)
    WHEN c.[V] LIKE N'989%' AND LEN(c.[V]) = 12 THEN N'0' + SUBSTRING(c.[V], 3, 40)
    WHEN c.[V] LIKE N'9%' AND LEN(c.[V]) = 10 THEN N'0' + c.[V]
    ELSE c.[V] END AS [Iranian]) AS i
CROSS APPLY (SELECT CASE
    WHEN i.[Iranian] LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' THEN i.[Iranian]
    WHEN c.[V] LIKE N'+[1-9]%' AND c.[V] NOT LIKE N'+98%' AND LEN(c.[V]) BETWEEN 9 AND 16
         AND SUBSTRING(c.[V], 2, 40) NOT LIKE N'%[^0-9]%' THEN c.[V]
    WHEN c.[V] LIKE N'00[1-9]%' AND c.[V] NOT LIKE N'0098%' AND LEN(c.[V]) BETWEEN 10 AND 17
         AND c.[V] NOT LIKE N'%[^0-9]%' THEN N'+' + SUBSTRING(c.[V], 3, 40)
    ELSE NULL END AS [Canonical]) AS n
WHERE u.[PhoneNumber] IS NOT NULL AND n.[Canonical] IS NOT NULL AND n.[Canonical] <> u.[PhoneNumber];
GO

-- 2. A mobile number is a sign-in name: one user per tenant.
IF EXISTS (SELECT 1 FROM [identity].[Users] WHERE [PhoneNumber] IS NOT NULL
           GROUP BY [TenantId], [PhoneNumber] HAVING COUNT(*) > 1)
BEGIN
    -- Roll back and stop every following batch, also in tools that carry on after an error.
    ROLLBACK TRANSACTION;
    RAISERROR(N'Two or more users of the same tenant have the same mobile number. Give each of them a different number (or clear the duplicate), then run this script again. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 3. Telegram settings out of the stored notification channels.
UPDATE [platform].[Settings]
SET [Value] = JSON_MODIFY([Value], '$.telegram', NULL)
WHERE [Key] = N'Notifications.Channels' AND ISJSON([Value]) = 1 AND JSON_QUERY([Value], '$.telegram') IS NOT NULL;
GO

-- 4. Telegram columns.
DECLARE @constraint sysname;
IF COL_LENGTH(N'identity.Users', N'NotifyTelegram') IS NOT NULL
BEGIN
    SELECT @constraint = d.[name] FROM sys.default_constraints AS d
    JOIN sys.columns AS c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.[object_id]
    WHERE d.parent_object_id = OBJECT_ID(N'[identity].[Users]') AND c.[name] = N'NotifyTelegram';
    IF @constraint IS NOT NULL EXEC(N'ALTER TABLE [identity].[Users] DROP CONSTRAINT [' + @constraint + N'];');
    ALTER TABLE [identity].[Users] DROP COLUMN [NotifyTelegram];
END
GO

IF COL_LENGTH(N'identity.Users', N'TelegramChatId') IS NOT NULL
    ALTER TABLE [identity].[Users] DROP COLUMN [TelegramChatId];
GO

-- 5. Email optional, unique when present.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[identity].[Users]')
           AND [name] = N'IX_Users_TenantId_Email' AND has_filter = 0)
    DROP INDEX [IX_Users_TenantId_Email] ON [identity].[Users];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[identity].[Users]')
           AND [name] = N'Email' AND is_nullable = 0)
    ALTER TABLE [identity].[Users] ALTER COLUMN [Email] nvarchar(256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[identity].[Users]') AND [name] = N'IX_Users_TenantId_Email')
    CREATE UNIQUE INDEX [IX_Users_TenantId_Email] ON [identity].[Users] ([TenantId], [Email]) WHERE [Email] IS NOT NULL;
GO

-- 6. Mobile numbers unique per tenant.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[identity].[Users]') AND [name] = N'IX_Users_TenantId_PhoneNumber')
    CREATE UNIQUE INDEX [IX_Users_TenantId_PhoneNumber] ON [identity].[Users] ([TenantId], [PhoneNumber]) WHERE [PhoneNumber] IS NOT NULL;
GO

-- 7. Keep EF's migration history in step, when the database has one. Dynamic SQL, because a
--    database built by EnsureCreated has no history table and the batch must still compile.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NOT NULL
    EXEC (N'
        IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260922073044_SignInByUsernameOrPhoneAndRemoveTelegram'')
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N''20260922073044_SignInByUsernameOrPhoneAndRemoveTelegram'', N''8.0.24'');
        IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''20260922073048_RemoveTelegramFromUserModel'')
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N''20260922073048_RemoveTelegramFromUserModel'', N''8.0.24'');');
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
