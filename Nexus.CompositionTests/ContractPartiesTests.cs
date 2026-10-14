using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>The supervisor / beneficiary of a contract, the period of an addendum, the title and billing period of an invoice.</summary>
public sealed class ContractPartiesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Fixture
    {
        public ContractService Contracts { get; }
        public ContractAddendumService Addenda { get; }
        public ContractInvoiceService Invoices { get; }

        public Fixture()
        {
            var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(db);
            Contracts = new ContractService(repository, db, new NotConfigured());
            Addenda = new ContractAddendumService(repository, db, new NotConfigured());
            Invoices = new ContractInvoiceService(repository, db, new NotConfigured());
        }

        public async Task<ContractDto> ContractAsync(string? supervisor = null, string? beneficiary = null)
        {
            var created = (await Contracts.CreateAsync(new CreateContractRequest(
                Tenant, Project, "1405/12", "Road works", "Acme", null, Day, Day, Day.AddDays(100), 1_000_000_000m, supervisor, beneficiary), default)).Value!;
            await Contracts.SubmitForApprovalAsync(created.Id, default); // no workflow: approved at once, so addenda and invoices can be added
            await Contracts.ChangeStatusAsync(created.Id, new ChangeContractStatusRequest(ContractStatus.Active), default);
            return created;
        }
    }

    [Fact]
    public async Task AContract_KeepsItsSupervisorAndBeneficiary_AndAnUpdateThatOmitsThemLeavesThemAlone()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("Consultant Co", "Roads Dept");

        Assert.Equal(("Consultant Co", "Roads Dept"), (contract.SupervisorName, contract.BeneficiaryName));

        UpdateContractRequest Update(string? supervisor, string? beneficiary) =>
            new("1405/12", "Road works", "Acme", null, Day, Day, Day.AddDays(100), 1_000_000_000m, supervisor, beneficiary);

        var kept = (await f.Contracts.UpdateAsync(contract.Id, Update(null, null), default)).Value!;
        Assert.Equal(("Consultant Co", "Roads Dept"), (kept.SupervisorName, kept.BeneficiaryName));

        var half = (await f.Contracts.UpdateAsync(contract.Id, Update("New Consultant", null), default)).Value!;
        Assert.Equal(("New Consultant", "Roads Dept"), (half.SupervisorName, half.BeneficiaryName));

        var cleared = (await f.Contracts.UpdateAsync(contract.Id, Update("", "  "), default)).Value!;
        Assert.Null(cleared.SupervisorName);
        Assert.Null(cleared.BeneficiaryName);
    }

    [Fact]
    public async Task AContractWithoutThem_ReadsBackWithNone_ExactlyAsBefore()
    {
        var f = new Fixture();

        var contract = await f.ContractAsync();

        Assert.Null(contract.SupervisorName);
        Assert.Null(contract.BeneficiaryName);
    }

    [Fact]
    public async Task AnAddendum_CarriesItsPeriod_WithTheJalaliDates_AndAnUpdateThatOmitsItKeepsIt()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();

        var addendum = (await f.Addenda.CreateAsync(contract.Id, new CreateContractAddendumRequest(
            Tenant, "Addendum 1", null, Day, 50_000_000m, 0, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)), default)).Value!;

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)), (addendum.StartDate, addendum.EndDate));
        Assert.False(string.IsNullOrEmpty(addendum.StartDateFa));
        Assert.False(string.IsNullOrEmpty(addendum.EndDateFa));

        var kept = (await f.Addenda.UpdateAsync(contract.Id, addendum.Id, new UpdateContractAddendumRequest("Renamed", null, Day, 60_000_000m, 0), default)).Value!;
        Assert.Equal("Renamed", kept.Title);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)), (kept.StartDate, kept.EndDate));

        var moved = (await f.Addenda.UpdateAsync(contract.Id, addendum.Id, new UpdateContractAddendumRequest("Renamed", null, Day, 60_000_000m, 0, new DateOnly(2026, 11, 5), null), default)).Value!;
        Assert.Equal((new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 1)), (moved.StartDate, moved.EndDate));
    }

    [Fact]
    public async Task AnInvoice_CarriesATitleAndABillingPeriod_AndAnUpdateThatOmitsThemKeepsThem()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();

        var invoice = (await f.Invoices.CreateAsync(contract.Id, new CreateContractInvoiceRequest(
            Tenant, "1", Day, 10_000_000m, null, "First statement", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), default)).Value!;

        Assert.Equal("First statement", invoice.Title);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (invoice.PeriodStartDate, invoice.PeriodEndDate));
        Assert.False(string.IsNullOrEmpty(invoice.PeriodStartDateFa));

        var kept = (await f.Invoices.UpdateAsync(contract.Id, invoice.Id, new UpdateContractInvoiceRequest("1", Day, 12_000_000m, "note"), default)).Value!;
        Assert.Equal("First statement", kept.Title);
        Assert.Equal(new DateOnly(2026, 10, 31), kept.PeriodEndDate);
        Assert.Equal("note", kept.Description);
    }

    [Fact]
    public async Task AnInvoiceWithoutThem_ReadsBackWithNone()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();

        var invoice = (await f.Invoices.CreateAsync(contract.Id, new CreateContractInvoiceRequest(Tenant, "1", Day, 1_000m, null), default)).Value!;

        Assert.Null(invoice.Title);
        Assert.Null(invoice.PeriodStartDate);
        Assert.Null(invoice.PeriodStartDateFa);
    }

    [Fact]
    public async Task TheBatchList_ReturnsEveryContractOfTheAskedProjects_WithItsAddendaAndInvoices()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync("Consultant Co", "Roads Dept");
        await f.Addenda.CreateAsync(contract.Id, new CreateContractAddendumRequest(Tenant, "A1", null, Day, 5_000_000m, 0), default);
        await f.Invoices.CreateAsync(contract.Id, new CreateContractInvoiceRequest(Tenant, "1", Day, 1_000_000m, null, "First"), default);

        var mine = (await f.Contracts.ListForProjectsAsync(new ListContractsForProjectsRequest([Project]), default)).Value!;
        var other = (await f.Contracts.ListForProjectsAsync(new ListContractsForProjectsRequest([Guid.NewGuid()]), default)).Value!;
        var none = (await f.Contracts.ListForProjectsAsync(new ListContractsForProjectsRequest([]), default)).Value!;

        var detail = Assert.Single(mine);
        Assert.Equal("Consultant Co", detail.Contract.SupervisorName);
        Assert.Single(detail.Addenda);
        Assert.Equal("First", Assert.Single(detail.Invoices).Title);
        Assert.Empty(other);
        Assert.Empty(none);
    }

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel_AndOnlyAdd()
    {
        using var db = new ContractsDbContext(
            new DbContextOptionsBuilder<ContractsDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_contracts", "2026-10-14-add-contract-parties-and-billing.sql", typeof(ContractsPartiesSchemaUpgrade), [],
            ("Contracts", "[SupervisorName] nvarchar(300) NULL"),
            ("Contracts", "[BeneficiaryName] nvarchar(300) NULL"),
            ("ContractAddenda", "[StartDate] date NULL"),
            ("ContractAddenda", "[EndDate] date NULL"),
            ("ContractInvoices", "[Title] nvarchar(300) NULL"),
            ("ContractInvoices", "[PeriodStartDate] date NULL"),
            ("ContractInvoices", "[PeriodEndDate] date NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-14-add-contract-parties-and-billing.sql", typeof(ContractsPartiesSchemaUpgrade));
    }
}
