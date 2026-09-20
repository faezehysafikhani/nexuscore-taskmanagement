-- ---------------------------------------------------------------------------
-- PasswordResetTokens
--
-- DefaultDataSeeder uses EnsureCreatedAsync(), which only creates a schema for a
-- brand-new database. Run this script once against any EXISTING NexusCore database
-- so the forgot-password / reset-password flow has its table.
-- Safe to re-run.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'identity')
    EXEC('CREATE SCHEMA [identity]');
GO

IF OBJECT_ID('[identity].[PasswordResetTokens]', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].[PasswordResetTokens]
    (
        [Id]               UNIQUEIDENTIFIER   NOT NULL,
        [UserId]           UNIQUEIDENTIFIER   NOT NULL,
        [TokenHash]        NVARCHAR(128)      NOT NULL,
        [ExpiresAtUtc]     DATETIMEOFFSET     NOT NULL,
        [CreatedAtUtc]     DATETIMEOFFSET     NOT NULL,
        [RequestedByIp]    NVARCHAR(64)       NULL,
        [UsedAtUtc]        DATETIMEOFFSET     NULL,
        [InvalidatedAtUtc] DATETIMEOFFSET     NULL,
        CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasswordResetTokens_Users_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [identity].[Users]([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_PasswordResetTokens_TokenHash]
        ON [identity].[PasswordResetTokens]([TokenHash]);

    CREATE INDEX [IX_PasswordResetTokens_UserId_ExpiresAtUtc]
        ON [identity].[PasswordResetTokens]([UserId], [ExpiresAtUtc]);
END
GO
