using Nexus.ProjectManagement.Team.Application.Dtos;
using Nexus.ProjectManagement.Team.Domain;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Team.Application;

public sealed class TeamService(
    ITeamRepository repository,
    ITeamUnitOfWork unitOfWork,
    IIdentityService identityService) : ITeamService
{
    public async Task<Result<IReadOnlyList<ProjectMemberDto>>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var members = await repository.ListMembersAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ProjectMemberDto>>(members.Select(ToDto).ToList());
    }

    public async Task<Result<ProjectMemberDto>> AddMemberAsync(AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        if (await repository.IsMemberAsync(request.ProjectId, request.UserId, cancellationToken))
        {
            return Result.Failure<ProjectMemberDto>(Error.Conflict("This user is already a member of the project."));
        }

        var member = new ProjectMember(Guid.NewGuid(), request.TenantId, request.ProjectId, request.UserId, request.RoleTitle);
        await repository.AddMemberAsync(member, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(member));
    }

    public async Task<Result> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await repository.GetMemberByIdAsync(memberId, cancellationToken);
        if (member is null)
        {
            return Result.Failure(Error.NotFound("Project member not found."));
        }

        await repository.RemoveMemberAsync(member, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<UserDto>>> ListAvailableUsersAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var usersResult = await identityService.ListUsersAsync(tenantId, pageNumber: 1, pageSize: 200, search: null, cancellationToken);
        if (usersResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<UserDto>>(usersResult.Error);
        }

        var members = await repository.ListMembersAsync(projectId, cancellationToken);
        var memberUserIds = members.Select(m => m.UserId).ToHashSet();

        var available = usersResult.Value!.Items.Where(user => !memberUserIds.Contains(user.Id)).ToList();
        return Result.Success<IReadOnlyList<UserDto>>(available);
    }

    public async Task<Result<IReadOnlyList<GovernanceRoleDto>>> ListGovernanceRolesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var roles = await repository.ListGovernanceRolesAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<GovernanceRoleDto>>(roles.Select(ToDto).ToList());
    }

    public async Task<Result<GovernanceRoleDto>> CreateGovernanceRoleAsync(CreateGovernanceRoleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<GovernanceRoleDto>(Error.Validation("Title is required."));
        }

        var role = new GovernanceRole(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Title, request.UserId);
        role.UpdateDetails(request.Title, request.UserId, request.PersonnelNumber, request.Phone, request.Email, request.ServiceLocation);

        await repository.AddGovernanceRoleAsync(role, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(role));
    }

    public async Task<Result<GovernanceRoleDto>> UpdateGovernanceRoleAsync(Guid id, UpdateGovernanceRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await repository.GetGovernanceRoleByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Result.Failure<GovernanceRoleDto>(Error.NotFound("Governance role not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<GovernanceRoleDto>(Error.Validation("Title is required."));
        }

        role.UpdateDetails(request.Title, request.UserId, request.PersonnelNumber, request.Phone, request.Email, request.ServiceLocation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(role));
    }

    private static ProjectMemberDto ToDto(ProjectMember member) =>
        new(member.Id, member.TenantId, member.ProjectId, member.UserId, member.RoleTitle);

    private static GovernanceRoleDto ToDto(GovernanceRole role) => new(
        role.Id, role.TenantId, role.ProjectId, role.Title, role.UserId,
        role.PersonnelNumber, role.Phone, role.Email, role.ServiceLocation);
}
