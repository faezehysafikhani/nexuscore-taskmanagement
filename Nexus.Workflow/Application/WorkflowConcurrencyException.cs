namespace Nexus.Workflow.Application;

/// <summary>Someone else changed the same approval at the same time (see WorkflowInstance.RowVersion).</summary>
public sealed class WorkflowConcurrencyException(Exception? inner = null)
    : Exception("The workflow instance was changed by someone else at the same time.", inner);

/// <summary>The codes and Persian messages Workflow answers with, so the client can rely on them.</summary>
public static class WorkflowErrors
{
    public const string ConcurrentDecisionCode = "workflow.concurrent_decision";
    public const string ConcurrentDecisionMessage = "هم‌زمان تصمیم دیگری روی همین مرحله ثبت شد. صفحه را تازه کنید و دوباره بررسی کنید.";

    public const string SelfApprovalCode = "workflow.self_approval";
    public const string SelfApprovalMessage = "ارسال‌کننده نمی‌تواند درخواست خودش را تأیید یا رد کند.";

    public const string SelfApprovalNoManagerMessage =
        "ارسال‌کننده نمی‌تواند تأییدکننده‌ی مرحله‌ی «{0}» باشد و مدیر او در چارت سازمانی هم مشخص نیست. تأییدکننده‌ی این مرحله را در تنظیمات گردش کار تغییر دهید.";

    public const string ApproverMissingCode = "workflow.approver_missing";
    public const string ApproverMissingMessage = "تأییدکننده‌ی مرحله‌ی «{0}» مشخص نیست. تنظیمات گردش کار یا مدیر و مالک پروژه را کامل کنید.";
}
