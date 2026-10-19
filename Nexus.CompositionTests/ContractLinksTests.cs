using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Links from a contract, its milestones and its obligations to documents and actions.</summary>
public sealed class ContractLinksTests
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
        public ContractsDbContext Db { get; }
        public ContractService Contracts { get; }
        public ContractCommitmentService Commitments { get; }
        public ContractLinkService Links { get; }

        public Fixture()
        {
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(Db);
            var commitmentRepository = new CommitmentRepository(Db);
            Contracts = new ContractService(repository, Db, new NotConfigured(), new SupplierRepository(Db), commitmentRepository);
            Commitments = new ContractCommitmentService(repository, commitmentRepository, Db);
            Links = new ContractLinkService(repository, commitmentRepository, Db);
        }

        public async Task<ContractDto> ContractAsync(string number = "1") =>
            (await Contracts.CreateAsync(new CreateContractRequest(
                Tenant, Project, number, "Road works", "Acme", null, Day, Day, Day.AddDays(100), 1_000_000_000m), default)).Value!;
    }

    [Fact]
    public async Task ALink_CanHangOnTheContract_AMilestone_OrAnObligation_AndIsListedWithItsTitle()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();
        var milestone = (await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("Foundations", Day), default)).Value!;
        var obligation = (await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("Insure the site"), default)).Value!;
        var doc = Guid.NewGuid();
        var action = Guid.NewGuid();

        var onContract = await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "  Signed contract.pdf "), default);
        var onMilestone = await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "Inspection report", ContractLinkSubject.Milestone, milestone.Id, " accepted "), default);
        var onObligation = await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Action, action, "Renew the policy", ContractLinkSubject.Obligation, obligation.Id), default);

        Assert.True(onContract.IsSuccess && onMilestone.IsSuccess && onObligation.IsSuccess);
        Assert.Equal(("Signed contract.pdf", contract.Id), (onContract.Value!.Title, onContract.Value.SubjectId));
        Assert.Equal("accepted", onMilestone.Value!.Note);
        Assert.Equal(3, (await f.Links.ListAsync(contract.Id, default)).Value!.Count);
    }

    [Fact]
    public async Task ALink_IsRefused_WhenItsSubjectBelongsToAnotherContract_ItsTitleIsMissing_OrItIsADuplicate()
    {
        var f = new Fixture();
        var one = await f.ContractAsync("1");
        var two = await f.ContractAsync("2");
        var foreign = (await f.Commitments.CreateMilestoneAsync(two.Id, new SaveContractMilestoneRequest("Other", Day), default)).Value!;
        var doc = Guid.NewGuid();

        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "x", ContractLinkSubject.Milestone, foreign.Id), default)).IsFailure);
        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "x", ContractLinkSubject.Milestone, null), default)).IsFailure);
        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "  "), default)).IsFailure);
        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, Guid.Empty, "x"), default)).IsFailure);
        Assert.True((await f.Links.CreateAsync(Guid.NewGuid(), new CreateContractLinkRequest(ContractLinkKind.Document, doc, "x"), default)).IsFailure);

        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "x"), default)).IsSuccess);
        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, doc, "again"), default)).IsFailure);
        Assert.True((await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Action, doc, "same id, other kind"), default)).IsSuccess);
    }

    [Fact]
    public async Task ALink_IsDeletedByItsOwnContractOnly()
    {
        var f = new Fixture();
        var one = await f.ContractAsync("1");
        var two = await f.ContractAsync("2");
        var link = (await f.Links.CreateAsync(one.Id, new CreateContractLinkRequest(ContractLinkKind.Document, Guid.NewGuid(), "x"), default)).Value!;

        Assert.True((await f.Links.DeleteAsync(two.Id, link.Id, default)).IsFailure);
        Assert.True((await f.Links.DeleteAsync(one.Id, link.Id, default)).IsSuccess);
        Assert.Empty((await f.Links.ListAsync(one.Id, default)).Value!);
    }

    [Fact]
    public async Task DeletingAMilestoneOrObligation_TakesItsLinks_AndDeletingTheContractTakesAll()
    {
        var f = new Fixture();
        var contract = await f.ContractAsync();
        var milestone = (await f.Commitments.CreateMilestoneAsync(contract.Id, new SaveContractMilestoneRequest("M", Day), default)).Value!;
        var obligation = (await f.Commitments.CreateObligationAsync(contract.Id, new SaveContractObligationRequest("O"), default)).Value!;
        await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Document, Guid.NewGuid(), "on contract"), default);
        await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Document, Guid.NewGuid(), "on milestone", ContractLinkSubject.Milestone, milestone.Id), default);
        await f.Links.CreateAsync(contract.Id, new CreateContractLinkRequest(ContractLinkKind.Action, Guid.NewGuid(), "on obligation", ContractLinkSubject.Obligation, obligation.Id), default);

        await f.Commitments.DeleteMilestoneAsync(contract.Id, milestone.Id, default);
        Assert.Equal(["on contract", "on obligation"], (await f.Links.ListAsync(contract.Id, default)).Value!.Select(l => l.Title).OrderBy(t => t));

        await f.Commitments.DeleteObligationAsync(contract.Id, obligation.Id, default);
        Assert.Equal(["on contract"], (await f.Links.ListAsync(contract.Id, default)).Value!.Select(l => l.Title));

        Assert.True((await f.Contracts.DeleteAsync(contract.Id, default)).IsSuccess);
        Assert.Empty(await f.Db.ContractLinks.ToListAsync());
    }

    // ---- the upgrade script and its code twin ----

    private const string ScriptFile = "2026-10-19-add-contract-links.sql";

    private static ContractsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ContractsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(db, "project_contracts", ScriptFile, typeof(ContractsLinksSchemaUpgrade), ["ContractLinks"]);
    }

    [Fact]
    public void TheUpgradeScript_OnlyCreates()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(ContractsLinksSchemaUpgrade));
    }
}
