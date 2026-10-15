namespace Nexus.TaskManagement.Domain;

/// <summary>
/// A task SMS held back during the organization's quiet hours (SMS panel), sent by the reminder
/// job once they end. The text is final: it was rendered when the message was due.
/// </summary>
public sealed class PendingTaskSms
{
    private PendingTaskSms()
    {
        PhoneNumber = string.Empty;
        Text = string.Empty;
    }

    public PendingTaskSms(Guid id, Guid tenantId, string phoneNumber, string text, DateTimeOffset sendAfterUtc, DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        PhoneNumber = phoneNumber;
        Text = text;
        SendAfterUtc = sendAfterUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string Text { get; private set; }
    public DateTimeOffset SendAfterUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
