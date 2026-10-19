using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>Suppliers kept once per tenant, and the contracts' link to them (create, rename, unlink, delete, the figures).</summary>
public sealed class SuppliersTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Fixture
    {
        public ContractsDbContext Db { get; }
        public ContractService Contracts { get; }
        public ContractInvoiceService Invoices { get; }
        public SupplierService Suppliers { get; }

        public Fixture(bool withSupplierStore = true)
        {
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(Db);
            var supplierRepository = new SupplierRepository(Db);
            Contracts = new ContractService(repository, Db, new NotConfigured(), withSupplierStore ? supplierRepository : null);
            Invoices = new ContractInvoiceService(repository, Db, new NotConfigured());
            Suppliers = new SupplierService(supplierRepository, repository, Db);
        }

        public Task<Result<ContractDto>> NewContractAsync(string number, string counterparty, Guid? supplierId = null, Guid? tenant = null) =>
            Contracts.CreateAsync(new CreateContractRequest(
                tenant ?? Tenant, Project, number, "Road works", counterparty, null, Day, Day, Day.AddDays(100), 1_000_000_000m,
                SupplierId: supplierId), default);

        public async Task<SupplierDto> NewSupplierAsync(string name, Guid? tenant = null) =>
            (await Suppliers.CreateAsync(tenant ?? Tenant, new CreateSupplierRequest(name, SupplierCategory.Contractor), default)).Value!;
    }

    [Theory]
    [InlineData("  Acme  Co ", "acme co")]
    [InlineData("شرکت‌سازه", "شرکت سازه")]
    [InlineData("علي كريمي", "علی کریمی")]
    [InlineData("   ", "")]
    public void NormalizedName_FoldsLookAlikesSpacesAndCase(string typed, string expected) =>
        Assert.Equal(expected, SupplierNames.Normalize(typed));

    [Fact]
    public async Task ASupplier_IsUniqueByNormalisedNameWithinATenantOnly()
    {
        var f = new Fixture();
        await f.NewSupplierAsync("شرکت علي");

        var again = await f.Suppliers.CreateAsync(Tenant, new CreateSupplierRequest("شرکت  علی", SupplierCategory.Supplier), default);
        var elsewhere = await f.Suppliers.CreateAsync(OtherTenant, new CreateSupplierRequest("شرکت علی", SupplierCategory.Supplier), default);

        Assert.True(again.IsFailure);
        Assert.True(elsewhere.IsSuccess);
        Assert.Single((await f.Suppliers.ListAsync(Tenant, default)).Value!);
    }

    [Fact]
    public async Task AContractTypedWithAName_IsLinkedToTheSupplierOfThatName_AndCreatesOneForANewName()
    {
        var f = new Fixture();
        var acme = await f.NewSupplierAsync("Acme");

        var known = (await f.NewContractAsync("1", "  acme ")).Value!;
        var fresh = (await f.NewContractAsync("2", "Brand New Co")).Value!;
        var sameFresh = (await f.NewContractAsync("3", "brand  new co")).Value!;

        Assert.Equal(acme.Id, known.SupplierId);
        Assert.Equal("Acme", known.Counterparty); // the supplier's own spelling
        Assert.NotNull(fresh.SupplierId);
        Assert.Equal(fresh.SupplierId, sameFresh.SupplierId);
        Assert.Equal(2, (await f.Suppliers.ListAsync(Tenant, default)).Value!.Count);
    }

    [Fact]
    public async Task AChosenSupplier_GivesItsNameToTheContract_AndMustExistInTheTenantAndBeActive()
    {
        var f = new Fixture();
        var acme = await f.NewSupplierAsync("Acme");
        var foreign = await f.NewSupplierAsync("Foreign Co", OtherTenant);
        var idle = await f.NewSupplierAsync("Idle Co");
        await f.Suppliers.UpdateAsync(Tenant, idle.Id, new UpdateSupplierRequest("Idle Co", SupplierCategory.Contractor, SupplierStatus.Inactive), default);

        var chosen = await f.NewContractAsync("1", "", acme.Id); // no typed name needed
        var missing = await f.NewContractAsync("2", "x", Guid.NewGuid());
        var otherTenants = await f.NewContractAsync("3", "x", foreign.Id);
        var inactive = await f.NewContractAsync("4", "x", idle.Id);

        Assert.Equal(("Acme", acme.Id), (chosen.Value!.Counterparty, chosen.Value.SupplierId));
        Assert.True(missing.IsFailure);
        Assert.True(otherTenants.IsFailure);
        Assert.True(inactive.IsFailure);
    }

    [Fact]
    public async Task AnUpdate_KeepsFollowsOrUnlinksTheSupplier()
    {
        var f = new Fixture();
        var acme = await f.NewSupplierAsync("Acme");
        var beta = await f.NewSupplierAsync("Beta");
        var contract = (await f.NewContractAsync("1", "Acme")).Value!;

        UpdateContractRequest Update(string counterparty, Guid? supplierId) =>
            new("1", "Road works", counterparty, null, Day, Day, Day.AddDays(100), 1_000_000_000m, SupplierId: supplierId);

        var same = (await f.Contracts.UpdateAsync(contract.Id, Update("Acme", null), default)).Value!;
        Assert.Equal(acme.Id, same.SupplierId);

        var picked = (await f.Contracts.UpdateAsync(contract.Id, Update("ignored", beta.Id), default)).Value!;
        Assert.Equal((beta.Id, "Beta"), (picked.SupplierId, picked.Counterparty));

        var typed = (await f.Contracts.UpdateAsync(contract.Id, Update("Acme", null), default)).Value!;
        Assert.Equal(acme.Id, typed.SupplierId); // follows the typed name

        var unlinked = (await f.Contracts.UpdateAsync(contract.Id, Update("Acme Ltd", Guid.Empty), default)).Value!;
        Assert.Null(unlinked.SupplierId);
        Assert.Equal("Acme Ltd", unlinked.Counterparty);
    }

    [Fact]
    public async Task RenamingASupplier_RenamesItsContractsToo_AndAClashIsRefused()
    {
        var f = new Fixture();
        var acme = await f.NewSupplierAsync("Acme");
        await f.NewSupplierAsync("Beta");
        var contract = (await f.NewContractAsync("1", "Acme")).Value!;

        var clash = await f.Suppliers.UpdateAsync(Tenant, acme.Id, new UpdateSupplierRequest("beta", SupplierCategory.Contractor, SupplierStatus.Active), default);
        var renamed = await f.Suppliers.UpdateAsync(Tenant, acme.Id, new UpdateSupplierRequest("Acme International", SupplierCategory.Contractor, SupplierStatus.Active, NationalId: "10100"), default);

        Assert.True(clash.IsFailure);
        Assert.Equal(("Acme International", "10100"), (renamed.Value!.Name, renamed.Value.NationalId));
        Assert.Equal("Acme International", (await f.Contracts.GetAsync(contract.Id, default)).Value!.Contract.Counterparty);
    }

    [Fact]
    public async Task ASupplierWithContracts_CannotBeDeleted_ButAnUnusedOneCan()
    {
        var f = new Fixture();
        var used = await f.NewSupplierAsync("Used");
        var unused = await f.NewSupplierAsync("Unused");
        await f.NewContractAsync("1", "Used");

        var refused = await f.Suppliers.DeleteAsync(Tenant, used.Id, default);
        var wrongTenant = await f.Suppliers.DeleteAsync(OtherTenant, unused.Id, default);
        var deleted = await f.Suppliers.DeleteAsync(Tenant, unused.Id, default);

        Assert.True(refused.IsFailure);
        Assert.True(wrongTenant.IsFailure);
        Assert.True(deleted.IsSuccess);
        Assert.Single((await f.Suppliers.ListAsync(Tenant, default)).Value!);
    }

    [Fact]
    public async Task TheFigures_CountOnlyApprovedContracts_AndAreKeptPerTenant()
    {
        var f = new Fixture();
        var acme = await f.NewSupplierAsync("Acme");
        var approved = (await f.NewContractAsync("1", "Acme")).Value!;
        await f.NewContractAsync("2", "Acme"); // stays a draft, never submitted
        await f.Contracts.SubmitForApprovalAsync(approved.Id, default); // no workflow: approved at once
        await f.Contracts.ChangeStatusAsync(approved.Id, new ChangeContractStatusRequest(ContractStatus.Active), default);
        var invoice = (await f.Invoices.CreateAsync(approved.Id, new CreateContractInvoiceRequest(Tenant, "1", Day, 300_000_000m, null), default)).Value!;
        await f.Invoices.SubmitForApprovalAsync(approved.Id, invoice.Id, default);

        var detail = (await f.Suppliers.GetAsync(Tenant, acme.Id, default)).Value!;

        Assert.Equal(2, detail.Supplier.Stats.ContractCount);
        Assert.Equal(1, detail.Supplier.Stats.ActiveContractCount);
        Assert.Equal(1_000_000_000m, detail.Supplier.Stats.ContractAmount.Rials);
        Assert.Equal(300_000_000m, detail.Supplier.Stats.InvoicedAmount.Rials);
        Assert.Equal(300_000_000m, detail.Supplier.Stats.OutstandingPayable.Rials);
        Assert.Equal(2, detail.Contracts.Count);
        Assert.True((await f.Suppliers.GetAsync(OtherTenant, acme.Id, default)).IsFailure);
        Assert.Empty((await f.Suppliers.ListAsync(OtherTenant, default)).Value!);
    }

    [Fact]
    public async Task WithoutASupplierStore_AContractKeepsItsTypedNameUnlinked_ExactlyAsBefore()
    {
        var f = new Fixture(withSupplierStore: false);

        var contract = (await f.NewContractAsync("1", "Acme")).Value!;

        Assert.Equal("Acme", contract.Counterparty);
        Assert.Null(contract.SupplierId);
        Assert.Empty(await f.Db.Suppliers.ToListAsync());
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-suppliers.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_contracts", ScriptFile, typeof(ContractsSuppliersSchemaUpgrade), ["Suppliers"],
            ("Contracts", "[SupplierId] uniqueidentifier NULL"));

        var generated = db.Database.GenerateCreateScript();
        var script = SchemaUpgradeVerifier.Normalise(SchemaUpgradeVerifier.ReadScript(ScriptFile));
        Assert.Contains("CREATE INDEX [IX_Contracts_SupplierId] ON [project_contracts].[Contracts] ([SupplierId]);", generated);
        Assert.Contains("CREATE INDEX [IX_Contracts_SupplierId] ON [project_contracts].[Contracts] ([SupplierId]);", script);
    }

    [Fact]
    public void TheUpgradeScript_OnlyAddsAndBackFillsTheSupplierLink()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsSuppliersSchemaUpgrade), allowedUpdateColumn: "SupplierId");
    }
}
