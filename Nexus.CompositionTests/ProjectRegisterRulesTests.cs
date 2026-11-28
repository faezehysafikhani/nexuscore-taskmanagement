using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Phase 4: a register item with its approvers is locked (here: a risk), and one definition of an active project (M8).</summary>
public sealed class ProjectRegisterRulesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class Sends : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.Submitted);
    }

    [Fact]
    public async Task ARiskWithItsApprovers_IsNotEditedNorSentAgain_AndCanBeOnceDecided()
    {
        var db = new RiskManagementDbContext(new DbContextOptionsBuilder<RiskManagementDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new RiskService(new RiskRepository(db), db, new Sends(), new RiskSettingsRepository(db));
        var risk = (await service.CreateAsync(new CreateRiskRequest(Tenant, Guid.NewGuid(), "تأخیر تأمین‌کننده", 3, 3, 3, "دو تأمین‌کننده", null), default)).Value!;
        await service.SubmitForApprovalAsync(risk.Id, default);

        var edit = new UpdateRiskRequest("تغییر در انتظار تأیید", 3, 3, 3, null, null);
        Assert.Equal("approval.pending_edit", (await service.UpdateAsync(risk.Id, edit, default)).Error.Code);
        Assert.Equal("approval.already_pending", (await service.SubmitForApprovalAsync(risk.Id, default)).Error.Code);

        (await db.Risks.SingleAsync()).Reject();
        await db.SaveChangesAsync();
        Assert.True((await service.UpdateAsync(risk.Id, edit, default)).IsSuccess); // rejected: corrected, then sent again
        Assert.True((await service.SubmitForApprovalAsync(risk.Id, default)).IsSuccess);
    }

    [Theory]
    [InlineData(true, ProjectStatus.Active, true)]
    [InlineData(true, ProjectStatus.OnHold, true)]
    [InlineData(true, ProjectStatus.Completed, false)]
    [InlineData(true, ProjectStatus.Draft, false)]
    [InlineData(false, ProjectStatus.Active, false)] // an Active status left over from before H2 does not make it active
    public void M8_AnActiveProject_IsApproved_AndActiveOrOnHold(bool approved, ProjectStatus status, bool active)
    {
        var project = new Project(Guid.NewGuid(), Tenant, "P", "P1", ProjectType.Waterfall);
        if (approved)
        {
            project.Approve();
        }

        project.ChangeStatus(status);

        Assert.Equal(active, project.IsActive);
    }
}
