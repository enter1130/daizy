using System.Runtime.InteropServices;

namespace Shelf.Services;

/// 面板展開期間偵測「點到面板外面」。面板帶 WS_EX_NOACTIVATE，沒被啟用也就不會收到 Deactivate，
/// 只能用低階滑鼠 hook。只在面板展開時掛上，收起就拆掉。
sealed class OutsideClickWatcher : IDisposable
{
    readonly Native.LowLevelMouseProc _proc;   // 要留住參考，否則被 GC 回收後 hook 回呼會崩潰
    IntPtr _hook;

    public OutsideClickWatcher() => _proc = Callback;

    /// 任何滑鼠按鍵按下，參數是螢幕座標（實體像素）。在 hook 回呼裡觸發，處理要快、重活請 BeginInvoke。
    public event Action<Point>? ButtonDown;

    /// 必須在有訊息迴圈的 UI 執行緒呼叫
    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            ButtonDown?.Invoke(new Point(info.pt.X, info.pt.Y));
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
