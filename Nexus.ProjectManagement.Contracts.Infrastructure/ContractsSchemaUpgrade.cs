using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Creates the project-contracts schema on a deployment that predates the Contracts module. A host
/// that runs ModuleSchemaInitializer.EnsureCreatedAsync for ContractsDbContext already gets it,
/// because the schema is new; this exists for hosts that prefer to call code over running
/// docs/upgrade/2026-10-03-add-contracts.sql, which does the same thing. It is a no-op once the
/// tables exist.
/// </summary>
public static class ContractsSchemaUpgrade
{
    // Run one at a time (each is its own batch, as the GO-separated script is), in this order, in
    // one transaction. Kept identical to the DDL EF generates for the model; a test compares them.
    internal static readonly string[] Statements =
    [
        "IF SCHEMA_ID(N'project_contracts') IS NULL EXEC(N'CREATE SCHEMA [project_contracts];');",

        """
        IF OBJECT_ID(N'[project_contracts].[Contracts]', N'U') IS NULL
        CREATE TABLE [project_contracts].[Contracts] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ProjectId] uniqueidentifier NOT NULL,
            [ContractNumber] nvarchar(100) NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Counterparty] nvarchar(300) NOT NULL,
            [Description] nvarchar(4000) NULL,
            [SignDate] date NULL,
            [StartDate] date NULL,
            [EndDate] date NULL,
            [OriginalAmount] decimal(19,0) NOT NULL,
            [Status] int NOT NULL,
            [ApprovalStatus] int NOT NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_Contracts] PRIMARY KEY ([Id])
        );
        """,

        """
        IF OBJECT_ID(N'[project_contracts].[ContractAddenda]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ContractAddenda] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ContractId] uniqueidentifier NOT NULL,
            [Number] int NOT NULL,
            [Title] nvarchar(300) NOT NULL,
            [Description] nvarchar(4000) NULL,
            [AddendumDate] date NULL,
            [AmountChange] decimal(19,0) NOT NULL,
            [ExtensionDays] int NOT NULL,
            [ApprovalStatus] int NOT NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ContractAddenda] PRIMARY KEY ([Id])
        );
        """,

        """
        IF OBJECT_ID(N'[project_contracts].[ContractInvoices]', N'U') IS NULL
        CREATE TABLE [project_contracts].[ContractInvoices] (
            [Id] uniqueidentifier NOT NULL,
            [TenantId] uniqueidentifier NOT NULL,
            [ContractId] uniqueidentifier NOT NULL,
            [InvoiceNumber] nvarchar(100) NOT NULL,
            [InvoiceDate] date NOT NULL,
            [Amount] decimal(19,0) NOT NULL,
            [Description] nvarchar(2000) NULL,
            [PaidAmount] decimal(19,0) NOT NULL,
            [LastPaymentDate] date NULL,
            [ApprovalStatus] int NOT NULL,
            [CreatedAtUtc] datetimeoffset NOT NULL,
            [CreatedByUserId] uniqueidentifier NULL,
            [ModifiedAtUtc] datetimeoffset NULL,
            [ModifiedByUserId] uniqueidentifier NULL,
            CONSTRAINT [PK_ContractInvoices] PRIMARY KEY ([Id])
        );
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_ProjectId_ContractNumber' AND object_id = OBJECT_ID(N'[project_contracts].[Contracts]'))
            CREATE UNIQUE INDEX [IX_Contracts_ProjectId_ContractNumber] ON [project_contracts].[Contracts] ([ProjectId], [ContractNumber]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractAddenda_ContractId_Number' AND object_id = OBJECT_ID(N'[project_contracts].[ContractAddenda]'))
            CREATE UNIQUE INDEX [IX_ContractAddenda_ContractId_Number] ON [project_contracts].[ContractAddenda] ([ContractId], [Number]);
        """,

        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractInvoices_ContractId_InvoiceNumber' AND object_id = OBJECT_ID(N'[project_contracts].[ContractInvoices]'))
            CREATE UNIQUE INDEX [IX_ContractInvoices_ContractId_InvoiceNumber] ON [project_contracts].[ContractInvoices] ([ContractId], [InvoiceNumber]);
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
