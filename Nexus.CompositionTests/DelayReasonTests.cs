using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Application.EventHandlers;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.Progress.Infrastructure;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

public sealed class DelayReasonTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 3);

    private sealed class FakeRepository(params DelayReason[] seed) : IDelayReasonRepository
    {
        public List<DelayReason> Items { get; } = [.. seed];
        public Task<DelayReason?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<DelayReason>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DelayReason>>(Items.Where(r => r.ProjectId == projectId).OrderByDescending(r => r.RegisterDate).ToList());
        public Task AddAsync(DelayReason reason, CancellationToken ct) { Items.Add(reason); return Task.CompletedTask; }
        public Task RemoveAsync(DelayReason reason, CancellationToken ct) { Items.Remove(reason); return Task.CompletedTask; }
    }

    private sealed class FakeUnitOfWork : IProgressUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class FakeApprovals(ApprovalRequestOutcome outcome) : IApprovalRequester
    {
        public ApprovalSubject? Requested { get; private set; }
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken)
        {
            Requested = subject;
            return Task.FromResult(outcome);
        }
    }

    private static DelayReasonService NewService(FakeRepository repository, FakeUnitOfWork? unitOfWork = null,
        ApprovalRequestOutcome outcome = ApprovalRequestOutcome.NotConfigured, FakeApprovals? approvals = null) =>
        new(repository, unitOfWork ?? new FakeUnitOfWork(), approvals ?? new FakeApprovals(outcome));

    private static CreateDelayReasonRequest Create(string description = "Cement delivery slipped", DelayRootCause cause = DelayRootCause.Procurement) =>
        new(Tenant, Project, Today, cause, description, 14, 250_000_000m, "Pre-buy from the commodity exchange");

    [Fact]
    public async Task Create_StoresAllFields_AndStartsUnsubmitted()
    {
        var repository = new FakeRepository();

        var created = await NewService(repository).CreateAsync(Create(), default);

        Assert.True(created.IsSuccess);
        var dto = created.Value!;
        Assert.Equal(DelayRootCause.Procurement, dto.RootCause);
        Assert.Equal(14, dto.TimeImpactDays);
        Assert.Equal(250_000_000m, dto.CostImpact);
        Assert.Equal("Pre-buy from the commodity exchange", dto.CorrectiveAction);
        Assert.Equal(ApprovalStatus.NotSubmitted, dto.ApprovalStatus);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task Create_TrimsTheDescription_AndRejectsABlankOne()
    {
        var repository = new FakeRepository();
        var service = NewService(repository);

        Assert.Equal("padded", (await service.CreateAsync(Create("  padded  "), default)).Value!.Description);

        var blank = await service.CreateAsync(Create("   "), default);
        Assert.Equal("validation.error", blank.Error.Code);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheProjectsReasons_NewestFirst()
    {
        var older = new DelayReason(Guid.NewGuid(), Tenant, Project, new DateOnly(2026, 1, 1), DelayRootCause.Funding, "older");
        var newer = new DelayReason(Guid.NewGuid(), Tenant, Project, new DateOnly(2026, 6, 1), DelayRootCause.Permits, "newer");
        var elsewhere = new DelayReason(Guid.NewGuid(), Tenant, Guid.NewGuid(), Today, DelayRootCause.Other, "elsewhere");

        var result = await NewService(new FakeRepository(older, newer, elsewhere)).ListByProjectAsync(Project, default);

        Assert.Equal(["newer", "older"], result.Value!.Select(r => r.Description));
    }

    [Fact]
    public async Task Update_ChangesTheFields_AndReportsUnknownIds()
    {
        var repository = new FakeRepository();
        var service = NewService(repository);
        var id = (await service.CreateAsync(Create(), default)).Value!.Id;

        var updated = await service.UpdateAsync(id,
            new UpdateDelayReasonRequest(new DateOnly(2026, 11, 1), DelayRootCause.Funding, "Budget release delayed", null, null, null), default);

        Assert.Equal(DelayRootCause.Funding, updated.Value!.RootCause);
        Assert.Equal("Budget release delayed", updated.Value.Description);
        Assert.Null(updated.Value.TimeImpactDays);
        Assert.Equal(new DateOnly(2026, 11, 1), updated.Value.RegisterDate);

        var missing = await service.UpdateAsync(Guid.NewGuid(),
            new UpdateDelayReasonRequest(Today, DelayRootCause.Other, "x", null, null, null), default);
        Assert.Equal("not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Delete_RemovesIt_ButNotWhilePendingApproval()
    {
        var removable = new DelayReason(Guid.NewGuid(), Tenant, Project, Today, DelayRootCause.Other, "a");
        var pending = new DelayReason(Guid.NewGuid(), Tenant, Project, Today, DelayRootCause.Other, "b");
        pending.MarkPendingApproval();
        var repository = new FakeRepository(removable, pending);
        var service = NewService(repository);

        Assert.True((await service.DeleteAsync(removable.Id, default)).IsSuccess);
        Assert.Equal("conflict", (await service.DeleteAsync(pending.Id, default)).Error.Code);
        Assert.Equal([pending], repository.Items);
        Assert.Equal("not_found", (await service.DeleteAsync(Guid.NewGuid(), default)).Error.Code);
    }

    [Fact]
    public async Task Submit_WithNoWorkflowInstalled_ApprovesDirectly()
    {
        var repository = new FakeRepository();
        var service = NewService(repository, outcome: ApprovalRequestOutcome.NotConfigured);
        var id = (await service.CreateAsync(Create(), default)).Value!.Id;

        var submitted = await service.SubmitForApprovalAsync(id, default);

        Assert.Equal(ApprovalStatus.Approved, submitted.Value!.ApprovalStatus);
    }

    [Fact]
    public async Task Submit_WithWorkflow_GoesPending_AndRequestsApprovalForTheRightSubject()
    {
        var approvals = new FakeApprovals(ApprovalRequestOutcome.Submitted);
        var service = NewService(new FakeRepository(), approvals: approvals);
        var created = (await service.CreateAsync(Create(), default)).Value!;

        var submitted = await service.SubmitForApprovalAsync(created.Id, default);

        Assert.Equal(ApprovalStatus.PendingApproval, submitted.Value!.ApprovalStatus);
        Assert.Equal("DelayReason", approvals.Requested!.SubjectType);
        Assert.Equal(created.Id, approvals.Requested.SubjectId);
        Assert.Equal(Project, approvals.Requested.ScopeId);
    }

    [Fact]
    public async Task ApprovalDecisions_AreAppliedOnlyToDelayReasons()
    {
        var reason = new DelayReason(Guid.NewGuid(), Tenant, Project, Today, DelayRootCause.Other, "a");
        reason.MarkPendingApproval();
        var repository = new FakeRepository(reason);
        var unitOfWork = new FakeUnitOfWork();
        var granted = new DelayReasonApprovalGrantedHandler(repository, unitOfWork);
        var rejected = new DelayReasonApprovalRejectedHandler(repository, unitOfWork);

        // Another module's subject with the same id must be ignored.
        await granted.HandleAsync(new ApprovalGranted("Risk", reason.Id, Tenant, null, null), default);
        Assert.Equal(ApprovalStatus.PendingApproval, reason.ApprovalStatus);

        await granted.HandleAsync(new ApprovalGranted("DelayReason", reason.Id, Tenant, null, null), default);
        Assert.Equal(ApprovalStatus.Approved, reason.ApprovalStatus);

        await rejected.HandleAsync(new ApprovalRejected("DelayReason", reason.Id, Tenant, null, null), default);
        Assert.Equal(ApprovalStatus.Rejected, reason.ApprovalStatus);
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task RoundTripsThroughEfCore_AndLeavesProgressUpdatesAlone()
    {
        var options = new DbContextOptionsBuilder<ProgressDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id = Guid.NewGuid();
        await using (var db = new ProgressDbContext(options))
        {
            var reason = new DelayReason(id, Tenant, Project, Today, DelayRootCause.HumanResources, "Key engineer left");
            reason.UpdateDetails(Today, DelayRootCause.HumanResources, "Key engineer left", 30, 1_000_000_000m, "Hire a contractor");
            db.DelayReasons.Add(reason);
            db.ProgressUpdates.Add(new ProgressUpdate(Guid.NewGuid(), Tenant, Project, Today, 50, 40));
            await db.SaveChangesAsync();
        }

        await using (var db = new ProgressDbContext(options))
        {
            var loaded = await db.DelayReasons.SingleAsync(r => r.Id == id);
            Assert.Equal(DelayRootCause.HumanResources, loaded.RootCause);
            Assert.Equal(30, loaded.TimeImpactDays);
            Assert.Equal(1_000_000_000m, loaded.CostImpact);
            Assert.Equal(1, await db.ProgressUpdates.CountAsync());
        }
    }

    [Fact]
    public void TheUpgradeScriptAndHelper_CreateExactlyTheTableTheModelDescribes()
    {
        var options = new DbContextOptionsBuilder<ProgressDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new ProgressDbContext(options);

        // What EF would create for DelayReasons on a brand-new database: the column lines.
        var generated = db.Database.GenerateCreateScript();
        var table = Regex.Match(generated, @"CREATE TABLE \[progress\]\.\[DelayReasons\] \((?<body>.*?)\r?\n\);", RegexOptions.Singleline);
        Assert.True(table.Success, "EF no longer generates the DelayReasons table the upgrade script assumes.");
        var expectedLines = table.Groups["body"].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => line.Length > 0)
            .ToList();
        Assert.Contains("[RootCause] int NOT NULL", expectedLines);
        Assert.Contains("[CostImpact] decimal(18,0) NULL", expectedLines);

        var scriptPath = Path.Combine(FindSolutionRoot(), "docs", "upgrade", "2026-10-03-add-delay-reasons.sql");
        var script = Normalise(File.ReadAllText(scriptPath));
        var helper = Normalise((string)typeof(ProgressSchemaUpgrade)
            .GetField("EnsureDelayReasonsSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!);

        foreach (var line in expectedLines)
        {
            Assert.Contains(Normalise(line), script);
            Assert.Contains(Normalise(line), helper);
        }

        Assert.Contains("IX_DelayReasons_ProjectId", generated);
        Assert.Contains("IX_DelayReasons_ProjectId", script);
        Assert.Contains("IX_DelayReasons_ProjectId", helper);
    }

    private static string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NexusCore.sln not found above the test output.");
    }
}
