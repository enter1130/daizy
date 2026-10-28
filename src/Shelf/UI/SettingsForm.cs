using Shelf.Models;
using Shelf.Services;

namespace Shelf.UI;

/// 設定對話框（7.7），Windows 11「設定」App 的卡片樣式。一般的可啟用視窗，不需要 NOACTIVATE 那套。
sealed class SettingsForm : Form
{
    static readonly int[] Limits = [100, 200, 500, 1000];

    readonly Settings _s;
    readonly ToggleSwitch _autoStart, _collect;
    readonly HotkeyBox _hotkey;
    readonly Segmented _theme, _limit;
    readonly FlowLayoutPanel _body;

    public SettingsForm(Settings settings, string usage)
    {
        _s = settings;
        Text = "Shelf 設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        _body = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(L(20), L(8), L(20), L(16)),
        };

        Section("一般");
        Card("開機時自動啟動", null, _autoStart = new ToggleSwitch { Checked = settings.AutoStart });
        _hotkey = new HotkeyBox(settings.HotkeyModifiers, settings.HotkeyKey);
        Card("呼叫熱鍵", "點一下後直接按下新的組合鍵", new InputBox(_hotkey) { Width = L(170) });
        Card("外觀", null, _theme = new Segmented("跟隨系統", "淺色", "深色") { SelectedIndex = (int)settings.Theme });

        Section("收集");
        Card("自動收集剪貼簿", "密碼管理員複製的內容一律略過", _collect = new ToggleSwitch { Checked = settings.CollectClipboard });
        int limitIndex = Array.IndexOf(Limits, settings.Limit);
        Card("保留筆數上限", "超過時刪除最舊的未釘選項目",
            _limit = new Segmented([.. Limits.Select(n => n.ToString())]) { SelectedIndex = limitIndex < 0 ? 1 : limitIndex });

        Section("資料");
        var open = new FluentButton("開啟資料夾");
        open.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(Storage.ShelfStore.Root) { UseShellExecute = true });
        Card("儲存位置", Storage.ShelfStore.Root, open);
        Card("目前用量", usage, null);

        var save = new FluentButton("儲存", ButtonKind.Accent);
        save.Click += (_, _) => Save();
        var cancel = new FluentButton("取消") { DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = L(64),
            Padding = new Padding(L(20), L(14), L(18), 0),
            BackColor = Theme.Window,
        };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Divider); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };

        Controls.Add(_body);
        Controls.Add(footer);
        AcceptButton = save;
        CancelButton = cancel;
        ClientSize = new Size(L(520), _body.PreferredSize.Height + footer.Height);
    }

    public Settings Result => _s;

    int L(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 標題列跟內容同色，看起來是一整片（Windows 11）
        int dark = Theme.Dark ? 1 : 0, caption = Theme.ColorRef(Theme.Window), text = Theme.ColorRef(Theme.Text);
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    void Save()
    {
        _s.AutoStart = _autoStart.Checked;
        _s.HotkeyModifiers = _hotkey.Modifiers;
        _s.HotkeyKey = _hotkey.Key;
        _s.Theme = (ThemeMode)_theme.SelectedIndex;
        _s.CollectClipboard = _collect.Checked;
        _s.Limit = Limits[_limit.SelectedIndex];
        DialogResult = DialogResult.OK;
    }

    void Section(string title) =>
        _body.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            Margin = new Padding(L(2), L(16), 0, L(8)),
        });

    void Card(string title, string? description, Control? control) =>
        _body.Controls.Add(new SettingsCard(title, description, control)
        {
            Width = L(520) - L(40),
            Margin = new Padding(0, 0, 0, L(4)),
        });
}

/// 設定卡片：圓角卡片，左邊標題與說明，右邊控制項
sealed class SettingsCard : Panel
{
    readonly string _title;
    readonly string? _description;
    readonly Control? _control;

    public SettingsCard(string title, string? description, Control? control)
    {
        (_title, _description, _control) = (title, description, control);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = LogicalToDeviceUnits(description is null ? 60 : 68);
        if (control is not null) Controls.Add(control);
    }

    int L(int logical) => LogicalToDeviceUnits(logical);

    Rectangle TextArea => new(L(16), 0, (_control?.Left ?? Width) - L(32), Height);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_control is null) return;
        _control.Location = new Point(Width - L(16) - _control.Width, (Height - _control.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        int radius = L(Theme.ControlRadius);
        Draw.FillRounded(g, Theme.Card, ClientRectangle, radius);
        Draw.StrokeRounded(g, Theme.CardStroke, ClientRectangle, radius);

        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var area = TextArea;
        if (_description is null)
        {
            TextRenderer.DrawText(g, _title, Theme.Body, area, Theme.Text, flags | TextFormatFlags.VerticalCenter);
            return;
        }
        int titleH = Theme.Body.Height, descH = Theme.Caption.Height, top = (Height - titleH - descH - L(2)) / 2;
        TextRenderer.DrawText(g, _title, Theme.Body, area with { Y = top, Height = titleH }, Theme.Text, flags);
        TextRenderer.DrawText(g, _description, Theme.Caption, area with { Y = top + titleH + L(2), Height = descH }, Theme.TextSecondary, flags);
    }
}

/// 熱鍵錄製框：點一下後按下組合鍵。至少要有 Ctrl / Alt / Win 其中之一，單獨 Shift 會跟打字衝突。
sealed class HotkeyBox : TextBox
{
    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    public HotkeyBox(uint modifiers, uint key)
    {
        (Modifiers, Key) = (modifiers, key);
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        Text = HotkeyService.Format(modifiers, key);
    }

    public uint Modifiers { get; private set; }
    public uint Key { get; private set; }

    /// 所有按鍵都在這裡攔，不走 KeyDown：TextBox 會在 ProcessCmdKey 裡先吃掉 Ctrl+C / V / Z / A 等編輯快捷鍵
    /// （含加 Shift 的版本，例如 Ctrl+Shift+V），Alt+鍵 則會被當成選單助記鍵，都到不了 KeyDown。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        // 沒有修飾鍵的 Tab / Esc / Enter 留給對話框：切換焦點、取消、儲存
        if ((keyData & Keys.Modifiers) == 0 && key is Keys.Tab or Keys.Escape or Keys.Enter)
            return base.ProcessCmdKey(ref msg, keyData);

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((keyData & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        if (Native.GetAsyncKeyState(VK_LWIN) < 0 || Native.GetAsyncKeyState(VK_RWIN) < 0) mods |= Native.MOD_WIN;

        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            // 只按著修飾鍵：先顯示「Ctrl+Alt+…」，讓使用者知道有在錄
            Text = HotkeyService.FormatModifiers(mods) + "…";
            return true;
        }
        if ((mods & ~Native.MOD_SHIFT) == 0)
        {
            Text = "請加上 Ctrl、Alt 或 Win";
            return true;
        }
        (Modifiers, Key) = (mods, (uint)key);
        Text = HotkeyService.Format(mods, (uint)key);
        return true;
    }

    /// 放開修飾鍵但沒按主鍵：回到目前設定的值，不要停在「Ctrl+…」
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (Text.EndsWith('…')) Text = HotkeyService.Format(Modifiers, Key);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Text = HotkeyService.Format(Modifiers, Key);
    }
}
