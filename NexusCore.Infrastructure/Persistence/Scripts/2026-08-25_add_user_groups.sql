-- ---------------------------------------------------------------------------
-- User groups  (optional third permission axis)
--
-- Only needed when Features:UserGroups:Enabled is true. Harmless if run anyway -
-- with the feature off nothing reads these tables.
-- DefaultDataSeeder uses EnsureCreatedAsync(), which only builds a schema for a
-- brand-new database, so run this once against any EXISTING NexusCore database.
-- Safe to re-run.
-- ---------------------------------------------------------------------------

IF OBJECT_ID('[identity].[UserGroups]', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].[UserGroups]
    (
        [Id]             UNIQUEIDENTIFIER NOT NULL,
        [TenantId]       UNIQUEIDENTIFIER NOT NULL,
        [Name]           NVARCHAR(128)    NOT NULL,
        [NormalizedName] NVARCHAR(128)    NOT NULL,
        [Description]    NVARCHAR(512)    NULL,
        [IsActive]       BIT              NOT NULL CONSTRAINT [DF_UserGroups_IsActive] DEFAULT (1),
        [CreatedAtUtc]   DATETIMEOFFSET   NOT NULL CONSTRAINT [DF_UserGroups_CreatedAtUtc] DEFAULT (SYSDATETIMEOFFSET()),
        [CreatedBy]      UNIQUEIDENTIFIER NULL,
        [UpdatedAtUtc]   DATETIMEOFFSET   NULL,
        [UpdatedBy]      UNIQUEIDENTIFIER NULL,
        CONSTRAINT [PK_UserGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserGroups_Tenants_TenantId]
            FOREIGN KEY ([TenantId]) REFERENCES [identity].[Tenants]([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_UserGroups_TenantId_NormalizedName]
        ON [identity].[UserGroups]([TenantId], [NormalizedName]);
END
GO

IF OBJECT_ID('[identity].[UserGroupPermissions]', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].[UserGroupPermissions]
    (
        [UserGroupId]  UNIQUEIDENTIFIER NOT NULL,
        [PermissionId] UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT [PK_UserGroupPermissions] PRIMARY KEY ([UserGroupId], [PermissionId]),
        CONSTRAINT [FK_UserGroupPermissions_UserGroups_UserGroupId]
            FOREIGN KEY ([UserGroupId]) REFERENCES [identity].[UserGroups]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserGroupPermissions_Permissions_PermissionId]
            FOREIGN KEY ([PermissionId]) REFERENCES [identity].[Permissions]([Id]) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID('[identity].[UserGroupMembers]', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].[UserGroupMembers]
    (
        [UserGroupId] UNIQUEIDENTIFIER NOT NULL,
        [UserId]      UNIQUEIDENTIFIER NOT NULL,
        [JoinedAtUtc] DATETIMEOFFSET   NOT NULL CONSTRAINT [DF_UserGroupMembers_JoinedAtUtc] DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT [PK_UserGroupMembers] PRIMARY KEY ([UserGroupId], [UserId]),
        CONSTRAINT [FK_UserGroupMembers_UserGroups_UserGroupId]
            FOREIGN KEY ([UserGroupId]) REFERENCES [identity].[UserGroups]([Id]) ON DELETE CASCADE,
        -- NO ACTION on purpose: Users already cascade from Tenants, and a second
        -- cascade path into this table would be rejected by SQL Server.
        CONSTRAINT [FK_UserGroupMembers_Users_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [identity].[Users]([Id])
    );

    CREATE INDEX [IX_UserGroupMembers_UserId]
        ON [identity].[UserGroupMembers]([UserId]);
END
GO

-- The groups.* permissions are inserted automatically by DefaultDataSeeder on the
-- next application start and granted to the Administrator role.
