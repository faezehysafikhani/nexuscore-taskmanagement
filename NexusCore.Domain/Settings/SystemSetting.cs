using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Settings;

public sealed class SystemSetting : AuditableEntity<Guid>
{
    private SystemSetting() : base(Guid.Empty)
    {
        Key = string.Empty;
        Value = string.Empty;
        Scope = string.Empty;
    }

    public SystemSetting(Guid id, Guid? tenantId, string key, string value, string scope = "System") : base(id)
    {
        TenantId = tenantId;
        Key = key;
        Value = value;
        Scope = scope;
    }

    public Guid? TenantId { get; private set; }
    public string Key { get; private set; }
    public string Value { get; private set; }
    public string Scope { get; private set; }

    public void UpdateValue(string value) => Value = value;
}
