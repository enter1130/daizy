namespace Shelf.Services;

/// 全域熱鍵。WM_HOTKEY 由視窗的 WndProc 接，wParam == Id 時觸發。
sealed class HotkeyService
{
    public const int Id = 0x5A11;
    const uint VK_V = 0x56;

    IntPtr _hwnd;

    /// 預設 Win+Alt+V。失敗（被其他程式佔用）回傳 false，呼叫端要提示使用者改用系統匣。
    public bool Register(IntPtr hwnd, uint modifiers = Native.MOD_WIN | Native.MOD_ALT, uint vk = VK_V)
    {
        Unregister();
        if (!Native.RegisterHotKey(hwnd, Id, modifiers | Native.MOD_NOREPEAT, vk)) return false;
        _hwnd = hwnd;
        return true;
    }

    public void Unregister()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.UnregisterHotKey(_hwnd, Id);
        _hwnd = IntPtr.Zero;
    }

    /// "Win+Alt+V" 這種顯示文字
    public static string Format(uint modifiers, uint vk)
    {
        var key = (Keys)vk;
        return FormatModifiers(modifiers) +
               (key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString());
    }

    /// "Win+Alt+" 這種前綴（錄製熱鍵時，只按著修飾鍵的即時顯示）
    public static string FormatModifiers(uint modifiers)
    {
        var s = "";
        if ((modifiers & Native.MOD_WIN) != 0) s += "Win+";
        if ((modifiers & Native.MOD_CONTROL) != 0) s += "Ctrl+";
        if ((modifiers & Native.MOD_ALT) != 0) s += "Alt+";
        if ((modifiers & Native.MOD_SHIFT) != 0) s += "Shift+";
        return s;
    }
}
