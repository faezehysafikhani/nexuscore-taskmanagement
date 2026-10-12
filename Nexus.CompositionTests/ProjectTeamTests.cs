using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Application.Dtos;
using Nexus.ProjectManagement.Team.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class ProjectTeamTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    private static (TeamService Service, TeamDbContext Db) Build()
    {
        var db = new TeamDbContext(new DbContextOptionsBuilder<TeamDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new TeamService(new TeamRepository(db), db, identityService: null!), db);
    }

    [Fact]
    public async Task SettingTheTeam_AddsTheMissing_RemovesTheRest_AndKeepsTheRoleTitleOfWhoStays()
    {
        var (service, _) = Build();
        var project = Guid.NewGuid();
        var alice = (await service.AddMemberAsync(new AddProjectMemberRequest(Tenant, project, Alice, "Lead"), default)).Value!;
        await service.AddMemberAsync(new AddProjectMemberRequest(Tenant, project, Bob, null), default);

        var team = await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, project, [Alice, Carol, Carol]), default);

        Assert.Equal(new[] { Alice, Carol }.Order(), team.Value!.Select(m => m.UserId).Order());
        Assert.Equal("Lead", team.Value!.Single(m => m.UserId == Alice).RoleTitle);
        Assert.Equal(alice.Id, team.Value!.Single(m => m.UserId == Alice).Id);
        Assert.Null(team.Value!.Single(m => m.UserId == Carol).RoleTitle);

        var empty = await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, project, []), default);
        Assert.Empty(empty.Value!);
    }

    [Fact]
    public async Task SettingOneTeam_DoesNotTouchAnotherProjectsTeam()
    {
        var (service, _) = Build();
        var one = Guid.NewGuid();
        var other = Guid.NewGuid();
        await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, other, [Alice, Bob]), default);

        await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, one, [Carol]), default);

        Assert.Equal(2, (await service.ListMembersAsync(other, default)).Value!.Count);
    }

    [Fact]
    public async Task ManyProjectsTeams_CanBeReadInOneCall()
    {
        var (service, _) = Build();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, p1, [Alice]), default);
        await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, p2, [Alice, Bob]), default);
        await service.SetTeamAsync(new SetProjectTeamRequest(Tenant, p3, [Carol]), default);

        var members = (await service.ListMembersForProjectsAsync(new ListMembersForProjectsRequest([p1, p2, p2]), default)).Value!;

        Assert.Equal(3, members.Count);
        Assert.DoesNotContain(members, m => m.ProjectId == p3);
        Assert.Empty((await service.ListMembersForProjectsAsync(new ListMembersForProjectsRequest([]), default)).Value!);
    }
}
