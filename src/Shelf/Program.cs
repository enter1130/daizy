using System.Diagnostics;
using System.Text;
using Shelf.UI;

namespace Shelf;

/// 產品名稱（2026-10-28 由暫定的 Shelf 改名）：Daizy，念起來像「袋子」。
/// 程式碼的命名空間、類別名稱仍用 Shelf，只有使用者看得到的地方用這個
static class Brand
{
    public const string Name = "Daizy";

    /// 改名前的名稱：資料夾、開機啟動的登錄值要從這裡搬過來
    public const string LegacyName = "Shelf";
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 開發用：重新產生程式圖示（改了 Logo 之後執行一次）
        if (args is ["--export-icon", var icoPath])
        {
            Logo.SaveIco(icoPath);
            return;
        }

        // 單實例：第二次啟動只通知既有實例把面板叫出來
        using var mutex = new Mutex(true, $@"Local\{Brand.Name}.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            NotifyRunningInstance();
            return;
        }

        // 改名前的資料（%LOCALAPPDATA%\Shelf）和開機啟動設定搬過來；要在讀設定與清單之前
        Storage.ShelfStore.MigrateLegacyFolder();
        Services.AutoStart.MigrateLegacyValue();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShelfForm());
    }

    /// 找到另一個 Daizy 行程標題為 "Daizy" 的面板視窗，直接送訊息。
    /// 不用 HWND_BROADCAST：實測送不到（面板在啟動時是隱藏的）；只認同名行程，也不會誤送給別的程式裡叫 Daizy 的視窗。
    static void NotifyRunningInstance()
    {
        var self = Environment.ProcessId;
        var pids = Process.GetProcessesByName(Brand.Name).Select(p => (uint)p.Id).Where(id => id != self).ToHashSet();
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            var title = new StringBuilder(16);
            Native.GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() != Brand.Name) return true;
            Native.PostMessage(hwnd, Native.WM_SHELF_SHOW, IntPtr.Zero, IntPtr.Zero);
            return false;
        }, IntPtr.Zero);
    }
}
