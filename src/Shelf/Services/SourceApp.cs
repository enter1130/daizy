using System.Diagnostics;
using System.Text;

namespace Shelf.Services;

/// 來源程式：顯示名稱（"Google Chrome"、"Microsoft Word"…，搜尋用）與 exe 路徑（清單上顯示圖示）
readonly record struct AppInfo(string Name, string Path);

/// 由視窗反查來源程式，依執行檔路徑快取名稱。
static class SourceApp
{
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// hwnd 為 0 時改用前景視窗。查不到或是自己時回傳 null。
    public static AppInfo? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

        // LIMITED_INFORMATION 對提權程式也開得起來，Process.MainModule 則會丟例外
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint len = (uint)sb.Capacity;
            if (!Native.QueryFullProcessImageName(handle, 0, sb, ref len)) return null;
            var path = sb.ToString();

            if (!Names.TryGetValue(path, out var name))
            {
                string? desc = null;
                try { desc = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch (FileNotFoundException) { }
                Names[path] = name = string.IsNullOrWhiteSpace(desc) ? System.IO.Path.GetFileNameWithoutExtension(path) : desc.Trim();
            }
            return new AppInfo(name, path);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
