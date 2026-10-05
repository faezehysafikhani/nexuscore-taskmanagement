using NexusCore.Application.Files;
using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Tests.Files;

public sealed class UploadPolicyReaderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task NothingConfigured_ReturnsDefault()
    {
        var reader = new UploadPolicyReader(new SettingsStore());
        Assert.Equal(UploadPolicySettings.DefaultMaxFileSizeKb, await reader.GetMaxFileSizeKbAsync(TenantId, default));
    }

    [Fact]
    public async Task SystemWideValue_IsUsedForATenantWithoutItsOwn()
    {
        // The admin settings page saves the value with TenantId null.
        var store = new SettingsStore();
        store.Add(null, "1024");
        Assert.Equal(1024, await new UploadPolicyReader(store).GetMaxFileSizeKbAsync(TenantId, default));
    }

    [Fact]
    public async Task TenantValue_WinsOverSystemWideValue()
    {
        var store = new SettingsStore();
        store.Add(null, "1024");
        store.Add(TenantId, "500");
        Assert.Equal(500, await new UploadPolicyReader(store).GetMaxFileSizeKbAsync(TenantId, default));
    }

    [Fact]
    public async Task ValueAboveHardCeiling_IsClamped()
    {
        var store = new SettingsStore();
        store.Add(null, "999999999");
        Assert.Equal(UploadPolicySettings.HardCeilingKb, await new UploadPolicyReader(store).GetMaxFileSizeKbAsync(TenantId, default));
    }

    private sealed class SettingsStore : IPlatformRepository
    {
        private readonly List<SystemSetting> _settings = [];

        public void Add(Guid? tenantId, string value) =>
            _settings.Add(new SystemSetting(Guid.NewGuid(), tenantId, UploadPolicySettings.SettingKey, value, UploadPolicySettings.SettingScope));

        // Exact TenantId match, like the real repository.
        public Task<SystemSetting?> FindSettingAsync(Guid? tenantId, string key, string scope, CancellationToken cancellationToken) =>
            Task.FromResult(_settings.SingleOrDefault(s => s.TenantId == tenantId && s.Key == key && s.Scope == scope));

        public Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AuditLogDto>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(Guid? tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
