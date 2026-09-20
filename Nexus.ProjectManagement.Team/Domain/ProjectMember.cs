using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Team.Domain;

public sealed class ProjectMember : AuditableEntity<Guid>
{
    private ProjectMember() : base(Guid.Empty)
    {
    }

    public ProjectMember(Guid id, Guid tenantId, Guid projectId, Guid userId, string? roleTitle = null) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        UserId = userId;
        RoleTitle = roleTitle;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public string? RoleTitle { get; private set; }

    public void UpdateRoleTitle(string? roleTitle) => RoleTitle = roleTitle;
}
