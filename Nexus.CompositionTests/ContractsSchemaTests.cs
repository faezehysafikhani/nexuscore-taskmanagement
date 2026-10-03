using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class ContractsSchemaTests
{
    private const string ScriptFile = "2026-10-03-add-contracts.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheCreationScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_contracts", ScriptFile, typeof(ContractsSchemaUpgrade),
            ["Contracts", "ContractAddenda", "ContractInvoices"]);
    }

    [Fact]
    public void TheCreationScript_OnlyCreates()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsSchemaUpgrade));
    }

    [Fact]
    public void TheModel_KeepsNumbersUniqueWithinTheirOwner()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE UNIQUE INDEX [IX_Contracts_ProjectId_ContractNumber] ON [project_contracts].[Contracts] ([ProjectId], [ContractNumber])", generated);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ContractAddenda_ContractId_Number] ON [project_contracts].[ContractAddenda] ([ContractId], [Number])", generated);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ContractInvoices_ContractId_InvoiceNumber] ON [project_contracts].[ContractInvoices] ([ContractId], [InvoiceNumber])", generated);
    }
}
