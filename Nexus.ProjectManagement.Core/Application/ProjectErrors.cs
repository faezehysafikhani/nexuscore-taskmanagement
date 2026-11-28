using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

/// <summary>The project's stable error codes (the client translates by code) with their Persian messages.</summary>
public static class ProjectErrors
{
    public static Error PendingApproval { get; } = new("project.pending_approval",
        "منشور این پروژه در انتظار تأیید است و تا پایان بررسی قابل ویرایش نیست.");

    public static Error AlreadyPending { get; } = new("project.already_pending",
        "این پروژه قبلاً برای تأیید ارسال شده و در انتظار تأیید است.");

    public static Error AlreadyApproved { get; } = new("project.already_approved",
        "منشور این پروژه قبلاً تأیید شده است. برای تغییر منشور، آن را ویرایش کنید.");

    public static Error NotApproved { get; } = new("project.not_approved",
        "پروژه هنوز تأیید نشده است. وضعیت «فعال»، «متوقف» یا «تکمیل‌شده» فقط برای پروژه‌ی تأییدشده ممکن است.");

    public static Error Forbidden { get; } = new("project.forbidden",
        "فقط مدیر یا مالک پروژه، یا کاربری با دسترسی مدیریت پروژه‌ها، می‌تواند این کار را انجام دهد.");

    public static Error CalendarNotFound { get; } = new("project.calendar_not_found",
        "تقویم کاری انتخاب‌شده پیدا نشد.");
}
