using Microsoft.Win32;

namespace Shelf.Services;

/// 開機啟動（F20）：寫 HKCU\...\Run，不需要系統管理員權限。
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = Brand.Name;

    /// 改名前的登錄值：有的話刪掉，改寫成新的名稱、指向目前的 exe
    public static void MigrateLegacyValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(Brand.LegacyName) is not string) return;
        key.DeleteValue(Brand.LegacyName, throwOnMissingValue: false);
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
    }

    /// 以登錄檔為準：安裝程式也可能寫入這個值，不能只看 settings.json
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// 每次儲存設定都重寫一次，exe 搬了位置也會跟著更新
    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
