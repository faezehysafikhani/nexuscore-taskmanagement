using FluentValidation;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Identity.Validators;

/// <summary>What an organization demands of a new password (CAT-257): a tenant's own settings, else the system-wide ones, else the defaults.</summary>
public sealed record PasswordPolicy(int MinLength, int RequiredClasses)
{
    public static readonly PasswordPolicy Default = new(PasswordRules.MinLength, 3);

    public string Message =>
        $"رمز عبور باید حداقل {MinLength} کاراکتر باشد و دست‌کم از {RequiredClasses} نوع کاراکتر (حرف کوچک، حرف بزرگ، رقم، نماد) تشکیل شده باشد و ساده یا تکراری نباشد.";
}

public static class PasswordPolicySettings
{
    public const string MinLengthKey = "Security.PasswordMinLength";
    public const string RequiredClassesKey = "Security.PasswordRequiredClasses";
    public const string Scope = "System";
    public const int LowestMinLength = 8;
    public const int HighestMinLength = 64;
}

public interface IPasswordPolicyReader
{
    Task<PasswordPolicy> GetAsync(Guid? tenantId, CancellationToken cancellationToken);
}

public sealed class PasswordPolicyReader(IPlatformRepository repository, ICurrentUserContext currentUser) : IPasswordPolicyReader
{
    public async Task<PasswordPolicy> GetAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        tenantId ??= currentUser.TenantId;
        var min = await ReadAsync(tenantId, PasswordPolicySettings.MinLengthKey, cancellationToken);
        var classes = await ReadAsync(tenantId, PasswordPolicySettings.RequiredClassesKey, cancellationToken);
        return new PasswordPolicy(
            min is { } m ? Math.Clamp(m, PasswordPolicySettings.LowestMinLength, PasswordPolicySettings.HighestMinLength) : PasswordPolicy.Default.MinLength,
            classes is { } c ? Math.Clamp(c, 2, 4) : PasswordPolicy.Default.RequiredClasses);
    }

    private async Task<int?> ReadAsync(Guid? tenantId, string key, CancellationToken cancellationToken)
    {
        var setting = tenantId is null ? null : await repository.FindSettingAsync(tenantId, key, PasswordPolicySettings.Scope, cancellationToken);
        setting ??= await repository.FindSettingAsync(null, key, PasswordPolicySettings.Scope, cancellationToken);
        return setting is not null && int.TryParse(setting.Value, out var value) ? value : null;
    }
}
