using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Domain;

/// <summary>Where, and about what, an organization wants to be told (Zapier, n8n, their own system). Events is a comma-separated list of event names, or "*".</summary>
public sealed class WebhookSubscription : AuditableEntity<Guid>
{
    /// <summary>After this many failed deliveries in a row the subscription switches itself off.</summary>
    public const int MaxConsecutiveFailures = 30;

    private WebhookSubscription() : base(Guid.Empty)
    {
        Name = string.Empty;
        Url = string.Empty;
        Secret = string.Empty;
        Events = "*";
    }

    public WebhookSubscription(Guid id, Guid tenantId, string name, string url, string secret, string events) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Url = url.Trim();
        Secret = secret;
        Events = events;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Url { get; private set; }

    /// <summary>The key deliveries are signed with (HMAC-SHA256). Shown once, when made or rotated.</summary>
    public string Secret { get; private set; }
    public string Events { get; private set; }
    public bool IsActive { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? LastDeliveredAtUtc { get; private set; }

    public bool Wants(string eventType) =>
        IsActive && (Events == "*" || Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(eventType, StringComparer.OrdinalIgnoreCase));

    public void Update(string name, string url, string events, bool isActive)
    {
        Name = name.Trim();
        Url = url.Trim();
        Events = events;
        IsActive = isActive;
        if (isActive)
        {
            ConsecutiveFailures = 0;
        }
    }

    public void RotateSecret(string secret) => Secret = secret;

    public void RecordSuccess(DateTimeOffset nowUtc)
    {
        ConsecutiveFailures = 0;
        LastDeliveredAtUtc = nowUtc;
    }

    public void RecordFailure()
    {
        ConsecutiveFailures++;
        if (ConsecutiveFailures >= MaxConsecutiveFailures)
        {
            IsActive = false;
        }
    }
}

/// <summary>An organization's plan: its limits and how long it lasts. One row per tenant; none means no limits.</summary>
public sealed class TenantMembership
{
    private TenantMembership()
    {
        PlanName = string.Empty;
    }

    public TenantMembership(Guid tenantId, string planName, int? maxUsers, int? maxStorageMb, DateOnly? validFrom, DateOnly? validUntil, string? notes)
    {
        TenantId = tenantId;
        Set(planName, maxUsers, maxStorageMb, validFrom, validUntil, notes);
    }

    public Guid TenantId { get; private set; }
    public string PlanName { get; private set; }
    public int? MaxUsers { get; private set; }
    public int? MaxStorageMb { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public string? Notes { get; private set; }

    public void Set(string planName, int? maxUsers, int? maxStorageMb, DateOnly? validFrom, DateOnly? validUntil, string? notes)
    {
        PlanName = planName.Trim();
        MaxUsers = maxUsers;
        MaxStorageMb = maxStorageMb;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        Notes = notes;
    }

    public bool IsExpired(DateOnly today) => ValidUntil is { } until && today > until;
    public bool NotStarted(DateOnly today) => ValidFrom is { } from && today < from;
}

/// <summary>How long this module keeps its own logs (days; null = for ever). One row per tenant.</summary>
public sealed class TaskRetentionSetting
{
    private TaskRetentionSetting()
    {
    }

    public TaskRetentionSetting(Guid tenantId, int? automationLogDays, int? webhookDeliveryDays)
    {
        TenantId = tenantId;
        Set(automationLogDays, webhookDeliveryDays);
    }

    public Guid TenantId { get; private set; }
    public int? AutomationLogDays { get; private set; }
    public int? WebhookDeliveryDays { get; private set; }

    public void Set(int? automationLogDays, int? webhookDeliveryDays)
    {
        AutomationLogDays = automationLogDays;
        WebhookDeliveryDays = webhookDeliveryDays;
    }
}

public enum ReportFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

/// <summary>A person's standing request for a digest of their own tasks, sent by SMS and/or email at a set time (Iran time).</summary>
public sealed class ReportSubscription : NexusCore.SharedKernel.Domain.AuditableEntity<Guid>
{
    public static readonly TimeSpan IranOffset = TimeSpan.FromMinutes(210);

    private ReportSubscription() : base(Guid.Empty)
    {
    }

