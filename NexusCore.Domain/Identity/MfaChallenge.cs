using NexusCore.SharedKernel.Domain;

namespace NexusCore.Domain.Identity;

public enum MfaPurpose
{
    /// <summary>The second step of a sign-in.</summary>
    SignIn = 0,

    /// <summary>Proving the mobile number works before the account is made to need a code.</summary>
    Enable = 1
}

/// <summary>A one-time code sent by SMS. Only its hash is stored; it is used once, expires, and allows a few wrong tries.</summary>
public sealed class MfaChallenge : Entity<Guid>
{
    public const int MaxAttempts = 5;

    private MfaChallenge() : base(Guid.Empty)
    {
        CodeHash = string.Empty;
    }

    public MfaChallenge(Guid id, Guid userId, MfaPurpose purpose, string codeHash, DateTimeOffset nowUtc, TimeSpan lifetime, string? createdIp) : base(id)
    {
        UserId = userId;
        Purpose = purpose;
        CodeHash = codeHash;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(lifetime);
        CreatedIp = createdIp;
    }

    public Guid UserId { get; private set; }
    public MfaPurpose Purpose { get; private set; }
    public string CodeHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? UsedAtUtc { get; private set; }
    public string? CreatedIp { get; private set; }

    public bool IsUsableAt(DateTimeOffset nowUtc) => UsedAtUtc is null && nowUtc < ExpiresAtUtc && Attempts < MaxAttempts;

    public void RecordWrongAttempt() => Attempts++;

    public void MarkUsed(DateTimeOffset nowUtc) => UsedAtUtc = nowUtc;
}
