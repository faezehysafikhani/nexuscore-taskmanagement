using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Approvals;

/// <summary>Stable codes (with Persian messages) for an item that is with its approvers.</summary>
public static class ApprovalErrors
{
    /// <summary>An item waiting for approval is not edited under its approvers.</summary>
    public static Error PendingEdit { get; } = new("approval.pending_edit",
        "این مورد در انتظار تأیید است و تا پایان بررسی قابل ویرایش نیست.");

    /// <summary>An item already waiting for approval is not sent again.</summary>
    public static Error AlreadyPending { get; } = new("approval.already_pending",
        "این مورد قبلاً برای تأیید ارسال شده و در انتظار تأیید است.");
}
