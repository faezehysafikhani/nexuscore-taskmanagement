using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Team.Domain;

/// <summary>UserId is optional - governance contacts are often recorded before (or without)
/// the person having a NexusCore account, so raw contact fields stand on their own.</summary>
public sealed class GovernanceRole : AuditableEntity<Guid>
{
    private GovernanceRole() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public GovernanceRole(Guid id, Guid tenantId, Guid projectId, string title, Guid? userId = null) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Title = title.Trim();
        UserId = userId;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; }
    public Guid? UserId { get; private set; }
    public string? PersonnelNumber { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? ServiceLocation { get; private set; }

    public void UpdateDetails(string title, Guid? userId, string? personnelNumber, string? phone, string? email, string? serviceLocation)
    {
        Title = title.Trim();
        UserId = userId;
        PersonnelNumber = personnelNumber;
        Phone = phone;
        Email = email;
        ServiceLocation = serviceLocation;
    }
}
