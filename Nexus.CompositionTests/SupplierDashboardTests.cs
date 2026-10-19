using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>The supplier dashboard: figures per supplier across projects, totals, and the reasons for attention.</summary>
public sealed class SupplierDashboardTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly DateOnly Day = new(2026, 9, 1);

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private sealed class NotConfigured : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private sealed class Fixture
    {
        public ContractService Contracts { get; }
        public ContractCommitmentService Commitments { get; }
        public SupplierEvaluationService Evaluations { get; }
        public SupplierDashboardService Dashboard { get; }

        public Fixture()
        {
            var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var repository = new ContractRepository(db);
            var supplierRepository = new SupplierRepository(db);
            var commitmentRepository = new CommitmentRepository(db);
            var clock = new FixedClock(Today);
            Contracts = new ContractService(repository, db, new NotConfigured(), supplierRepository, commitmentRepository);
            Commitments = new ContractCommitmentService(repository, commitmentRepository, db, clock);
            Evaluations = new SupplierEvaluationService(supplierRepository, commitmentRepository, db, clock);
            Dashboard = new SupplierDashboardService(supplierRepository, repository, commitmentRepository, clock);
        }

        /// <summary>A contract with the supplier; approved and made active when asked.</summary>
        public async Task<ContractDto> ContractAsync(string number, string supplier, Guid project, decimal amount, bool active, Guid? tenant = null)
        {
            var created = (await Contracts.CreateAsync(new CreateContractRequest(
                tenant ?? Tenant, project, number, "Works", supplier, null, Day, Day, Day.AddDays(200), amount), default)).Value!;
            if (active)
            {
                await Contracts.SubmitForApprovalAsync(created.Id, default); // no workflow: approved at once
                await Contracts.ChangeStatusAsync(created.Id, new ChangeContractStatusRequest(ContractStatus.Active), default);
            }

            return created;
        }
    }

    [Fact]
    public async Task EachSupplier_ShowsItsFiguresAcrossProjects_AndWhyItNeedsAttention()
    {
        var f = new Fixture();
        // Acme: two active contracts in two projects, one overdue milestone and one breached obligation, never evaluated.
        var a1 = await f.ContractAsync("A1", "Acme", ProjectA, 3_000_000m, active: true);
        await f.ContractAsync("A2", "Acme", ProjectB, 2_000_000m, active: true);
        await f.Commitments.CreateMilestoneAsync(a1.Id, new SaveContractMilestoneRequest("Late", new DateOnly(2026, 10, 1)), default);
        await f.Commitments.CreateMilestoneAsync(a1.Id, new SaveContractMilestoneRequest("Future", new DateOnly(2026, 12, 1)), default);
        await f.Commitments.CreateObligationAsync(a1.Id, new SaveContractObligationRequest("Broken", Status: ObligationStatus.Breached), default);
        // Beta: one active contract, evaluated low.
        var b1 = await f.ContractAsync("B1", "Beta", ProjectA, 1_000_000m, active: true);
        await f.Evaluations.CreateAsync(Tenant, b1.SupplierId!.Value, new SaveSupplierEvaluationRequest(Day, Day.AddDays(30), 2, 2, 2, 3), default);
        // Gamma: only a draft contract - its money does not count and nothing is asked of it.
        await f.ContractAsync("G1", "Gamma", ProjectB, 9_000_000m, active: false);
        // Another tenant's supplier is never shown.
        await f.ContractAsync("X1", "Elsewhere", ProjectA, 5_000_000m, active: true, tenant: OtherTenant);

        var dashboard = (await f.Dashboard.GetAsync(Tenant, default)).Value!;

        Assert.Equal(["Acme", "Beta", "Gamma"], dashboard.Rows.Select(r => r.Name)); // largest approved amount first
        var acme = dashboard.Rows[0];
        Assert.Equal((2, 2, 2), (acme.ContractCount, acme.ActiveContractCount, acme.ProjectCount));
        Assert.Equal(5_000_000m, acme.ContractAmount.Rials);
        Assert.Equal((1, 1), (acme.OverdueMilestones, acme.MissedObligations));
        Assert.Equal([SupplierAttention.OverdueMilestones, SupplierAttention.MissedObligations, SupplierAttention.NotEvaluated], acme.Attention);

        var beta = dashboard.Rows[1];
        Assert.Equal((2.25m, 1), (beta.LatestScore, beta.EvaluationCount));
        Assert.Equal([SupplierAttention.LowScore], beta.Attention);

        var gamma = dashboard.Rows[2];
        Assert.Equal(0m, gamma.ContractAmount.Rials);
        Assert.Empty(gamma.Attention);

        var t = dashboard.Totals;
        Assert.Equal((3, 4, 2), (t.SupplierCount, t.ContractCount, t.ProjectCount));
        Assert.Equal(6_000_000m, t.ContractAmount.Rials);
        Assert.Equal((1, 1, 1, 2), (t.OverdueMilestones, t.MissedObligations, t.EvaluatedSuppliers, t.NeedingAttention));
        Assert.Equal(2.25m, t.AverageLatestScore);
    }

    [Fact]
    public async Task ATenantWithoutSuppliers_GetsAnEmptyDashboard()
    {
        var f = new Fixture();

        var dashboard = (await f.Dashboard.GetAsync(Tenant, default)).Value!;

        Assert.Empty(dashboard.Rows);
        Assert.Equal((0, 0, 0), (dashboard.Totals.SupplierCount, dashboard.Totals.ContractCount, dashboard.Totals.NeedingAttention));
        Assert.Null(dashboard.Totals.AverageLatestScore);
    }
}
