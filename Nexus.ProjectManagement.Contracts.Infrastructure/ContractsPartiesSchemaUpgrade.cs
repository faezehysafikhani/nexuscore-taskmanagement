using Microsoft.EntityFrameworkCore;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

/// <summary>
/// Brings an existing deployment's project-contracts schema up to the current model: the supervisor and beneficiary of a contract, the
/// period an addendum covers, and the title and billing period of an invoice. ModuleSchemaInitializer.EnsureCreatedAsync never adds a column
/// to a table that already exists, so they have to be patched in. A host that runs EnsureCreatedAsync for this module can call this right
/// after it on every start; it is a no-op once the columns exist. Hosts that manage the schema by script apply
/// docs/upgrade/2026-10-14-add-contract-parties-and-billing.sql, which does the same thing in the same order.
/// </summary>
public static class ContractsPartiesSchemaUpgrade
{
    // Every column is nullable: an existing contract, addendum or invoice simply has none of them.
    internal static readonly string[] Statements =
    [
        """
        IF COL_LENGTH(N'project_contracts.Contracts', N'SupervisorName') IS NULL
            ALTER TABLE [project_contracts].[Contracts] ADD [SupervisorName] nvarchar(300) NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.Contracts', N'BeneficiaryName') IS NULL
            ALTER TABLE [project_contracts].[Contracts] ADD [BeneficiaryName] nvarchar(300) NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.ContractAddenda', N'StartDate') IS NULL
            ALTER TABLE [project_contracts].[ContractAddenda] ADD [StartDate] date NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.ContractAddenda', N'EndDate') IS NULL
            ALTER TABLE [project_contracts].[ContractAddenda] ADD [EndDate] date NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.ContractInvoices', N'Title') IS NULL
            ALTER TABLE [project_contracts].[ContractInvoices] ADD [Title] nvarchar(300) NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.ContractInvoices', N'PeriodStartDate') IS NULL
            ALTER TABLE [project_contracts].[ContractInvoices] ADD [PeriodStartDate] date NULL;
        """,

        """
        IF COL_LENGTH(N'project_contracts.ContractInvoices', N'PeriodEndDate') IS NULL
            ALTER TABLE [project_contracts].[ContractInvoices] ADD [PeriodEndDate] date NULL;
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
