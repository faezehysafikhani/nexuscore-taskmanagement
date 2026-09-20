namespace NexusCore.AdminUI.Services;

public static class PersianMessages
{
    public static string Error(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "عملیات انجام نشد. لطفاً دوباره تلاش کنید.";
        }

        if (error.Contains("Invalid email or password", StringComparison.OrdinalIgnoreCase))
        {
            return "ایمیل یا رمز عبور نادرست است.";
        }

        if (error.Contains("Authentication is required", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "برای انجام این عملیات ابتدا وارد شوید.";
        }

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return "رکورد موردنظر پیدا نشد.";
        }

        if (error.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("409", StringComparison.OrdinalIgnoreCase))
        {
            return "رکوردی با این مشخصات قبلاً ثبت شده است.";
        }

        if (error.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("required", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("400", StringComparison.OrdinalIgnoreCase))
        {
            return "اطلاعات واردشده معتبر نیست. لطفاً فیلدها را بررسی کنید.";
        }

        return "خطایی رخ داد. لطفاً جزئیات درخواست را بررسی کنید.";
    }
}
