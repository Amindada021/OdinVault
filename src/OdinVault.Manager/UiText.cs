namespace OdinVault.Manager;

internal static class UiText
{
    public static bool IsPersian(string? language) =>
        !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

    public static string TranslateLiteral(string text, string? language)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var fa = IsPersian(language);
        foreach (var pair in LiteralPairs)
        {
            if (fa && string.Equals(text, pair.En, StringComparison.Ordinal))
                return pair.Fa;
            if (!fa && string.Equals(text, pair.Fa, StringComparison.Ordinal))
                return pair.En;
        }

        return text;
    }

    private static readonly (string Fa, string En)[] LiteralPairs =
    [
        ("دیتابیس‌های محافظت‌شده", "Protected databases"),
        ("از کل دیتابیس‌های فعال", "of enabled databases"),
        ("خطاهای ۲۴ ساعت اخیر", "Failures in last 24 hours"),
        ("عملیات ناموفق یا متوقف‌شده", "failed or interrupted operations"),
        ("عملیات فعال", "Active operations"),
        ("در صف یا در حال اجرا", "queued or running"),
        ("فضای آزاد بکاپ", "Free backup storage"),
        ("فضای مقصد محلی Agent", "Agent local destination"),
        ("بازه نمودارها", "Chart range"),
        ("۷ روز اخیر", "Last 7 days"),
        ("۳۰ روز اخیر", "Last 30 days"),
        ("نیازمند توجه", "Needs attention"),
        ("فعالیت‌های اخیر", "Recent activity"),
        ("بروزرسانی", "Refresh"),
        ("همه وضعیت‌ها", "All statuses"),
        ("در صف", "Queued"),
        ("در حال اجرا", "Running"),
        ("متوقف‌شده", "Interrupted"),
        ("وضعیت", "Status"),
        ("دیتابیس", "Database"),
        ("Jobها", "Jobs"),
        ("تاریخچه بکاپ", "Backup history"),
        ("مرحله", "Stage"),
        ("پیشرفت", "Progress"),
        ("شروع", "Started"),
        ("آخرین تغییر", "Last change"),
        ("زمان", "Time"),
        ("فایل Local", "Local file"),
        ("همه دیتابیس‌ها", "All databases"),
        ("موجود", "Available"),
        ("حذف‌شده", "Deleted"),
        ("ساخت بکاپ", "Create backup"),
        ("بررسی سلامت", "Verify"),
        ("ارسال Replica", "Replicate"),
        ("پاکسازی نگهداری", "Retention cleanup"),
        ("کامل", "Completed"),
        ("مسیر Local", "Local path"),
        ("فضای آزاد", "Free space"),
        ("تست اتصال", "Test connection"),
        ("ویرایش", "Edit"),
        ("فعال / غیرفعال", "Enable / Disable"),
        ("مدیریت Replica", "Manage replica"),
        ("نام", "Name"),
        ("نوع", "Type"),
        ("مسیر / حساب / مقصد", "Path / account / destination"),
        ("Replica موفق", "Successful replicas"),
        ("Replica ناموفق", "Failed replicas"),
        ("آخرین موفقیت", "Last success"),
        ("آخرین خطا", "Last error"),
        ("مقصدهای ذخیره‌سازی", "Storage targets"),
        ("غیرفعال", "Disabled"),
        ("متصل", "Connected"),
        ("نیاز به اتصال", "Connection required"),
        ("Restore امن به دیتابیس جدید", "Safe restore to a new database"),
        ("شروع Restore Wizard", "Start Restore Wizard"),
        ("خوانده شد", "Mark read"),
        ("خواندن همه", "Mark all read"),
        ("همه", "All"),
        ("خوانده‌نشده", "Unread"),
        ("خوانده‌شده", "Read"),
        ("همه شدت‌ها", "All severities"),
        ("بحرانی", "Critical"),
        ("هشدار", "Warning"),
        ("شدت", "Severity"),
        ("دیتابیس / منبع", "Database / source"),
        ("عنوان", "Title"),
        ("جزئیات", "Details"),
        ("جدید", "New"),
        ("اطلاع", "Info"),
        ("بکاپ", "Backup"),
        ("حفاظت", "Protection"),
        ("فضا", "Storage"),
        ("خروجی CSV برای Excel", "Export CSV for Excel"),
        ("۷ روز", "7 days"),
        ("۳۰ روز", "30 days"),
        ("۹۰ روز", "90 days"),
        ("بازه گزارش", "Report range"),
        ("نرخ موفقیت", "Success rate"),
        ("در بازه انتخاب‌شده", "in selected range"),
        ("بکاپ ناموفق", "Failed backups"),
        ("تعداد بکاپ‌های ناموفق", "failed backup count"),
        ("میانگین مدت", "Average duration"),
        ("بکاپ‌های موفق", "Successful backups"),
        ("دیتابیس محافظت‌شده", "Protected databases"),
        ("از کل دیتابیس فعال", "of enabled databases"),
        ("روند موفق / ناموفق / Verify ناموفق", "Success / failure / verify failure trend"),
        ("حجم بکاپ‌های موفق", "Successful backup size"),
        ("Verify ناموفق", "Verify failed"),
        ("آخرین حجم", "Latest size"),
        ("رشد حجم", "Size growth"),
        ("آخرین بکاپ", "Latest backup"),
        ("گزارش دیتابیس‌ها", "Database report"),
        ("بررسی بروزرسانی", "Check for updates"),
        ("اتصال موبایل", "Mobile connection"),
        ("امکانات اصلی", "Main features"),
        ("رفتار برنامه", "Behavior"),
        ("ظاهر و زبان", "Appearance & language"),
        ("ذخیره تنظیمات", "Save settings"),
        ("آدرس فعلی اتصال موبایل", "Current mobile connection URL"),
        ("مدیریت اتصال موبایل", "Manage mobile connection"),
        ("آدرس اتصال موبایل", "Mobile connection address"),
        ("URL نهایی", "Final URL"),
        ("ذخیره آدرس اتصال", "Save connection address"),
        ("امنیت و API Key", "Security & API Key"),
        ("نمایش کلید", "Show key"),
        ("کپی API Key", "Copy API Key"),
        ("بررسی اتصال", "Connection tests"),
        ("تست اتصال Agent", "Test Agent connection"),
        ("بررسی باز بودن Port", "Check port"),
        ("آماده برای توسعه نسخه‌های بعدی", "Ready for future features"),
        ("+ افزودن زمان", "+ Add time"),
        ("دستی", "Manual"),
        ("روزانه یک‌بار", "Once daily"),
        ("روزانه چند زمان", "Multiple times daily"),
        ("حذف", "Remove"),
        ("زمان‌بندی", "Schedule"),
        ("بازیابی بکاپ به دیتابیس جدید", "Restore backup to a new database"),
        ("اجرای Restore", "Run Restore"),
        ("بستن", "Close"),
        ("موفق", "Succeeded"),
        ("ناموفق", "Failed"),
        ("در حال بررسی", "Checking"),
        ("درخواست نشده", "Not requested"),
        ("شناسایی دیتابیس‌ها", "Discover databases"),
        ("انتخاب", "Select"),
        ("دسترسی", "Access"),
        ("آماده بکاپ", "Ready for backup"),
        ("ثبت‌شده", "Registered"),
        ("افزودن دیتابیس‌های انتخاب‌شده", "Add selected databases"),
        ("Verify بعد از بکاپ", "Verify after backup"),
        ("مسیر بکاپ", "Backup path"),
        ("تعداد نگهداری", "Retention count")
    ];

    public static string Get(string key, string? language)
    {
        var fa = IsPersian(language);
        return key switch
        {
            "app.title" => "OdinVault Manager",
            "nav.dashboard" => fa ? "داشبورد" : "Dashboard",
            "nav.databases" => fa ? "دیتابیس‌ها" : "Databases",
            "nav.backups" => fa ? "بکاپ‌ها" : "Backups",
            "nav.storage" => fa ? "ذخیره‌سازی" : "Storage",
            "nav.restore" => fa ? "بازیابی" : "Restore",
            "nav.alerts" => fa ? "هشدارها" : "Alerts",
            "nav.reports" => fa ? "گزارش‌ها" : "Reports",
            "nav.mobile" => fa ? "اتصال موبایل" : "Mobile",
            "nav.settings" => fa ? "تنظیمات" : "Settings",
            "nav.about" => fa ? "ⓘ درباره" : "ⓘ About",

            "page.dashboard" => fa ? "داشبورد" : "Dashboard",
            "page.databases" => fa ? "دیتابیس‌ها" : "Databases",
            "page.backups" => fa ? "بکاپ‌ها" : "Backups",
            "page.storage" => fa ? "ذخیره‌سازی" : "Storage",
            "page.restore" => fa ? "بازیابی" : "Restore",
            "page.alerts" => fa ? "هشدارها" : "Alerts",
            "page.reports" => fa ? "گزارش‌ها" : "Reports",
            "page.mobile" => fa ? "اتصال موبایل" : "Mobile Connection",
            "page.settings" => fa ? "تنظیمات" : "Settings",
            "page.about" => fa ? "درباره OdinVault" : "About OdinVault",

            "shell.subtitle" => fa
                ? "کنترل سلامت، بکاپ‌ها و زیرساخت ذخیره‌سازی"
                : "Backup health, operations and storage control center",
            "agent.checking" => fa ? "در حال بررسی Agent..." : "Checking Agent...",

            "settings.language" => fa ? "زبان رابط کاربری" : "Interface language",
            "settings.language.fa" => "فارسی",
            "settings.language.en" => "English",
            "settings.language.note" => fa
                ? "جهت رابط کاربری بر اساس زبان تنظیم می‌شود: فارسی RTL و انگلیسی LTR."
                : "Layout direction follows the selected language: Persian RTL, English LTR.",
            "settings.apply" => fa ? "اعمال ظاهر و زبان" : "Apply appearance & language",
            "settings.theme" => fa ? "تم رابط کاربری" : "Interface theme",
            "settings.behavior" => fa ? "رفتار برنامه" : "Behavior",
            "settings.appearance" => fa ? "ظاهر و زبان" : "Appearance & language",
            "settings.agentMobile" => fa ? "Agent و اتصال موبایل" : "Agent & Mobile",
            "settings.updateAbout" => fa ? "بروزرسانی و درباره" : "Update & About",
            "settings.mobile.manage" => fa ? "مدیریت اتصال موبایل" : "Manage mobile connection",
            "settings.save" => fa ? "ذخیره تنظیمات" : "Save settings",

            "theme.light" => "Odin Light",
            "theme.dark" => "Odin Dark",

            _ => key
        };
    }
}
