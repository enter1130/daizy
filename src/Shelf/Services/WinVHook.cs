using System.Runtime.InteropServices;

namespace Shelf.Services;

/// 取代 Windows 的 Win+V（6.10）。Win+V 是系統保留的組合，RegisterHotKey 註冊不到，
/// 只能用低階鍵盤 hook 在系統處理之前攔下來：吃掉 V 的按下與放開，改通知 Shelf。
/// 只攔「Win + V」本身；同時按著 Ctrl / Alt / Shift 的組合（例如 Win+Alt+V）照常放行。
sealed class WinVHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const int VK_V = 0x56, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const byte VK_DUMMY = 0xE8;   // 未指定用途的虛擬鍵

    readonly Native.LowLevelMouseProc _proc;   // 鍵盤 hook 的回呼簽章和滑鼠一樣；要留住參考，否則被 GC 回收後 hook 會崩潰
    IntPtr _hook;
    bool _vDown;   // 這次的 V 已經攔下：之後的自動重複和放開也要吃掉

    public WinVHook() => _proc = Callback;

    /// 在 hook 回呼裡觸發，處理要快、重活請 BeginInvoke
    public event Action? Pressed;

    /// 必須在有訊息迴圈的 UI 執行緒設定
    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value && _hook == IntPtr.Zero)
                _hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            else if (!value && _hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    static bool Held(int vk) => Native.GetAsyncKeyState(vk) < 0;

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Marshal.ReadInt32(lParam) == VK_V)
        {            bool down = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (down && (_vDown || (Held(VK_LWIN) || Held(VK_RWIN)) && !Held(VK_CONTROL) && !Held(VK_MENU) && !Held(VK_SHIFT)))
            {
                if (!_vDown)
                {
                    _vDown = true;
                    // V 被吃掉，系統以為只按了 Win，放開時會打開開始功能表；
                    // 先送一個沒用途的鍵，讓系統知道 Win 已經和別的鍵組合過
                    Native.keybd_event(VK_DUMMY, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(VK_DUMMY, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Pressed?.Invoke();
                }
                return 1;
            }
            if (!down && _vDown)
            {
                _vDown = false;
                return 1;
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Enabled = false;
}
