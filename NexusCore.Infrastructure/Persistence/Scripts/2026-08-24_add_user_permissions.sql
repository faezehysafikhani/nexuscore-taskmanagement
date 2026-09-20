-- ---------------------------------------------------------------------------
-- UserPermissions  (direct, per-user permission grants)
--
-- Effective permissions = role-derived UNION direct grants.
-- DefaultDataSeeder uses EnsureCreatedAsync(), which only builds a schema for a
-- brand-new database. Run this once against any EXISTING NexusCore database.
-- Safe to re-run.
-- ---------------------------------------------------------------------------

IF OBJECT_ID('[identity].[UserPermissions]', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].[UserPermissions]
    (
        [UserId]       UNIQUEIDENTIFIER NOT NULL,
        [PermissionId] UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([UserId], [PermissionId]),
        CONSTRAINT [FK_UserPermissions_Users_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [identity].[Users]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserPermissions_Permissions_PermissionId]
            FOREIGN KEY ([PermissionId]) REFERENCES [identity].[Permissions]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_UserPermissions_PermissionId]
        ON [identity].[UserPermissions]([PermissionId]);
END
GO

-- The new "users.assign_permissions" permission is inserted automatically by
-- DefaultDataSeeder on the next application start, and granted to the Administrator role.
