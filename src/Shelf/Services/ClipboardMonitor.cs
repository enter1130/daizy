using System.Runtime.InteropServices;

namespace Shelf.Services;

/// 剪貼簿監聽。視窗的 WndProc 收到 WM_CLIPBOARDUPDATE 時呼叫 ReadAsync。
sealed class ClipboardMonitor
{
    /// 程式自己寫入剪貼簿時附上的標記格式（F6）
    public const string SelfFormat = "Shelf.Self";

    public bool Paused { get; set; }

    public void Start(IntPtr hwnd) => Native.AddClipboardFormatListener(hwnd);

    public void Stop(IntPtr hwnd) => Native.RemoveClipboardFormatListener(hwnd);

    /// 回傳 null 表示這次不收：暫停中、讀不到、自己寫的、或來源要求不要記錄。
    public async Task<(IDataObject Data, AppInfo? Source)?> ReadAsync()
    {
        if (Paused) return null;

        // 先取來源：重試期間剪貼簿擁有者可能已經換人
        var source = SourceApp.FromWindow(Native.GetClipboardOwner());

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                return data is null || ShouldSkip(data) ? null : (data, source);
            }
            catch (ExternalException) when (attempt < 5)
            {
                // 其他程式正鎖著剪貼簿：Excel、遠端桌面，或剛寫入大圖、還在轉換格式的截圖工具。
                // 實測 50 ms × 3 次不夠，連續複製 4K 截圖會漏收
                await Task.Delay(100);
            }
            catch (ExternalException)
            {
                return null;            // 重試 5 次仍失敗就放棄這次；await 不會卡住 UI
            }
        }
    }

    static bool ShouldSkip(IDataObject data)
    {
        if (data.GetDataPresent(SelfFormat)) return true;

        // 密碼管理員等程式的隱私標記（F6b），Win+V 也遵守同一套
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (data.GetDataPresent("Clipboard Viewer Ignore")) return true;

        return data.GetDataPresent("CanIncludeInClipboardHistory")
            && data.GetData("CanIncludeInClipboardHistory") is MemoryStream { Length: >= 4 } ms
            && BitConverter.ToInt32(ms.ToArray(), 0) == 0;
    }
}
