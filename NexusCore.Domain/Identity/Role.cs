using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class Role : AuditableEntity<Guid>
{
    private readonly List<RolePermission> _permissions = [];

    private Role() : base(Guid.Empty)
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public Role(Guid id, Guid tenantId, string name, string? description = null, bool isSystem = false) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
        IsSystem = isSystem;
    }

    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public void Update(string name, string? description)
    {
        Name = name.Trim();
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
    }

    public void SetPermissions(IEnumerable<Guid> permissionIds)
    {
        _permissions.Clear();
        foreach (var permissionId in permissionIds.Distinct())
        {
            _permissions.Add(new RolePermission(Id, permissionId));
        }
    }
}
