using Microsoft.Extensions.Logging;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Stable log event ids for the states one recurring-task occurrence goes through, so they can
/// be told apart and searched for in the logs. There is no delivery table (see the report for
/// the planned one); these events are the record.
///
///   claimed          the scheduler reserved the occurrence (its due time moved on)
///   notification ok  the notification was stored for a recipient
///   notification ✗   storing it failed after every retry
///   sms accepted     the SMS gateway accepted the message (accepted, not necessarily delivered)
///   sms failed       the gateway refused or did not answer; never re-sent automatically, since
///                    a message the gateway took but did not confirm would arrive twice
///   sms skipped      nothing to send: SMS off, no mobile number, or SMS switched off by the user
///   retry pending    a storage attempt failed and will be tried again
/// </summary>
public static class ReminderDeliveryEvents
{
    public static readonly EventId OccurrenceClaimed = new(4101, "ReminderOccurrenceClaimed");
    public static readonly EventId NotificationStored = new(4102, "ReminderNotificationStored");
    public static readonly EventId NotificationFailed = new(4103, "ReminderNotificationFailed");
    public static readonly EventId SmsAccepted = new(4104, "ReminderSmsAccepted");
    public static readonly EventId SmsFailed = new(4105, "ReminderSmsFailed");
    public static readonly EventId SmsSkipped = new(4106, "ReminderSmsSkipped");
    public static readonly EventId RetryPending = new(4107, "ReminderRetryPending");
    public static readonly EventId LegacyTimeFound = new(4110, "ReminderLegacyTimeFound");
    public static readonly EventId LegacyTimeRealigned = new(4111, "ReminderLegacyTimeRealigned");
}
