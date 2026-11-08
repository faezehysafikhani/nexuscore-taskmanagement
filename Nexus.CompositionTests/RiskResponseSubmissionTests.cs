using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Application.EventHandlers;
using Nexus.ProjectManagement.RiskManagement.Domain;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class RiskResponseSubmissionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Submitting : IApprovalRequester
    {
        public ApprovalSubject? Subject { get; private set; }
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct)
        {
            Subject = subject;
            return Task.FromResult(ApprovalRequestOutcome.Submitted);
        }
    }

    private sealed class Fixture
    {
        public RiskManagementDbContext Db { get; } = new(new DbContextOptionsBuilder<RiskManagementDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public RiskService Risks { get; }
        public RiskResponseSubmissionService Service { get; }
        public RiskResponseSubmissionRepository Repository { get; }
        public Guid ProjectId { get; } = Guid.NewGuid();

        public Fixture(IApprovalRequester? responseApprovals = null)
        {
            Risks = new RiskService(new RiskRepository(Db), Db, new NotConfigured(), new RiskSettingsRepository(Db));
            Repository = new RiskResponseSubmissionRepository(Db);
            Service = new RiskResponseSubmissionService(Repository, new RiskRepository(Db), Risks, Db, responseApprovals ?? new NotConfigured());
        }

        /// <summary>An approved risk (no approval backend: submitting approves it at once).</summary>
        public async Task<Guid> ApprovedRiskAsync()
        {
            var risk = (await Risks.CreateAsync(new CreateRiskRequest(Tenant, ProjectId, "Supplier delay", 3, 3, 3, "Dual sourcing", null), default)).Value!;
            await Risks.SubmitForApprovalAsync(risk.Id, default);
            return risk.Id;
        }

        public async Task<RiskResponseStatus> StatusOfAsync(Guid riskId) => (await Risks.GetAsync(riskId, default)).Value!.ResponseStatus;
    }

    [Fact]
    public async Task WithoutAnApprovalBackend_TheResponseStatusChangesAtOnce()
    {
        var f = new Fixture();
        var risk = await f.ApprovedRiskAsync();

        var result = await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, risk, RiskResponseStatus.InProgress, "started"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalStatus.Approved, result.Value!.ApprovalStatus);
        Assert.Equal(RiskResponseStatus.Planning, result.Value.PreviousStatus);
        Assert.Equal(RiskResponseStatus.InProgress, await f.StatusOfAsync(risk));
    }

    [Fact]
    public async Task WithAWorkflow_TheRiskKeepsItsStatus_UntilTheReportIsVerified()
    {
        var requester = new Submitting();
        var f = new Fixture(requester);
        var risk = await f.ApprovedRiskAsync();

        var report = (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, risk, RiskResponseStatus.Completed, null), default)).Value!;

        Assert.Equal(ApprovalStatus.PendingApproval, report.ApprovalStatus);
        Assert.Equal(RiskResponseStatus.Planning, await f.StatusOfAsync(risk));
        Assert.Equal("RiskResponseStatus", requester.Subject!.SubjectType);
        Assert.Equal(f.ProjectId, requester.Subject.ScopeId);

        var second = await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, risk, RiskResponseStatus.OnHold, null), default);
        Assert.Equal("conflict", second.Error.Code);

        await new RiskResponseApprovalGrantedHandler(f.Repository, f.Risks, f.Db)
            .HandleAsync(new ApprovalGranted("RiskResponseStatus", report.Id, Tenant, null, null), default);

        Assert.Equal(RiskResponseStatus.Completed, await f.StatusOfAsync(risk));
        Assert.Equal(ApprovalStatus.Approved, (await f.Repository.GetByIdAsync(report.Id, default))!.ApprovalStatus);
    }

    [Fact]
    public async Task ARejectedReport_LeavesTheStatus_AndOtherSubjectsAreIgnored()
    {
        var f = new Fixture(new Submitting());
        var risk = await f.ApprovedRiskAsync();
        var report = (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, risk, RiskResponseStatus.Completed, null), default)).Value!;

        await new RiskResponseApprovalGrantedHandler(f.Repository, f.Risks, f.Db)
            .HandleAsync(new ApprovalGranted("Risk", report.Id, Tenant, null, null), default);
        Assert.Equal(RiskResponseStatus.Planning, await f.StatusOfAsync(risk));

        await new RiskResponseApprovalRejectedHandler(f.Repository, f.Db)
            .HandleAsync(new ApprovalRejected("RiskResponseStatus", report.Id, Tenant, null, null), default);
        Assert.Equal(RiskResponseStatus.Planning, await f.StatusOfAsync(risk));
        Assert.Equal(ApprovalStatus.Rejected, (await f.Repository.GetByIdAsync(report.Id, default))!.ApprovalStatus);
    }

    [Fact]
    public async Task OnlyAnApprovedRisk_AndADifferentStatus_CanBeReported()
    {
        var f = new Fixture();
        var draft = (await f.Risks.CreateAsync(new CreateRiskRequest(Tenant, f.ProjectId, "Draft", 3, 3, 3, null, null), default)).Value!.Id;
        var approved = await f.ApprovedRiskAsync();

        Assert.Equal("conflict", (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, draft, RiskResponseStatus.InProgress, null), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, approved, RiskResponseStatus.Planning, null), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, approved, (RiskResponseStatus)9, null), default)).Error.Code);
        Assert.Equal("not_found", (await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, Guid.NewGuid(), RiskResponseStatus.InProgress, null), default)).Error.Code);
    }

    [Fact]
    public async Task TheBatchList_ReturnsTheReportsOfTheAskedProjectsOnly()
    {
        var f = new Fixture(new Submitting());
        var risk = await f.ApprovedRiskAsync();
        await f.Service.SubmitAsync(new SubmitRiskResponseRequest(Tenant, risk, RiskResponseStatus.InProgress, "go"), default);

        var mine = (await f.Service.ListForProjectsAsync(new ListRisksForProjectsRequest([f.ProjectId]), default)).Value!;
        var other = (await f.Service.ListForProjectsAsync(new ListRisksForProjectsRequest([Guid.NewGuid()]), default)).Value!;
        var none = (await f.Service.ListForProjectsAsync(new ListRisksForProjectsRequest([]), default)).Value!;

        Assert.Single(mine);
        Assert.Equal("go", mine[0].Comment);
        Assert.Empty(other);
        Assert.Empty(none);
    }

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel_AndOnlyAdd()
    {
        using var db = new RiskManagementDbContext(
            new DbContextOptionsBuilder<RiskManagementDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "risk_management", "2026-10-14-add-risk-response-submissions.sql", typeof(RiskResponseSchemaUpgrade), ["RiskResponseSubmissions"]);
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-14-add-risk-response-submissions.sql", typeof(RiskResponseSchemaUpgrade));
    }
}
