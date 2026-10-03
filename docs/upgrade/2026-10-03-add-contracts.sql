/*
    Create the project-contracts schema on an EXISTING NexusCore database (the DefaultConnection
    database): contracts, their addenda and their invoices for the Nexus.ProjectManagement.Contracts
    module.

    What it does, in order:
      1. creates the [project_contracts] schema;
      2. creates [Contracts], [ContractAddenda] and [ContractInvoices];
      3. creates the unique indexes (contract number per project, addendum number per contract,
         invoice number per contract).

    Nothing existing is changed or dropped.

    Why a script: a host that already runs ModuleSchemaInitializer.EnsureCreatedAsync for
    ContractsDbContext gets all of this on its own the first time the module starts, because the
    schema is new. Hosts that manage the schema by script instead - or that want the tables in
    place before the build is deployed - run this file. A host that prefers code can call
    Nexus.ProjectManagement.Contracts.Infrastructure.ContractsSchemaUpgrade.EnsureCurrentAsync; it
    runs the same statements in the same order. Safe to run more than once, and safe to run after
    EnsureCreatedAsync has already created the tables.

        sqlcmd -S <server> -d <database> -E -C -b -i 2026-10-03-add-contracts.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF SCHEMA_ID(N'project_contracts') IS NULL EXEC(N'CREATE SCHEMA [project_contracts];');
GO

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
GO

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
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Contracts_ProjectId_ContractNumber' AND object_id = OBJECT_ID(N'[project_contracts].[Contracts]'))
    CREATE UNIQUE INDEX [IX_Contracts_ProjectId_ContractNumber] ON [project_contracts].[Contracts] ([ProjectId], [ContractNumber]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractAddenda_ContractId_Number' AND object_id = OBJECT_ID(N'[project_contracts].[ContractAddenda]'))
    CREATE UNIQUE INDEX [IX_ContractAddenda_ContractId_Number] ON [project_contracts].[ContractAddenda] ([ContractId], [Number]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContractInvoices_ContractId_InvoiceNumber' AND object_id = OBJECT_ID(N'[project_contracts].[ContractInvoices]'))
    CREATE UNIQUE INDEX [IX_ContractInvoices_ContractId_InvoiceNumber] ON [project_contracts].[ContractInvoices] ([ContractId], [InvoiceNumber]);
GO

COMMIT TRANSACTION;
GO
