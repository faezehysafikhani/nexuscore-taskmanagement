using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to suppliers: the Suppliers table, the optional SupplierId on a contract,
/// and a back-fill that turns the names typed into existing contracts into suppliers and links each contract to its own. A host that runs
/// ModuleSchemaInitializer.EnsureCreatedAsync for this module gets nothing of this on a database that already has the contracts tables
/// (it never adds a column or a table next to existing ones), so it is applied here. A host that runs EnsureCreatedAsync can call this
/// right after it on every start; it is a no-op once done. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-19-add-suppliers.sql, which holds the same statements in the same order.
/// </summary>
public static class ContractsSuppliersSchemaUpgrade
{
    // The comparison form of a name, as SupplierNames.Normalize builds it in code: Arabic yeh and kaf to the Persian letters, a half-space
    // to a space, runs of spaces to one (through a placeholder character, since REPLACE does not repeat), trimmed, lower-cased.
    internal static readonly string[] Statements =
    [
        """
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
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Suppliers_TenantId_NormalizedName' AND object_id = OBJECT_ID(N'[project_contracts].[Suppliers]'))
            CREATE UNIQUE INDEX [IX_Suppliers_TenantId_NormalizedName] ON [project_contracts].[Suppliers] ([TenantId], [NormalizedName]);
        """,

        """
        IF COL_LENGTH(N'project_contracts.Contracts', N'SupplierId') IS NULL
            ALTER TABLE [project_contracts].[Contracts] ADD [SupplierId] uniqueidentifier NULL;
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_SupplierId' AND object_id = OBJECT_ID(N'[project_contracts].[Contracts]'))
            CREATE INDEX [IX_Contracts_SupplierId] ON [project_contracts].[Contracts] ([SupplierId]);
        """,

        // One supplier per distinct name (within a tenant) that the contracts already use; the first spelling in sort order is kept as its name.
        """
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
        """,

        """
        UPDATE c SET c.[SupplierId] = s.[Id]
        FROM [project_contracts].[Contracts] c
        CROSS APPLY (SELECT LOWER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.[Counterparty], NCHAR(1610), NCHAR(1740)), NCHAR(1603), NCHAR(1705)), NCHAR(8204), N' '), N'  ', N' ' + NCHAR(7)), NCHAR(7) + N' ', N''), NCHAR(7), N'')))) AS [NormalizedName]) k
        JOIN [project_contracts].[Suppliers] s ON s.[TenantId] = c.[TenantId] AND s.[NormalizedName] = k.[NormalizedName]
        WHERE c.[SupplierId] IS NULL;
        """
    ];

    public static async Task EnsureCurrentAsync(DbContext contractsDbContext, CancellationToken cancellationToken)
    {
        await using var transaction = await contractsDbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements)
        {
            await contractsDbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
