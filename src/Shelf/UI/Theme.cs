using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Shelf.Models;

namespace Shelf.UI;

/// 設計規範：Windows 11 Fluent（WinUI 3 的色彩、字級、圓角）。7.8
/// 色彩在繪製時才讀，切換深淺色、系統強調色後 Invalidate 即生效。
static class Theme
{
    public static bool Dark { get; private set; }

    // 系統強調色：淺色主題用 AccentDark1、深色主題用 AccentLight2（同 WinUI 的 AccentFillColorDefault）
    static Color _accentForLight = Rgb(0x005FB8), _accentForDark = Rgb(0x60CDFF);

    // ── 表面 ──
    public static Color Panel => Dark ? Rgb(0x2C2C2C) : Rgb(0xF9F9F9);           // 面板（Flyout）底色
    public static Color Window => Dark ? Rgb(0x202020) : Rgb(0xF3F3F3);          // 設定視窗、對話框頁尾
    public static Color Card => Dark ? Rgb(0x2B2B2B) : Rgb(0xFBFBFB);            // 設定卡片
    public static Color CardStroke => Dark ? Rgb(0x1C1C1C) : Rgb(0xE5E5E5);
    public static Color Dialog => Dark ? Rgb(0x2B2B2B) : Rgb(0xFFFFFF);          // 對話框內容區
    public static Color Divider => Dark ? Rgb(0x3D3D3D) : Rgb(0xE5E5E5);
    public static Color WindowBorder => Dark ? Rgb(0x404040) : Rgb(0xDCDCDC);    // DWM 視窗外框

    // ── 控制項 ──
    public static Color ControlFill => Dark ? Rgb(0x373737) : Rgb(0xFFFFFF);
    public static Color ControlHover => Dark ? Rgb(0x3D3D3D) : Rgb(0xF6F6F6);
    public static Color ControlPressed => Dark ? Rgb(0x323232) : Rgb(0xF0F0F0);
    public static Color ControlStroke => Dark ? Rgb(0x454545) : Rgb(0xE5E5E5);
    public static Color ControlStrokeBottom => Dark ? Rgb(0x3A3A3A) : Rgb(0xC4C4C4);   // 下緣較深，做出浮起感
    public static Color InputFocused => Dark ? Rgb(0x1F1F1F) : Rgb(0xFFFFFF);
    public static Color SubtleHover => Dark ? Rgb(0x383838) : Rgb(0xEDEDED);           // 透明按鈕、清單列懸停
    public static Color SubtlePressed => Dark ? Rgb(0x333333) : Rgb(0xF2F2F2);
    public static Color SubtleStrong => Dark ? Rgb(0x464646) : Rgb(0xE0E0E0);          // 懸停列上的按鈕懸停

    // ── 文字 ──
    public static Color Text => Dark ? Rgb(0xFFFFFF) : Rgb(0x1A1A1A);
    public static Color TextSecondary => Dark ? Rgb(0xC5C5C5) : Rgb(0x5F5F5F);
    public static Color TextTertiary => Dark ? Rgb(0x9A9A9A) : Rgb(0x8A8A8A);

    // ── 強調與狀態 ──
    public static Color Accent => Dark ? _accentForDark : _accentForLight;
    public static Color OnAccent => Dark ? Color.Black : Color.White;   // 強調色底上的文字（同 Windows 11）
    public static Color Danger => Dark ? Rgb(0xFF99A4) : Rgb(0xC42B1C);
    public static Color Caution => Dark ? Rgb(0xFCE100) : Rgb(0x9D5D00);   // 釘選
    public static Color Success => Dark ? Rgb(0x6CCB5F) : Rgb(0x0F7B0F);   // 拖檔案到球上

    // ── 字級（Segoe UI 會由系統字型連結補上微軟正黑體）──
    public static readonly Font Body = new("Segoe UI", 10.5f);              // 14 px
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Caption = new("Segoe UI", 9f);              // 12 px
    public static readonly Font CaptionStrong = new("Segoe UI Semibold", 9f);
    public static readonly Font Label = new("Segoe UI", 8.25f);             // 11 px：分類膠囊圖示下的小字
    public static readonly Font Subtitle = new("Segoe UI Semibold", 15f);   // 20 px

    // ── 圖示字型：Windows 11 是 Segoe Fluent Icons，Windows 10 是 Segoe MDL2 Assets，碼位相同 ──
    public static readonly string IconFontName = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons")
        ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    public static readonly Font Icon = new(IconFontName, 10.5f);
    public static readonly Font IconSmall = new(IconFontName, 9f);
    public static readonly Font IconMedium = new(IconFontName, 12.5f);   // 分類列只有圖示，放大一點
    public static readonly Font IconLarge = new(IconFontName, 24f);

    public const string GlyphSearch = "";
    public const string GlyphCopy = "";
    public const string GlyphPin = "";
    public const string GlyphUnpin = "";
    public const string GlyphPinned = "";
    public const string GlyphDelete = "";
    public const string GlyphClose = "";
    public const string GlyphSettings = "";
    public const string GlyphMore = "";
    public const string GlyphAll = "";
    public const string GlyphFolder = "";
    public const string GlyphWarning = "";
    public const string GlyphCheck = "";
    public const string GlyphClipboard = "";
    public const string GlyphText = "";
    public const string GlyphPhoto = "";
    public const string GlyphDownload = "";

