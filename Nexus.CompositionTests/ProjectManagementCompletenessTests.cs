using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Domain;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using Nexus.ProjectManagement.StakeholderManagement.Domain;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Application.Dtos;
using Nexus.ProjectManagement.Team.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// Service-level tests for the delete/update/matrix operations added to the project management
/// modules. They run against in-memory fakes, so they need neither SQL Server nor a DI host.
/// </summary>
public sealed class ProjectManagementCompletenessTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    private sealed class FakeUnitOfWork : IRiskUnitOfWork, IStakeholderUnitOfWork, IProgressUnitOfWork, IKpiUnitOfWork, ITeamUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class NotConfiguredApprovals : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    // ---------------------------------------------------------------- Risk

    private sealed class FakeRiskRepository(params Risk[] seed) : IRiskRepository
    {
        public List<Risk> Items { get; } = [.. seed];
        public Task<Risk?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<Risk>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Risk>>(Items.Where(r => r.ProjectId == projectId).ToList());
        public Task AddAsync(Risk risk, CancellationToken ct) { Items.Add(risk); return Task.CompletedTask; }
        public Task RemoveAsync(Risk risk, CancellationToken ct) { Items.Remove(risk); return Task.CompletedTask; }
    }

    private static Risk NewRisk(int probability, int impact) =>
        new(Guid.NewGuid(), Tenant, Project, "risk", probability, severityScore: 3, impact);

    private static RiskService RiskService(FakeRiskRepository repository, FakeUnitOfWork unitOfWork) =>
        new(repository, unitOfWork, new NotConfiguredApprovals());

    [Fact]
    public async Task Risk_Delete_RemovesIt()
    {
        var risk = NewRisk(2, 2);
        var repository = new FakeRiskRepository(risk);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RiskService(repository, unitOfWork).DeleteAsync(risk.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Items);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Risk_Delete_UnknownId_IsNotFound()
    {
        var result = await RiskService(new FakeRiskRepository(), new FakeUnitOfWork()).DeleteAsync(Guid.NewGuid(), default);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Risk_Delete_PendingApproval_IsConflictAndKeepsTheRisk()
    {
        var risk = NewRisk(2, 2);
        risk.MarkPendingApproval();
        var repository = new FakeRiskRepository(risk);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RiskService(repository, unitOfWork).DeleteAsync(risk.Id, default);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Single(repository.Items);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Risk_Matrix_GroupsByProbabilityAndImpact_OnlyForTheRequestedProject()
    {
        var a = NewRisk(2, 3);
        var b = NewRisk(2, 3);
        var c = NewRisk(5, 1);
        var otherProject = new Risk(Guid.NewGuid(), Tenant, Guid.NewGuid(), "other", 2, 3, 3);
        var repository = new FakeRiskRepository(a, b, c, otherProject);

        var result = await RiskService(repository, new FakeUnitOfWork()).GetMatrixAsync(Project, default);

        Assert.True(result.IsSuccess);
        var matrix = result.Value!;
        Assert.Equal(3, matrix.TotalRisks);
        Assert.Equal(2, matrix.Cells.Count);

        var shared = matrix.Cells.Single(cell => cell.ProbabilityScore == 2 && cell.ImpactScore == 3);
        Assert.Equal(2, shared.Count);
        Assert.Equivalent(new[] { a.Id, b.Id }, shared.RiskIds);

        var corner = matrix.Cells.Single(cell => cell.ProbabilityScore == 5 && cell.ImpactScore == 1);
        Assert.Equal(1, corner.Count);
    }

    [Fact]
    public async Task Risk_Matrix_ForAProjectWithNoRisks_IsEmpty()
    {
        var result = await RiskService(new FakeRiskRepository(), new FakeUnitOfWork()).GetMatrixAsync(Project, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalRisks);
        Assert.Empty(result.Value.Cells);
    }

    // --------------------------------------------------------- Stakeholder

    private sealed class FakeStakeholderRepository(params Stakeholder[] seed) : IStakeholderRepository
    {
        public List<Stakeholder> Items { get; } = [.. seed];
        public Task<Stakeholder?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<Stakeholder>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Stakeholder>>(Items.Where(s => s.ProjectId == projectId).ToList());
        public Task AddAsync(Stakeholder stakeholder, CancellationToken ct) { Items.Add(stakeholder); return Task.CompletedTask; }
        public Task RemoveAsync(Stakeholder stakeholder, CancellationToken ct) { Items.Remove(stakeholder); return Task.CompletedTask; }
    }

    private static Stakeholder NewStakeholder(string name, PowerLevel power, InterestLevel interest)
    {
        var stakeholder = new Stakeholder(Guid.NewGuid(), Tenant, Project, name, isInternal: true);
        stakeholder.UpdateDetails(name, true, null, null, power, interest, null, null);
        return stakeholder;
    }

    private static StakeholderService StakeholderService(FakeStakeholderRepository repository, FakeUnitOfWork unitOfWork) =>
        new(repository, unitOfWork, new NotConfiguredApprovals());

    [Fact]
    public async Task Stakeholder_Delete_RemovesIt_ButNotWhilePendingApproval()
    {
        var removable = NewStakeholder("a", PowerLevel.Low, InterestLevel.Low);
        var pending = NewStakeholder("b", PowerLevel.Low, InterestLevel.Low);
        pending.MarkPendingApproval();
        var repository = new FakeStakeholderRepository(removable, pending);
        var service = StakeholderService(repository, new FakeUnitOfWork());

        Assert.True((await service.DeleteAsync(removable.Id, default)).IsSuccess);
        var blocked = await service.DeleteAsync(pending.Id, default);

        Assert.Equal("conflict", blocked.Error.Code);
        Assert.Equal([pending], repository.Items);
        Assert.Equal("not_found", (await service.DeleteAsync(Guid.NewGuid(), default)).Error.Code);
    }

    [Theory]
    [InlineData(PowerLevel.High, InterestLevel.High, StakeholderQuadrant.ManageClosely)]
    [InlineData(PowerLevel.High, InterestLevel.Medium, StakeholderQuadrant.KeepSatisfied)]
    [InlineData(PowerLevel.High, InterestLevel.Low, StakeholderQuadrant.KeepSatisfied)]
    [InlineData(PowerLevel.Medium, InterestLevel.High, StakeholderQuadrant.KeepInformed)]
    [InlineData(PowerLevel.Low, InterestLevel.High, StakeholderQuadrant.KeepInformed)]
    [InlineData(PowerLevel.Low, InterestLevel.Low, StakeholderQuadrant.Monitor)]
    [InlineData(PowerLevel.Medium, InterestLevel.Medium, StakeholderQuadrant.Monitor)]
    public async Task Stakeholder_Matrix_AssignsTheExpectedQuadrant(PowerLevel power, InterestLevel interest, StakeholderQuadrant expected)
    {
        var repository = new FakeStakeholderRepository(NewStakeholder("s", power, interest));

        var result = await StakeholderService(repository, new FakeUnitOfWork()).GetMatrixAsync(Project, default);

        var cell = Assert.Single(result.Value!.Cells);
        Assert.Equal(expected, cell.Quadrant);
        Assert.Equal("s", Assert.Single(cell.Stakeholders).Name);
    }

    [Fact]
    public async Task Stakeholder_Matrix_GroupsStakeholdersSharingACell()
    {
        var repository = new FakeStakeholderRepository(
            NewStakeholder("a", PowerLevel.High, InterestLevel.High),
            NewStakeholder("b", PowerLevel.High, InterestLevel.High),
            NewStakeholder("c", PowerLevel.Low, InterestLevel.Low));

        var result = await StakeholderService(repository, new FakeUnitOfWork()).GetMatrixAsync(Project, default);

        Assert.Equal(3, result.Value!.TotalStakeholders);
        Assert.Equal(2, result.Value.Cells.Count);
        Assert.Equal(2, result.Value.Cells.Single(c => c.Quadrant == StakeholderQuadrant.ManageClosely).Stakeholders.Count);
    }

    // ------------------------------------------------------------ Progress

    private sealed class FakeProgressRepository(params ProgressUpdate[] seed) : IProgressRepository
    {
        public List<ProgressUpdate> Items { get; } = [.. seed];
        public Task<ProgressUpdate?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<ProgressUpdate>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProgressUpdate>>(Items.Where(p => p.ProjectId == projectId).ToList());
        public Task AddAsync(ProgressUpdate update, CancellationToken ct) { Items.Add(update); return Task.CompletedTask; }
        public Task RemoveAsync(ProgressUpdate update, CancellationToken ct) { Items.Remove(update); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Progress_Delete_RemovesIt_ButNotWhilePendingApproval()
    {
        var removable = new ProgressUpdate(Guid.NewGuid(), Tenant, Project, new DateOnly(2026, 1, 1), 10, 5);
        var pending = new ProgressUpdate(Guid.NewGuid(), Tenant, Project, new DateOnly(2026, 2, 1), 20, 10);
        pending.MarkPendingApproval();
        var repository = new FakeProgressRepository(removable, pending);
        var service = new ProgressService(repository, new FakeUnitOfWork(), new NotConfiguredApprovals());

        Assert.True((await service.DeleteAsync(removable.Id, default)).IsSuccess);
        Assert.Equal("conflict", (await service.DeleteAsync(pending.Id, default)).Error.Code);
        Assert.Equal([pending], repository.Items);
        Assert.Equal("not_found", (await service.DeleteAsync(Guid.NewGuid(), default)).Error.Code);
    }

    // ----------------------------------------------------------------- Kpi

    private sealed class FakeKpiRepository(params KpiDefinition[] seed) : IKpiRepository
    {
        public List<KpiDefinition> Items { get; } = [.. seed];
        public Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(k => k.Id == id));
        public Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiDefinition>>(Items.Where(k => k.ProjectId == projectId).ToList());
        public Task AddAsync(KpiDefinition kpi, CancellationToken ct) { Items.Add(kpi); return Task.CompletedTask; }
        public Task RemoveAsync(KpiDefinition kpi, CancellationToken ct) { Items.Remove(kpi); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Kpi_Delete_RemovesIt_AndReportsUnknownIds()
    {
        var kpi = new KpiDefinition(Guid.NewGuid(), Tenant, Project, Guid.NewGuid(), default, "kpi");
        var repository = new FakeKpiRepository(kpi);
        // Delete never touches the deliverable repository, so none is needed here.
        var service = new KpiService(repository, deliverableRepository: null!, new FakeUnitOfWork());

        Assert.Equal("not_found", (await service.DeleteAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.True((await service.DeleteAsync(kpi.Id, default)).IsSuccess);
        Assert.Empty(repository.Items);
    }

    // ---------------------------------------------------------------- Team

    private sealed class FakeTeamRepository : ITeamRepository
    {
        public List<ProjectMember> Members { get; } = [];
        public List<GovernanceRole> Roles { get; } = [];
        public Task<ProjectMember?> GetMemberByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Members.SingleOrDefault(m => m.Id == id));
        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectMember>>(Members.Where(m => m.ProjectId == projectId).ToList());
        public Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
            Task.FromResult(Members.Any(m => m.ProjectId == projectId && m.UserId == userId));
        public Task<IReadOnlyList<Guid>> ListProjectIdsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Members.Where(m => m.TenantId == tenantId && m.UserId == userId).Select(m => m.ProjectId).Distinct().ToList());
        public Task AddMemberAsync(ProjectMember member, CancellationToken ct) { Members.Add(member); return Task.CompletedTask; }
        public Task RemoveMemberAsync(ProjectMember member, CancellationToken ct) { Members.Remove(member); return Task.CompletedTask; }
        public Task<GovernanceRole?> GetGovernanceRoleByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Roles.SingleOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<GovernanceRole>> ListGovernanceRolesAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GovernanceRole>>(Roles.Where(r => r.ProjectId == projectId).ToList());
        public Task AddGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) { Roles.Add(role); return Task.CompletedTask; }
        public Task RemoveGovernanceRoleAsync(GovernanceRole role, CancellationToken ct) { Roles.Remove(role); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Team_UpdateMember_ChangesOnlyTheRoleTitle_AndBlanksBecomeNull()
    {
        var userId = Guid.NewGuid();
        var member = new ProjectMember(Guid.NewGuid(), Tenant, Project, userId, "Engineer");
        var repository = new FakeTeamRepository();
        repository.Members.Add(member);
        // Updating a member never touches the identity service.
        var service = new TeamService(repository, new FakeUnitOfWork(), identityService: null!);

        var renamed = await service.UpdateMemberAsync(member.Id, new UpdateProjectMemberRequest("  Lead  "), default);
        Assert.Equal("Lead", renamed.Value!.RoleTitle);
        Assert.Equal(userId, renamed.Value.UserId);

        var cleared = await service.UpdateMemberAsync(member.Id, new UpdateProjectMemberRequest("   "), default);
        Assert.Null(cleared.Value!.RoleTitle);

        Assert.Equal("not_found", (await service.UpdateMemberAsync(Guid.NewGuid(), new UpdateProjectMemberRequest("x"), default)).Error.Code);
    }

    [Fact]
    public async Task Team_DeleteGovernanceRole_RemovesIt_AndReportsUnknownIds()
    {
        var role = new GovernanceRole(Guid.NewGuid(), Tenant, Project, "Sponsor");
        var repository = new FakeTeamRepository();
        repository.Roles.Add(role);
        var service = new TeamService(repository, new FakeUnitOfWork(), identityService: null!);

        Assert.Equal("not_found", (await service.DeleteGovernanceRoleAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.True((await service.DeleteGovernanceRoleAsync(role.Id, default)).IsSuccess);
        Assert.Empty(repository.Roles);
    }
}
