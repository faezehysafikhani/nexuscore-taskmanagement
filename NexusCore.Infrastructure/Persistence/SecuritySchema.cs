using Microsoft.EntityFrameworkCore;

namespace NexusCore.Infrastructure.Persistence;

/// <summary>
/// The columns and tables of the second sign-in step (SMS code), account kinds (guest, service account) and API keys, for a
/// database that existed before them. The seeder runs it at start-up; docs/upgrade/2026-11-21-add-mfa-guests-service-accounts.sql
/// holds the same statements for a database changed by hand. Every statement checks first, so running it again is safe.
/// </summary>
public static class SecuritySchema
{
    public static readonly string[] Statements =
    [
        "IF COL_LENGTH(N'identity.Users', N'Kind') IS NULL ALTER TABLE [identity].[Users] ADD [Kind] int NOT NULL CONSTRAINT [DF_Users_Kind] DEFAULT 0",
        "IF COL_LENGTH(N'identity.Users', N'MfaEnabled') IS NULL ALTER TABLE [identity].[Users] ADD [MfaEnabled] bit NOT NULL CONSTRAINT [DF_Users_MfaEnabled] DEFAULT 0",
        "IF COL_LENGTH(N'identity.Users', N'LdapDn') IS NULL ALTER TABLE [identity].[Users] ADD [LdapDn] nvarchar(500) NULL",
        "IF OBJECT_ID(N'identity.MfaChallenges', N'U') IS NULL CREATE TABLE [identity].[MfaChallenges] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [Purpose] int NOT NULL, [CodeHash] nvarchar(128) NOT NULL, [CreatedAtUtc] datetimeoffset NOT NULL, [ExpiresAtUtc] datetimeoffset NOT NULL, [Attempts] int NOT NULL, [UsedAtUtc] datetimeoffset NULL, [CreatedIp] nvarchar(64) NULL, CONSTRAINT [PK_MfaChallenges] PRIMARY KEY ([Id]))",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MfaChallenges_UserId_ExpiresAtUtc' AND object_id = OBJECT_ID(N'identity.MfaChallenges')) CREATE INDEX [IX_MfaChallenges_UserId_ExpiresAtUtc] ON [identity].[MfaChallenges] ([UserId], [ExpiresAtUtc])",
        "IF OBJECT_ID(N'identity.ApiKeys', N'U') IS NULL CREATE TABLE [identity].[ApiKeys] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [TenantId] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [SecretHash] nvarchar(128) NOT NULL, [CreatedAtUtc] datetimeoffset NOT NULL, [ExpiresAtUtc] datetimeoffset NULL, [RevokedAtUtc] datetimeoffset NULL, [LastUsedAtUtc] datetimeoffset NULL, [CreatedByUserId] uniqueidentifier NULL, CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]))",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApiKeys_UserId' AND object_id = OBJECT_ID(N'identity.ApiKeys')) CREATE INDEX [IX_ApiKeys_UserId] ON [identity].[ApiKeys] ([UserId])"
    ];

    public static async Task EnsureAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        foreach (var statement in Statements)
        {
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }
}