    public ReportSubscription(Guid id, Guid tenantId, Guid userId, ReportFrequency frequency, DayOfWeek? dayOfWeek, int? dayOfMonth, int hour, bool sms, bool email, DateTimeOffset nowUtc) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        IsActive = true;
        Set(frequency, dayOfWeek, dayOfMonth, hour, sms, email, nowUtc);
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public ReportFrequency Frequency { get; private set; }
    public DayOfWeek? DayOfWeek { get; private set; }
    public int? DayOfMonth { get; private set; }

    /// <summary>The hour of the day in Iran time (0-23).</summary>
    public int Hour { get; private set; }
    public bool BySms { get; private set; }
    public bool ByEmail { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset NextRunAtUtc { get; private set; }
    public DateTimeOffset? LastSentAtUtc { get; private set; }

    public void Set(ReportFrequency frequency, DayOfWeek? dayOfWeek, int? dayOfMonth, int hour, bool sms, bool email, DateTimeOffset nowUtc)
    {
        Frequency = frequency;
        DayOfWeek = frequency == ReportFrequency.Weekly ? dayOfWeek ?? System.DayOfWeek.Saturday : null;
        DayOfMonth = frequency == ReportFrequency.Monthly ? Math.Clamp(dayOfMonth ?? 1, 1, 28) : null;
        Hour = Math.Clamp(hour, 0, 23);
        BySms = sms;
        ByEmail = email;
        NextRunAtUtc = NextRunAfter(nowUtc);
    }

    public void SetActive(bool active, DateTimeOffset nowUtc)
    {
        IsActive = active;
        if (active)
        {
            NextRunAtUtc = NextRunAfter(nowUtc);
        }
    }

    /// <summary>Sent: the next run is the one after <paramref name="nowUtc"/>.</summary>
    public void MarkSent(DateTimeOffset nowUtc)
    {
        LastSentAtUtc = nowUtc;
        NextRunAtUtc = NextRunAfter(nowUtc);
    }

    /// <summary>The next moment (UTC) this subscription is due strictly after <paramref name="after"/>.</summary>
    public DateTimeOffset NextRunAfter(DateTimeOffset after)
    {
        var local = after.ToOffset(IranOffset);
        var day = DateOnly.FromDateTime(local.DateTime);
        DateTimeOffset At(DateOnly d) => new(d.ToDateTime(new TimeOnly(Hour, 0)), IranOffset);

        for (var i = 0; i < 400; i++)
        {
            var candidate = day.AddDays(i);
            var fits = Frequency switch
            {
                ReportFrequency.Daily => true,
                ReportFrequency.Weekly => candidate.DayOfWeek == (DayOfWeek ?? System.DayOfWeek.Saturday),
                _ => candidate.Day == (DayOfMonth ?? 1)
            };
            if (fits && At(candidate) > local)
            {
                return At(candidate).ToUniversalTime();
            }
        }

        return after.AddDays(1);
    }
}

/// <summary>One event to send to one subscription; kept until delivered (or given up on) - the webhook outbox.</summary>
public sealed class WebhookDelivery
{
    public const int MaxAttempts = 8;

    private WebhookDelivery()
    {
        EventType = string.Empty;
        Payload = "{}";
    }

    public WebhookDelivery(Guid id, Guid tenantId, Guid subscriptionId, string eventType, string payload, DateTimeOffset nowUtc)
    {
        Id = id;
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        EventType = eventType;
        Payload = payload;
        CreatedAtUtc = nowUtc;
        NextAttemptAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    /// <summary>Set when it will not be tried again.</summary>
    public DateTimeOffset? GaveUpAtUtc { get; private set; }
    public int? LastStatusCode { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsPending => DeliveredAtUtc is null && GaveUpAtUtc is null;

    /// <summary>Held by one server for a while, so two do not send the same row.</summary>
    public void Lease(DateTimeOffset until) => NextAttemptAtUtc = until;

    public void Succeeded(int statusCode, DateTimeOffset nowUtc)
    {
        Attempts++;
        DeliveredAtUtc = nowUtc;
        LastStatusCode = statusCode;
        LastError = null;
    }

    public void Failed(int? statusCode, string error, DateTimeOffset nowUtc)
    {
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error.Length <= 480 ? error : error[..480];
        if (Attempts >= MaxAttempts)
        {
            GaveUpAtUtc = nowUtc;
        }
        else
        {
            NextAttemptAtUtc = nowUtc.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Attempts - 1)));
        }
    }

    public void GiveUp(string error, DateTimeOffset nowUtc)
    {
        LastError = error;
        GaveUpAtUtc = nowUtc;
    }
}
