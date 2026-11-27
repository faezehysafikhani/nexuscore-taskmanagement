/*
    Task management: the organization's plan - user and storage limits and validity (phase 9).
    Adds, never drops or rewrites. Safe to run more than once. Hosts that start with the task module applied
    (TaskManagementSchemaPatcher) get the same statements on startup.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-11-19-add-membership.sql
*/
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'task_management') IS NOT NULL AND OBJECT_ID(N'[task_management].[TenantMemberships]') IS NULL
    CREATE TABLE [task_management].[TenantMemberships] (
        [TenantId] uniqueidentifier NOT NULL,
        [PlanName] nvarchar(100) NOT NULL,
        [MaxUsers] int NULL,
        [MaxStorageMb] int NULL,
        [ValidFrom] date NULL,
        [ValidUntil] date NULL,
        [Notes] nvarchar(1000) NULL,
        CONSTRAINT [PK_TenantMemberships] PRIMARY KEY ([TenantId]));
GO