    // ── 圓角（邏輯像素）──
    public const int ControlRadius = 4;
    public const int OverlayRadius = 8;

    static Color Rgb(int rgb) => Color.FromArgb(unchecked((int)0xFF000000) | rgb);

    public static void Apply(ThemeMode mode)
    {
        Dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => SystemPrefersDark(),
        };
        LoadAccent();
    }

    static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// AccentPalette 是 8 組 RGBA：Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, (未用)
    static void LoadAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        if (key?.GetValue("AccentPalette") is not byte[] { Length: >= 32 } p) return;
        _accentForDark = Color.FromArgb(p[4], p[5], p[6]);     // Light2
        _accentForLight = Color.FromArgb(p[16], p[17], p[18]); // Dark1
    }

    /// DWM 屬性用的 COLORREF（0x00BBGGRR）
    public static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}

/// 共用繪製：圓角、Fluent 控制項外觀
static class Draw
{
    /// .NET 的 FillRoundedRectangle / AddRoundedRectangle 參數名叫 radius，實際要的是圓角的「直徑」。
    /// 這裡一律收真正的半徑，換算成直徑，並限制在矩形短邊內（傳很大的值就是膠囊形）
    static Size Corner(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        return new Size(d, d);
    }

    public static void FillRounded(Graphics g, Color color, Rectangle r, int radius)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(color)) g.FillRoundedRectangle(b, r, Corner(r, radius));
        g.SmoothingMode = old;
    }

    public static void StrokeRounded(Graphics g, Color color, Rectangle r, int radius, float width = 1)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
        using (var p = new Pen(color, width))
            g.DrawRoundedRectangle(p, box, Corner(box, radius));
        g.SmoothingMode = old;
    }

    /// 圓角裁切路徑（圖片縮圖用）
    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        path.AddRoundedRectangle(r, Corner(r, radius));
        return path;
    }

    /// Fluent 標準控制項：填色 + 1 px 外框，下緣用較深的線做出浮起感
    public static void Control(Graphics g, Rectangle r, int radius, Color fill)
    {
        FillRounded(g, fill, r, radius);
        StrokeRounded(g, Theme.ControlStroke, r, radius);
        using var p = new Pen(Theme.ControlStrokeBottom);
        g.DrawLine(p, r.Left + radius, r.Bottom - 1, r.Right - radius - 1, r.Bottom - 1);
    }

    /// 選取指示條（清單左側、選擇列下方）
    public static void Pill(Graphics g, Rectangle r) => FillRounded(g, Theme.Accent, r, Math.Min(r.Width, r.Height));

    public static void Glyph(Graphics g, string glyph, Font font, Rectangle r, Color color) =>
        TextRenderer.DrawText(g, glyph, font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
}

/// 動畫共用：每 15 ms 一格，先快後慢（ease-out cubic）。7.10
static class Anim
{
    public const int TickMs = 15;

    public static float Ease(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

    /// 把 value 往 target 推一格；durationMs 是從 0 走到 1 的總時間
    public static float Step(float value, float target, int durationMs)
    {
        float step = (float)TickMs / durationMs;
        return value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
    }

    public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// Fluent 選單（MenuFlyout）：圓角、懸停圓角底色、項目高度 32。右鍵選單與系統匣選單共用
sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColors())
{
    /// 標成危險動作（紅字）的選單項目
    public const string DangerTag = "danger";

    /// 套用到一個選單：字型、間距、DWM 圓角與外框
    public static void Style(ContextMenuStrip menu, bool showCheckMargin = false)
    {
        menu.Renderer = new MenuRenderer();
        menu.Font = Theme.Body;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = showCheckMargin;
        menu.Padding = new Padding(4);
        menu.Opening += (_, _) =>
        {
            menu.BackColor = Theme.Panel;
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem) item.Padding = new Padding(4, 6, 12, 6);
        };
        menu.Opened += (_, _) =>
        {
            int round = 2, border = Theme.ColorRef(Theme.WindowBorder);
            Native.DwmSetWindowAttribute(menu.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            Native.DwmSetWindowAttribute(menu.Handle, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        };
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }   // 外框交給 DWM

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Panel);

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        Draw.FillRounded(e.Graphics, e.Item.Pressed ? Theme.SubtlePressed : Theme.SubtleHover, r, 4);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool shortcut = e.Item is ToolStripMenuItem m && e.Text == m.ShortcutKeyDisplayString;
        e.TextColor = !e.Item.Enabled ? Theme.TextTertiary
            : shortcut ? Theme.TextSecondary
            : Equals(e.Item.Tag, DangerTag) ? Theme.Danger
            : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Divider);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, -4, y, e.Item.Width + 4, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
        Draw.Glyph(e.Graphics, Theme.GlyphCheck, Theme.IconSmall, e.ImageRectangle, Theme.Text);

    sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color CheckBackground => Theme.Panel;
        public override Color CheckSelectedBackground => Theme.Panel;
        public override Color CheckPressedBackground => Theme.Panel;
    }
}

static class Fmt
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1048576.0:0.0} MB",
    };

    public static string Ago(DateTime t)
    {
        var now = DateTime.Now;
        var d = now - t;
        if (d.TotalMinutes < 1) return "剛剛";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分鐘前";
        if (t.Date == now.Date) return $"{(int)d.TotalHours} 小時前";
        if (t.Date == now.Date.AddDays(-1)) return $"昨天 {t:HH:mm}";
        return t.ToString("M/d HH:mm");
    }
}
