using System.Diagnostics;
using System.Text;
using Shelf.UI;

namespace Shelf;

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
        using var mutex = new Mutex(true, @"Local\Shelf.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            NotifyRunningInstance();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShelfForm());
    }

    /// 找到另一個 Shelf 行程標題為 "Shelf" 的面板視窗，直接送訊息。
    /// 不用 HWND_BROADCAST：實測送不到（面板在啟動時是隱藏的）；只認同名行程，也不會誤送給別的程式裡叫 Shelf 的視窗。
    static void NotifyRunningInstance()
    {
        var self = Environment.ProcessId;
        var pids = Process.GetProcessesByName("Shelf").Select(p => (uint)p.Id).Where(id => id != self).ToHashSet();
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            var title = new StringBuilder(16);
            Native.GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() != "Shelf") return true;
            Native.PostMessage(hwnd, Native.WM_SHELF_SHOW, IntPtr.Zero, IntPtr.Zero);
            return false;
        }, IntPtr.Zero);
    }
}
