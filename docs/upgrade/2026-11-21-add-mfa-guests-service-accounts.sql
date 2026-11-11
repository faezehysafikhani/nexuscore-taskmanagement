/*
    Identity: the SMS code at sign-in (MFA), account kinds (guest, service account) and API keys (phase 9b).
    Adds, never drops or rewrites. Safe to run more than once. The host's seeder applies the same statements on start-up
    (NexusCore.Infrastructure.Persistence.SecuritySchema), so this script is for sites that manage the schema by script.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-21-add-mfa-guests-service-accounts.sql
*/
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'identity.Users', N'Kind') IS NULL ALTER TABLE [identity].[Users] ADD [Kind] int NOT NULL CONSTRAINT [DF_Users_Kind] DEFAULT 0
GO

IF COL_LENGTH(N'identity.Users', N'MfaEnabled') IS NULL ALTER TABLE [identity].[Users] ADD [MfaEnabled] bit NOT NULL CONSTRAINT [DF_Users_MfaEnabled] DEFAULT 0
GO

IF OBJECT_ID(N'identity.MfaChallenges', N'U') IS NULL CREATE TABLE [identity].[MfaChallenges] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [Purpose] int NOT NULL, [CodeHash] nvarchar(128) NOT NULL, [CreatedAtUtc] datetimeoffset NOT NULL, [ExpiresAtUtc] datetimeoffset NOT NULL, [Attempts] int NOT NULL, [UsedAtUtc] datetimeoffset NULL, [CreatedIp] nvarchar(64) NULL, CONSTRAINT [PK_MfaChallenges] PRIMARY KEY ([Id]))
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MfaChallenges_UserId_ExpiresAtUtc' AND object_id = OBJECT_ID(N'identity.MfaChallenges')) CREATE INDEX [IX_MfaChallenges_UserId_ExpiresAtUtc] ON [identity].[MfaChallenges] ([UserId], [ExpiresAtUtc])
GO

IF OBJECT_ID(N'identity.ApiKeys', N'U') IS NULL CREATE TABLE [identity].[ApiKeys] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [TenantId] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [SecretHash] nvarchar(128) NOT NULL, [CreatedAtUtc] datetimeoffset NOT NULL, [ExpiresAtUtc] datetimeoffset NULL, [RevokedAtUtc] datetimeoffset NULL, [LastUsedAtUtc] datetimeoffset NULL, [CreatedByUserId] uniqueidentifier NULL, CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]))
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApiKeys_UserId' AND object_id = OBJECT_ID(N'identity.ApiKeys')) CREATE INDEX [IX_ApiKeys_UserId] ON [identity].[ApiKeys] ([UserId])
GO
