namespace OdinVault.Manager;

internal static class UiText
{
    public static bool IsPersian(string? language) =>
        !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

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
