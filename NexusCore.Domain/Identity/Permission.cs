using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public sealed class Permission : Entity<Guid>
{
    private Permission() : base(Guid.Empty)
    {
        Name = string.Empty;
        Module = string.Empty;
        Description = string.Empty;
    }

    public Permission(Guid id, string name, string module, string description) : base(id)
    {
        Name = name;
        Module = module;
        Description = description;
    }

    public string Name { get; private set; }
    public string Module { get; private set; }
    public string Description { get; private set; }
}
