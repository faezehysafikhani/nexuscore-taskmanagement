using NexusCore.Application.Identity.Sessions;
using NexusCore.Domain.Identity;

namespace NexusCore.Tests.Identity;

public class SessionEndReasonTests
{
    private static UserSession Session(DateTimeOffset created, DateTimeOffset lastActivity, int idleSeconds, DateTimeOffset absolute)
    {
        var session = new UserSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), created, absolute, idleSeconds, "default", null, null, null);
        session.RecordActivity(lastActivity, null);
        return session;
    }

    [Fact]
    public void IdleLimitReachedFirst_IsIdleExpired_EvenWhenReadAfterTheAbsoluteLimit()
    {
        // created 13:22, last activity 13:43, idle 60 min (ends 14:43), absolute next day 01:22.
        var session = Session(
            DateTimeOffset.Parse("2026-11-24T13:22:00Z"), DateTimeOffset.Parse("2026-11-24T13:43:00Z"), 3600,
            DateTimeOffset.Parse("2026-11-25T01:22:00Z"));

        Assert.Equal(SessionEndReason.IdleTimeout, session.EndReasonAt(DateTimeOffset.Parse("2026-11-24T14:43:00Z")));
        Assert.Equal(SessionEndReason.IdleTimeout, session.EndReasonAt(DateTimeOffset.Parse("2026-11-26T00:00:00Z")));
        Assert.Null(session.EndReasonAt(DateTimeOffset.Parse("2026-11-24T14:42:59Z")));
    }

    [Fact]
    public void AbsoluteLimitReachedFirst_IsAbsoluteExpired()
    {
        var session = Session(
            DateTimeOffset.Parse("2026-11-24T10:00:00Z"), DateTimeOffset.Parse("2026-11-24T10:58:00Z"), 600,
            DateTimeOffset.Parse("2026-11-24T11:00:00Z"));

        Assert.Equal(SessionEndReason.AbsoluteTimeout, session.EndReasonAt(DateTimeOffset.Parse("2026-11-24T12:00:00Z")));
    }

    [Fact]
    public void BothLimitsAtTheSameMoment_IsIdleExpired()
    {
        var at = DateTimeOffset.Parse("2026-11-24T11:00:00Z");
        Assert.Equal(SessionEndReason.IdleTimeout, UserSession.NaturalEndReason(at, at, at));
    }

    [Fact]
    public void ValidationFailureCode_UsesTheLimitReachedFirst()
    {
        var state = new SessionValidation(true, null, null, DateTimeOffset.Parse("2026-11-25T01:22:00Z"), 3600, DateTimeOffset.Parse("2026-11-24T13:43:00Z"));
        Assert.Equal(SessionReasons.IdleExpired, state.FailureCode(DateTimeOffset.Parse("2026-11-26T00:00:00Z")));
    }

    [Fact]
    public void PolicyOutOfRange_IsRefusedFieldByField()
    {
        var dto = new SessionPolicySettingsDto(3, 20, 0, 10, 8, 2, 10, 8, 1, [], 10, 4, null!);
        var errors = SessionPolicySettings.Validate(dto);
        Assert.Contains("idleMinutes", errors.Keys);
        Assert.Contains("absoluteHours", errors.Keys);
        Assert.Contains("maxConcurrent", errors.Keys);
        Assert.Equal(3, errors.Count);
    }
}
