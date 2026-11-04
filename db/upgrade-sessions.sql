-- Session management (NexusCore): the UserSessions and PendingSignIns tables, the session columns of
-- identity.Users and platform.AuditLogs. Matches the EF Core migration SessionManagement
-- (NexusCore.Infrastructure) and NexusCore.Infrastructure.Persistence.SessionSchema.
-- Idempotent: safe to run again. Additive only: the previous version of the program keeps working with it.
-- Not needed when Database:SeedOnStartup is true (the default): the API adds them at start-up.
--   sqlcmd -S <server> -d <database> -I -i db/upgrade-sessions.sql
-- Take a backup first. Everybody signs in again once after the new program starts (old tokens have no session).
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'identity.Users', N'MaxConcurrentSessions') IS NULL ALTER TABLE [identity].[Users] ADD [MaxConcurrentSessions] int NULL
GO

IF COL_LENGTH(N'identity.Users', N'SmsOnEveryNewSignIn') IS NULL ALTER TABLE [identity].[Users] ADD [SmsOnEveryNewSignIn] bit NOT NULL CONSTRAINT [DF_Users_SmsOnEveryNewSignIn] DEFAULT 0
GO

IF COL_LENGTH(N'platform.AuditLogs', N'SessionId') IS NULL ALTER TABLE [platform].[AuditLogs] ADD [SessionId] uniqueidentifier NULL
GO

IF COL_LENGTH(N'platform.AuditLogs', N'UserAgent') IS NULL ALTER TABLE [platform].[AuditLogs] ADD [UserAgent] nvarchar(512) NULL
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_SessionId' AND object_id = OBJECT_ID(N'platform.AuditLogs')) CREATE INDEX [IX_AuditLogs_SessionId] ON [platform].[AuditLogs] ([SessionId])
GO

IF OBJECT_ID(N'identity.UserSessions', N'U') IS NULL CREATE TABLE [identity].[UserSessions] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [TenantId] uniqueidentifier NOT NULL, [CreatedAtUtc] datetimeoffset NOT NULL, [LastActivityAtUtc] datetimeoffset NOT NULL, [LastSeenAtUtc] datetimeoffset NOT NULL, [AbsoluteExpiresAtUtc] datetimeoffset NOT NULL, [IdleTimeoutSeconds] int NOT NULL, [PolicyProfile] nvarchar(40) NOT NULL, [RevokedAtUtc] datetimeoffset NULL, [RevokedReason] nvarchar(40) NULL, [RevokedByUserId] uniqueidentifier NULL, [CreatedIp] nvarchar(64) NULL, [LastIp] nvarchar(64) NULL, [UserAgent] nvarchar(512) NULL, [DeviceLabel] nvarchar(80) NULL, CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id]), CONSTRAINT [FK_UserSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Users] ([Id]) ON DELETE CASCADE)
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserSessions_UserId_RevokedAtUtc' AND object_id = OBJECT_ID(N'identity.UserSessions')) CREATE INDEX [IX_UserSessions_UserId_RevokedAtUtc] ON [identity].[UserSessions] ([UserId], [RevokedAtUtc])
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserSessions_TenantId_RevokedAtUtc' AND object_id = OBJECT_ID(N'identity.UserSessions')) CREATE INDEX [IX_UserSessions_TenantId_RevokedAtUtc] ON [identity].[UserSessions] ([TenantId], [RevokedAtUtc])
GO

IF OBJECT_ID(N'identity.PendingSignIns', N'U') IS NULL CREATE TABLE [identity].[PendingSignIns] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [TicketHash] nvarchar(128) NOT NULL, [ExpiresAtUtc] datetimeoffset NOT NULL, [CreatedIp] nvarchar(64) NULL, [UsedAtUtc] datetimeoffset NULL, CONSTRAINT [PK_PendingSignIns] PRIMARY KEY ([Id]))
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PendingSignIns_TicketHash' AND object_id = OBJECT_ID(N'identity.PendingSignIns')) CREATE UNIQUE INDEX [IX_PendingSignIns_TicketHash] ON [identity].[PendingSignIns] ([TicketHash])
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PendingSignIns_ExpiresAtUtc' AND object_id = OBJECT_ID(N'identity.PendingSignIns')) CREATE INDEX [IX_PendingSignIns_ExpiresAtUtc] ON [identity].[PendingSignIns] ([ExpiresAtUtc])
GO
