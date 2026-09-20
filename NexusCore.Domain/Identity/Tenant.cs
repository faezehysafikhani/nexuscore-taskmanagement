using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class Tenant : AuditableEntity<Guid>
{
    private Tenant() : base(Guid.Empty)
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public Tenant(Guid id, string name, string slug, bool isActive = true) : base(id)
    {
        Name = name;
        Slug = slug;
        IsActive = isActive;
    }

    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string slug, string? description, bool isActive)
    {
        Name = name;
        Slug = slug;
        Description = description;
        IsActive = isActive;
    }
}
