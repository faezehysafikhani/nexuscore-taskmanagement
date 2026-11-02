using System.Globalization;
using FluentValidation;

namespace NexusCore.Application.Common;

/// <summary>
/// The validators' own messages («'Tenant Id' must not be empty.») in Persian, with the field's
/// Persian name: FluentValidation's built-in Persian messages and a name for the common fields
/// (any other field keeps its own name). Messages a validator sets itself are not touched.
/// </summary>
public static class PersianValidationMessages
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TenantId"] = "سازمان",
        ["Username"] = "نام کاربری (کد ملی)",
        ["Password"] = "رمز عبور",
        ["NewPassword"] = "رمز عبور جدید",
        ["CurrentPassword"] = "رمز عبور فعلی",
        ["PhoneNumber"] = "شماره تلفن همراه",
        ["Email"] = "ایمیل",
        ["FirstName"] = "نام",
        ["LastName"] = "نام خانوادگی",
        ["DisplayName"] = "نام نمایشی",
        ["Name"] = "نام",
        ["Title"] = "عنوان",
        ["Description"] = "توضیح",
        ["Code"] = "کد",
        ["Token"] = "کد",
        ["RoleIds"] = "نقش‌ها",
        ["PermissionIds"] = "مجوزها",
        ["Identifier"] = "نام کاربری یا شماره تلفن",
        ["StartDate"] = "تاریخ شروع",
        ["DueDate"] = "مهلت",
    };

    public static void Use()
    {
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("fa");
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
            member is not null && Names.TryGetValue(member.Name, out var name) ? name : null;
    }
}
