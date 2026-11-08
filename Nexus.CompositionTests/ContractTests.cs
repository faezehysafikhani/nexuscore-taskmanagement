using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.EventHandlers;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class ContractTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 3);

    private sealed class FakeApprovals(ApprovalRequestOutcome outcome) : IApprovalRequester
    {
        public List<ApprovalSubject> Requested { get; } = [];
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken)
        {
            Requested.Add(subject);
            return Task.FromResult(outcome);
        }
    }

    private sealed class Fixture
    {
        public ContractsDbContext Db { get; }
        public FakeApprovals Approvals { get; }
        public ContractService Contracts { get; }
        public ContractAddendumService Addenda { get; }
        public ContractInvoiceService Invoices { get; }
        public ContractRepository Repository { get; }

        public Fixture(ApprovalRequestOutcome outcome = ApprovalRequestOutcome.NotConfigured, string? database = null)
        {
            var options = new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(database ?? Guid.NewGuid().ToString()).Options;
            Db = new ContractsDbContext(options);
            Repository = new ContractRepository(Db);
            Approvals = new FakeApprovals(outcome);
            Contracts = new ContractService(Repository, Db, Approvals);
            Addenda = new ContractAddendumService(Repository, Db, Approvals);
            Invoices = new ContractInvoiceService(Repository, Db, Approvals);
        }

        /// <summary>An approved, running contract worth <paramref name="amount"/> rials.</summary>
        public async Task<Guid> ApprovedContractAsync(decimal amount = 1_000_000_000m, string number = "1405/12")
        {
            var created = await Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, number, "Road works", "Acme", null, Day, Day, Day.AddDays(100), amount), default);
            Assert.True(created.IsSuccess, created.Error.Message);
            Assert.True((await Contracts.SubmitForApprovalAsync(created.Value!.Id, default)).IsSuccess);
            Assert.True((await Contracts.ChangeStatusAsync(created.Value.Id, new ChangeContractStatusRequest(ContractStatus.Active), default)).IsSuccess);
            return created.Value.Id;
        }

        public async Task<Guid> ApprovedAddendumAsync(Guid contractId, decimal change, int days = 0)
        {
            var created = await Addenda.CreateAsync(contractId, new CreateContractAddendumRequest(Tenant, "Addendum", null, Day, change, days), default);
            Assert.True(created.IsSuccess, created.Error.Message);
            Assert.True((await Addenda.SubmitForApprovalAsync(contractId, created.Value!.Id, default)).IsSuccess);
            return created.Value.Id;
        }

        public async Task<Guid> ApprovedInvoiceAsync(Guid contractId, decimal amount, string number = "1")
        {
            var created = await Invoices.CreateAsync(contractId, new CreateContractInvoiceRequest(Tenant, number, Day, amount, null), default);
            Assert.True(created.IsSuccess, created.Error.Message);
            Assert.True((await Invoices.SubmitForApprovalAsync(contractId, created.Value!.Id, default)).IsSuccess);
            return created.Value.Id;
        }
    }

    // ------------------------------------------------------------ contracts

    [Fact]
    public async Task Create_NormalisesTheNumberDigits_AndStartsAsAnUnapprovedDraftWorthNothing()
    {
        var f = new Fixture();

        var created = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, " ۱۴۰۵/۱۲ ", "Road works", "Acme", null, Day, Day, Day.AddDays(30), 500_000_000m), default);

        var dto = created.Value!;
        Assert.Equal("1405/12", dto.ContractNumber);
        Assert.Equal("۱۴۰۵/۱۲", dto.ContractNumberFa);
        Assert.Equal(ContractStatus.Draft, dto.Status);
        Assert.Equal(ApprovalStatus.NotSubmitted, dto.ApprovalStatus);
        Assert.Equal(0m, dto.Summary.CurrentAmount.Rials);
        Assert.Equal(500_000_000m, dto.OriginalAmount.Rials);
        Assert.False(string.IsNullOrEmpty(dto.OriginalAmount.InWords));
        Assert.False(string.IsNullOrEmpty(dto.StartDateFa));
    }

    [Fact]
    public async Task Create_RejectsADuplicateNumberInTheSameProject_EvenInAnotherScript_ButAllowsItInAnotherProject()
    {
        var f = new Fixture();
        await f.ApprovedContractAsync(number: "1405/12");

        var duplicate = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "۱۴۰۵/۱۲", "T", "C", null, null, null, null, 1), default);
        Assert.Equal("conflict", duplicate.Error.Code);

        var otherProject = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Guid.NewGuid(), "1405/12", "T", "C", null, null, null, null, 1), default);
        Assert.True(otherProject.IsSuccess);
    }

    [Theory]
    [InlineData("", "T", "C", 1, "validation.error")]
    [InlineData("1", " ", "C", 1, "validation.error")]
    [InlineData("1", "T", " ", 1, "validation.error")]
    [InlineData("1", "T", "C", -1, "validation.error")]
    public async Task Create_ValidatesTheRequiredFields(string number, string title, string counterparty, decimal amount, string code)
    {
        var f = new Fixture();
        var result = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, number, title, counterparty, null, null, null, null, amount), default);
        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public async Task Create_RejectsAnEndDateBeforeTheStartDate()
    {
        var f = new Fixture();
        var result = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, Day, Day.AddDays(-1), 1), default);
        Assert.Equal("validation.error", result.Error.Code);
    }

    [Fact]
    public async Task AContract_CannotGoLiveBeforeItIsApproved()
    {
        var f = new Fixture();
        var created = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 10), default);

        var attempt = await f.Contracts.ChangeStatusAsync(created.Value!.Id, new ChangeContractStatusRequest(ContractStatus.Active), default);

        Assert.Equal("conflict", attempt.Error.Code);
    }

    [Fact]
    public async Task SubmittingWithWorkflow_LeavesTheContractPending_UntilTheDecisionArrives()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var created = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 10), default);

        var submitted = await f.Contracts.SubmitForApprovalAsync(created.Value!.Id, default);

        Assert.Equal(ApprovalStatus.PendingApproval, submitted.Value!.ApprovalStatus);
        var subject = Assert.Single(f.Approvals.Requested);
        Assert.Equal("Contract", subject.SubjectType);
        Assert.Equal(Project, subject.ScopeId);

        var edit = await f.Contracts.UpdateAsync(created.Value.Id, new UpdateContractRequest("1", "T2", "C", null, null, null, null, 10), default);
        Assert.Equal("conflict", edit.Error.Code);

        await new ContractApprovalGrantedHandler(f.Repository, f.Db).HandleAsync(
            new ApprovalGranted("Contract", created.Value.Id, Tenant, Guid.NewGuid(), null), default);
        Assert.Equal(ApprovalStatus.Approved, (await f.Contracts.GetAsync(created.Value.Id, default)).Value!.Contract.ApprovalStatus);
    }

    [Fact]
    public async Task TheApprovalHandlers_RouteBySubjectType_AndIgnoreOthers()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var contract = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 100), default);
        await f.Contracts.SubmitForApprovalAsync(contract.Value!.Id, default);
        var rejected = new ContractApprovalRejectedHandler(f.Repository, f.Db);

        await rejected.HandleAsync(new ApprovalRejected("Risk", contract.Value.Id, Tenant, Guid.NewGuid(), null), default);
        Assert.Equal(ApprovalStatus.PendingApproval, (await f.Contracts.GetAsync(contract.Value.Id, default)).Value!.Contract.ApprovalStatus);

        await rejected.HandleAsync(new ApprovalRejected("Contract", contract.Value.Id, Tenant, Guid.NewGuid(), "no"), default);
        Assert.Equal(ApprovalStatus.Rejected, (await f.Contracts.GetAsync(contract.Value.Id, default)).Value!.Contract.ApprovalStatus);
    }

    [Fact]
    public async Task AnApprovedContract_KeepsItsSignedAmount_ChangesGoThroughAddenda()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);

        var changed = await f.Contracts.UpdateAsync(id, new UpdateContractRequest("1405/12", "Road works", "Acme", null, Day, Day, Day.AddDays(100), 2_000m), default);
        Assert.Equal("conflict", changed.Error.Code);

        var sameAmount = await f.Contracts.UpdateAsync(id, new UpdateContractRequest("1405/12", "New title", "Acme", null, Day, Day, Day.AddDays(100), 1_000m), default);
        Assert.Equal("New title", sameAmount.Value!.Title);
    }

    [Fact]
    public async Task Delete_IsRefusedForInvoicedContracts_ButCleansUpDraftAddenda()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "Draft addendum", null, null, 10, 0), default);

        Assert.True((await f.Contracts.DeleteAsync(id, default)).IsSuccess);
        Assert.Empty(await f.Db.ContractAddenda.ToListAsync());
        Assert.Empty(await f.Db.Contracts.ToListAsync());

        var invoiced = await f.ApprovedContractAsync(1_000m, "2");
        await f.ApprovedInvoiceAsync(invoiced, 100m);
        Assert.Equal("conflict", (await f.Contracts.DeleteAsync(invoiced, default)).Error.Code);
    }

    [Fact]
    public async Task Delete_IsRefusedWhenAnAddendumIsApproved()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.ApprovedAddendumAsync(id, 100m);

        Assert.Equal("conflict", (await f.Contracts.DeleteAsync(id, default)).Error.Code);
    }

    // ------------------------------------------------------------- addenda

    [Fact]
    public async Task Addenda_AreNumberedFromOne_AndOnlyApprovedOnesChangeTheContract()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var contract = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, Day, Day.AddDays(10), 1_000m), default);
        await new ContractApprovalGrantedHandler(f.Repository, f.Db).HandleAsync(new ApprovalGranted("Contract", contract.Value!.Id, Tenant, Guid.NewGuid(), null), default);
        var id = contract.Value.Id;

        var first = (await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "A1", null, Day, 500m, 5), default)).Value!;
        var second = (await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "A2", null, Day, -200m, 0), default)).Value!;
        Assert.Equal([1, 2], new[] { first.Number, second.Number });

        // Neither is approved yet: the contract is unchanged, and the draft amounts are shown as pending.
        var before = (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary;
        Assert.Equal(1_000m, before.CurrentAmount.Rials);
        Assert.Equal(300m, before.PendingAddendaAmount);

        await f.Addenda.SubmitForApprovalAsync(id, first.Id, default);
        await new ContractApprovalGrantedHandler(f.Repository, f.Db).HandleAsync(new ApprovalGranted("ContractAddendum", first.Id, Tenant, Guid.NewGuid(), null), default);

        var after = (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary;
        Assert.Equal(1_500m, after.CurrentAmount.Rials);
        Assert.Equal(500m, after.ApprovedAddendaAmount);
        Assert.Equal(5, after.ApprovedExtensionDays);
        Assert.Equal(Day.AddDays(15), after.CurrentEndDate);
    }

    [Fact]
    public async Task ARejectedAddendum_DoesNotCount_AndCanBeEditedAgain()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var contract = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 1_000m), default);
        await new ContractApprovalGrantedHandler(f.Repository, f.Db).HandleAsync(new ApprovalGranted("Contract", contract.Value!.Id, Tenant, Guid.NewGuid(), null), default);
        var id = contract.Value.Id;
        var addendum = (await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "A1", null, null, 500m, 0), default)).Value!;
        await f.Addenda.SubmitForApprovalAsync(id, addendum.Id, default);
        await new ContractApprovalRejectedHandler(f.Repository, f.Db).HandleAsync(new ApprovalRejected("ContractAddendum", addendum.Id, Tenant, Guid.NewGuid(), "no"), default);

        Assert.Equal(1_000m, (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary.CurrentAmount.Rials);
        Assert.Equal(0m, (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary.PendingAddendaAmount);

        var edited = await f.Addenda.UpdateAsync(id, addendum.Id, new UpdateContractAddendumRequest("A1 v2", null, null, 300m, 0), default);
        Assert.Equal(300m, edited.Value!.AmountChange);
    }

    [Fact]
    public async Task Addenda_RequireAnApprovedContract_AndSomethingToChange()
    {
        var f = new Fixture();
        var draft = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 100), default);
        var onDraft = await f.Addenda.CreateAsync(draft.Value!.Id, new CreateContractAddendumRequest(Tenant, "A", null, null, 10, 0), default);
        Assert.Equal("conflict", onDraft.Error.Code);

        var id = await f.ApprovedContractAsync(100m, "2");
        var nothing = await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "A", null, null, 0, 0), default);
        Assert.Equal("validation.error", nothing.Error.Code);

        await f.Contracts.ChangeStatusAsync(id, new ChangeContractStatusRequest(ContractStatus.Terminated), default);
        var terminated = await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "A", null, null, 10, 0), default);
        Assert.Equal("conflict", terminated.Error.Code);
    }

    [Fact]
    public async Task AReduction_CannotTakeTheContractBelowWhatIsInvoiced()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.ApprovedInvoiceAsync(id, 800m);

        var addendum = (await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "Cut", null, null, -300m, 0), default)).Value!;
        var submit = await f.Addenda.SubmitForApprovalAsync(id, addendum.Id, default);

        Assert.Equal("conflict", submit.Error.Code);
        Assert.Equal(1_000m, (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary.CurrentAmount.Rials);

        var smaller = (await f.Addenda.CreateAsync(id, new CreateContractAddendumRequest(Tenant, "Small cut", null, null, -200m, 0), default)).Value!;
        Assert.True((await f.Addenda.SubmitForApprovalAsync(id, smaller.Id, default)).IsSuccess);
        Assert.Equal(800m, (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary.CurrentAmount.Rials);
    }

    [Fact]
    public async Task AnApprovedAddendum_CannotBeEditedOrDeleted_AndBelongsToItsContract()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        var addendumId = await f.ApprovedAddendumAsync(id, 100m);

        Assert.Equal("conflict", (await f.Addenda.UpdateAsync(id, addendumId, new UpdateContractAddendumRequest("x", null, null, 1, 0), default)).Error.Code);
        Assert.Equal("conflict", (await f.Addenda.DeleteAsync(id, addendumId, default)).Error.Code);

        var other = await f.ApprovedContractAsync(1_000m, "2");
        Assert.Equal("not_found", (await f.Addenda.DeleteAsync(other, addendumId, default)).Error.Code);
    }

    // ------------------------------------------------------------- invoices

    [Fact]
    public async Task Invoices_AreCountedWhenApproved_AndReservedWhileOpen()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.ApprovedInvoiceAsync(id, 400m, "1");
        var draft = (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "2", Day, 300m, null), default)).Value!;

        var summary = (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary;
        Assert.Equal(400m, summary.InvoicedAmount.Rials);
        Assert.Equal(700m, summary.ReservedAmount);
        Assert.Equal(600m, summary.RemainingCommitment.Rials);
        Assert.Equal(40m, summary.InvoicedPercent);
        Assert.Equal(ApprovalStatus.NotSubmitted, draft.ApprovalStatus);
    }

    [Fact]
    public async Task AnInvoice_CannotTakeTheTotalAboveTheContract_CountingEveryOpenInvoice()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 700m, null), default);

        var tooMuch = await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "2", Day, 301m, null), default);
        Assert.Equal("conflict", tooMuch.Error.Code);

        Assert.True((await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "2", Day, 300m, null), default)).IsSuccess);
    }

    [Fact]
    public async Task AnApprovedAddendum_RaisesTheCeilingForInvoices()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        await f.ApprovedAddendumAsync(id, 500m);

        Assert.True((await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 1_500m, null), default)).IsSuccess);
    }

    [Fact]
    public async Task Invoices_NeedAnApprovedContract_ARealNumber_AndAPositiveAmount()
    {
        var f = new Fixture();
        var draft = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 100), default);
        Assert.Equal("conflict", (await f.Invoices.CreateAsync(draft.Value!.Id, new CreateContractInvoiceRequest(Tenant, "1", Day, 10, null), default)).Error.Code);

        var id = await f.ApprovedContractAsync(100m, "2");
        Assert.Equal("validation.error", (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, " ", Day, 10, null), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 0, null), default)).Error.Code);
    }

    [Fact]
    public async Task InvoiceNumbers_AreUniquePerContract_AcrossScripts_NotAcrossContracts()
    {
        var f = new Fixture();
        var first = await f.ApprovedContractAsync(1_000m, "A");
        var second = await f.ApprovedContractAsync(1_000m, "B");
        await f.Invoices.CreateAsync(first, new CreateContractInvoiceRequest(Tenant, "12", Day, 10, null), default);

        Assert.Equal("conflict", (await f.Invoices.CreateAsync(first, new CreateContractInvoiceRequest(Tenant, "۱۲", Day, 10, null), default)).Error.Code);
        Assert.True((await f.Invoices.CreateAsync(second, new CreateContractInvoiceRequest(Tenant, "12", Day, 10, null), default)).IsSuccess);
    }

    [Fact]
    public async Task Update_RechecksTheCeilingWithoutCountingTheInvoiceItself()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        var invoice = (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 900m, null), default)).Value!;

        Assert.True((await f.Invoices.UpdateAsync(id, invoice.Id, new UpdateContractInvoiceRequest("1", Day, 1_000m, "full"), default)).IsSuccess);
        Assert.Equal("conflict", (await f.Invoices.UpdateAsync(id, invoice.Id, new UpdateContractInvoiceRequest("1", Day, 1_001m, null), default)).Error.Code);
    }

    [Fact]
    public async Task Payments_AreRecordedAsARunningTotal_OnlyAgainstApprovedInvoices()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        var open = (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 400m, null), default)).Value!;
        Assert.Equal("conflict", (await f.Invoices.RecordPaymentAsync(id, open.Id, new RecordInvoicePaymentRequest(100m, Day), default)).Error.Code);

        var invoiceId = await f.ApprovedInvoiceAsync(id, 600m, "2");
        var paid = await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(250m, Day.AddDays(3)), default);
        Assert.Equal(250m, paid.Value!.PaidAmount.Rials);
        Assert.Equal(350m, paid.Value.UnpaidAmount.Rials);
        Assert.Equal(Day.AddDays(3), paid.Value.LastPaymentDate);

        var summary = (await f.Contracts.GetAsync(id, default)).Value!.Contract.Summary;
        Assert.Equal(250m, summary.PaidAmount.Rials);
        Assert.Equal(350m, summary.OutstandingPayable.Rials);
    }

    [Fact]
    public async Task Payments_AreValidated_AndClearingThemResetsTheDate()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        var invoiceId = await f.ApprovedInvoiceAsync(id, 500m);

        Assert.Equal("validation.error", (await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(501m, Day), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(-1m, Day), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(10m, null), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(10m, Day.AddDays(-1)), default)).Error.Code);

        await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(500m, Day), default);
        var cleared = await f.Invoices.RecordPaymentAsync(id, invoiceId, new RecordInvoicePaymentRequest(0m, null), default);
        Assert.Equal(0m, cleared.Value!.PaidAmount.Rials);
        Assert.Null(cleared.Value.LastPaymentDate);
    }

    [Fact]
    public async Task APaidOrApprovedInvoice_CannotBeDeleted_AnOpenOneCan()
    {
        var f = new Fixture();
        var id = await f.ApprovedContractAsync(1_000m);
        var approved = await f.ApprovedInvoiceAsync(id, 100m, "1");
        Assert.Equal("conflict", (await f.Invoices.DeleteAsync(id, approved, default)).Error.Code);

        var open = (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "2", Day, 100m, null), default)).Value!;
        Assert.True((await f.Invoices.DeleteAsync(id, open.Id, default)).IsSuccess);
        Assert.Equal(1, await f.Db.ContractInvoices.CountAsync());
    }

    [Fact]
    public async Task ARejectedInvoice_ReleasesItsReservation()
    {
        var f = new Fixture(ApprovalRequestOutcome.Submitted);
        var contract = await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "1", "T", "C", null, null, null, null, 1_000m), default);
        await new ContractApprovalGrantedHandler(f.Repository, f.Db).HandleAsync(new ApprovalGranted("Contract", contract.Value!.Id, Tenant, Guid.NewGuid(), null), default);
        var id = contract.Value.Id;
        var invoice = (await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "1", Day, 900m, null), default)).Value!;
        await f.Invoices.SubmitForApprovalAsync(id, invoice.Id, default);
        await new ContractApprovalRejectedHandler(f.Repository, f.Db).HandleAsync(new ApprovalRejected("ContractInvoice", invoice.Id, Tenant, Guid.NewGuid(), "no"), default);

        Assert.True((await f.Invoices.CreateAsync(id, new CreateContractInvoiceRequest(Tenant, "2", Day, 1_000m, null), default)).IsSuccess);
    }

    // -------------------------------------------------------------- summary

    [Fact]
    public async Task TheProjectSummary_AddsUpApprovedContractsOnly_AndFlagsOverInvoicing()
    {
        var f = new Fixture();
        var a = await f.ApprovedContractAsync(1_000m, "A");
        var b = await f.ApprovedContractAsync(2_000m, "B");
        await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Project, "C", "Draft", "X", null, null, null, null, 9_999m), default);
        var invoiceId = await f.ApprovedInvoiceAsync(a, 500m);
        await f.Invoices.RecordPaymentAsync(a, invoiceId, new RecordInvoicePaymentRequest(200m, Day), default);
        await f.ApprovedInvoiceAsync(b, 1_000m);

        var summary = (await f.Contracts.GetProjectSummaryAsync(Project, default)).Value!;

        Assert.Equal(3, summary.ContractCount);
        Assert.Equal(3_000m, summary.TotalContractAmount.Rials);
        Assert.Equal(1_500m, summary.TotalInvoiced.Rials);
        Assert.Equal(200m, summary.TotalPaid.Rials);
        Assert.Equal(1_500m, summary.TotalRemainingCommitment.Rials);
        Assert.Equal(1_300m, summary.TotalOutstandingPayable.Rials);
        Assert.Equal(50m, summary.InvoicedPercent);
        Assert.Equal(0, summary.OverInvoicedContracts);
    }

    [Fact]
    public void TheCalculator_FlagsAContractThatIsInvoicedAboveItsValue()
    {
        // Possible when an addendum is approved after invoices (a reduction that slipped past the
        // submit-time check because it was approved later): it must be visible, not hidden.
        var contract = new Contract(Guid.NewGuid(), Tenant, Project, "1", "T", "C", 1_000m);
        contract.Approve();
        var addendum = new ContractAddendum(Guid.NewGuid(), Tenant, contract.Id, 1, "Cut", null, null, -400m, 0);
        addendum.Approve();
        var invoice = new ContractInvoice(Guid.NewGuid(), Tenant, contract.Id, "1", Day, 800m, null);
        invoice.Approve();

        var summary = ContractCalculator.Summarize(contract, [addendum], [invoice]);

        Assert.True(summary.IsOverInvoiced);
        Assert.Equal(-200m, summary.RemainingCommitment);
    }

    [Fact]
    public async Task TheList_IsScopedToTheProject_AndSortedByNumber()
    {
        var f = new Fixture();
        await f.ApprovedContractAsync(1m, "B");
        await f.ApprovedContractAsync(1m, "A");
        await f.Contracts.CreateAsync(new CreateContractRequest(Tenant, Guid.NewGuid(), "Z", "T", "C", null, null, null, null, 1), default);

        var list = (await f.Contracts.ListByProjectAsync(Project, default)).Value!;

        Assert.Equal(["A", "B"], list.Select(c => c.ContractNumber));
    }

    [Fact]
    public async Task TheEfRepository_EnforcesTheUniqueNumbersAndFiltersByContract()
    {
        var f = new Fixture();
        var first = await f.ApprovedContractAsync(1_000m, "A");
        var second = await f.ApprovedContractAsync(1_000m, "B");
        await f.ApprovedInvoiceAsync(first, 10m, "1");
        await f.ApprovedInvoiceAsync(second, 20m, "1");

        Assert.Equal([10m], (await f.Repository.ListInvoicesAsync(first, default)).Select(i => i.Amount));
        Assert.Equal(2, (await f.Repository.ListInvoicesAsync([first, second], default)).Count);
        Assert.True(await f.Repository.InvoiceNumberExistsAsync(first, "1", null, default));
        var existing = (await f.Repository.ListInvoicesAsync(first, default)).Single();
        Assert.False(await f.Repository.InvoiceNumberExistsAsync(first, "1", existing.Id, default));
    }

    [Fact]
    public async Task Everything_ReadsBackAfterAFreshContext()
    {
        var database = Guid.NewGuid().ToString();
        Guid id;
        {
            var first = new Fixture(database: database);
            id = await first.ApprovedContractAsync(1_000m);
            await first.ApprovedAddendumAsync(id, 250m, 7);
            await first.ApprovedInvoiceAsync(id, 300m);
        }

        var second = new Fixture(database: database);
        var detail = (await second.Contracts.GetAsync(id, default)).Value!;
        Assert.Equal(1_250m, detail.Contract.Summary.CurrentAmount.Rials);
        Assert.Single(detail.Addenda);
        Assert.Single(detail.Invoices);
        Assert.Equal(Day.AddDays(107), detail.Contract.Summary.CurrentEndDate);
    }
}
