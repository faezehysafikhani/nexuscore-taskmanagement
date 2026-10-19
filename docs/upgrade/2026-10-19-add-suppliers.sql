/*
    Upgrade an EXISTING NexusCore database (the DefaultConnection database) for suppliers in project contracts:
      - a Suppliers table, one row per contractor / supplier / consultant of the tenant (name, category, status, national ID,
        contact details), unique by name within the tenant;
      - an optional SupplierId on every contract, saying which supplier it is with.

    What it does: creates [project_contracts].[Suppliers] with its unique index, adds one NULLABLE column
    (Contracts.SupplierId) with an index, then BACK-FILLS from what the contracts already say: every distinct name typed in
    Contracts.Counterparty (compared with Arabic yeh/kaf folded to the Persian letters, half-spaces and runs of spaces folded to one
    space, trimmed and lower-cased) becomes one supplier (category Other, active) of that tenant, and each contract is linked to the
    supplier of its name. Contracts.Counterparty itself is not touched. Nothing is changed or dropped, and the only UPDATE sets
    Contracts.SupplierId where it is still NULL, so running it again changes nothing.

    Why a script: hosts create schemas with ModuleSchemaInitializer (EnsureCreated), which never adds a column or a table next to
    tables that already exist. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsSuppliersSchemaUpgrade.EnsureCurrentAsync on startup instead; it runs the same
    statements in the same order.

    Apply this BEFORE deploying the build that contains suppliers: that build selects the new column and table, so a database
    without them fails every contract query.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-19-add-suppliers.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Skipped, without an error, on a database that does not have this module's tables yet: the module creates
-- its whole schema (this change included) the first time the host starts against such a database, and a
-- partial schema made here would stop it from doing so.
IF OBJECT_ID(N'[project_contracts].[Contracts]', N'U') IS NULL
BEGIN
    PRINT N'Skipped: Contracts is not installed in this database ([project_contracts].[Contracts] does not exist). Nothing to upgrade.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'[project_contracts].[Suppliers]', N'U') IS NULL
CREATE TABLE [project_contracts].[Suppliers] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(300) NOT NULL,
    [NormalizedName] nvarchar(300) NOT NULL,
    [Category] int NOT NULL,
    [Status] int NOT NULL,
    [NationalId] nvarchar(50) NULL,
    [ContactPerson] nvarchar(200) NULL,
    [Phone] nvarchar(50) NULL,
    [Email] nvarchar(200) NULL,
    [Address] nvarchar(1000) NULL,
    [Description] nvarchar(4000) NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [ModifiedAtUtc] datetimeoffset NULL,
    [ModifiedByUserId] uniqueidentifier NULL,
    CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Suppliers_TenantId_NormalizedName' AND object_id = OBJECT_ID(N'[project_contracts].[Suppliers]'))
    CREATE UNIQUE INDEX [IX_Suppliers_TenantId_NormalizedName] ON [project_contracts].[Suppliers] ([TenantId], [NormalizedName]);
GO

IF COL_LENGTH(N'project_contracts.Contracts', N'SupplierId') IS NULL
    ALTER TABLE [project_contracts].[Contracts] ADD [SupplierId] uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_SupplierId' AND object_id = OBJECT_ID(N'[project_contracts].[Contracts]'))
    CREATE INDEX [IX_Contracts_SupplierId] ON [project_contracts].[Contracts] ([SupplierId]);
GO

INSERT INTO [project_contracts].[Suppliers] ([Id], [TenantId], [Name], [NormalizedName], [Category], [Status], [CreatedAtUtc])
SELECT NEWID(), n.[TenantId], n.[Name], n.[NormalizedName], 3, 0, SYSDATETIMEOFFSET()
FROM (
    SELECT c.[TenantId], MIN(LTRIM(RTRIM(c.[Counterparty]))) AS [Name], k.[NormalizedName]
    FROM [project_contracts].[Contracts] c
    CROSS APPLY (SELECT LOWER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.[Counterparty], NCHAR(1610), NCHAR(1740)), NCHAR(1603), NCHAR(1705)), NCHAR(8204), N' '), N'  ', N' ' + NCHAR(7)), NCHAR(7) + N' ', N''), NCHAR(7), N'')))) AS [NormalizedName]) k
    WHERE k.[NormalizedName] <> N''
    GROUP BY c.[TenantId], k.[NormalizedName]
) n
WHERE NOT EXISTS (SELECT 1 FROM [project_contracts].[Suppliers] s WHERE s.[TenantId] = n.[TenantId] AND s.[NormalizedName] = n.[NormalizedName]);
GO

UPDATE c SET c.[SupplierId] = s.[Id]
FROM [project_contracts].[Contracts] c
CROSS APPLY (SELECT LOWER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.[Counterparty], NCHAR(1610), NCHAR(1740)), NCHAR(1603), NCHAR(1705)), NCHAR(8204), N' '), N'  ', N' ' + NCHAR(7)), NCHAR(7) + N' ', N''), NCHAR(7), N'')))) AS [NormalizedName]) k
JOIN [project_contracts].[Suppliers] s ON s.[TenantId] = c.[TenantId] AND s.[NormalizedName] = k.[NormalizedName]
WHERE c.[SupplierId] IS NULL;
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
