using System.ComponentModel;

namespace Shelf.UI;

// Fluent 元件庫。全部自己畫、色彩一律取自 Theme。
//
// 面板裡的元件關掉 Selectable：可取得焦點的控制項被點擊時會 SetFocus → 啟用視窗 → 搶走使用者的焦點（6.1）。
// 對話框裡的元件（FluentButton、ToggleSwitch、Segmented）可取得焦點，支援 Tab 與鍵盤操作。

/// 自己畫的控制項基底
abstract class PaintedControl : Control
{
    protected PaintedControl(bool selectable = false)
    {
        SetStyle(ControlStyles.Selectable, selectable);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = selectable;
    }

    protected int L(int logical) => LogicalToDeviceUnits(logical);

    protected void ClearBackground(Graphics g) => g.Clear(Parent?.BackColor ?? Theme.Panel);

    /// Fluent 的鍵盤焦點框：控制項外圍 2 px 文字色框（只在用鍵盤操作時出現）
    protected void DrawFocus(Graphics g, Rectangle r, int radius)
    {
        if (Focused && ShowFocusCues) Draw.StrokeRounded(g, Theme.Text, r, radius + 2, 2);
    }
}

/// 點擊前先通知「需要鍵盤」，讓表單先拿掉 WS_EX_NOACTIVATE 並啟用視窗，SetFocus 才拿得到焦點
sealed class ActivatingTextBox : TextBox
{
    public event Action? ActivationNeeded;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_LBUTTONDOWN) ActivationNeeded?.Invoke();
        base.WndProc(ref m);
    }
}

/// Fluent 輸入框外框：包住一個無框 TextBox。取得焦點時底色變亮、下緣出現 2 px 強調色線
class InputBox : PaintedControl
{
    readonly string? _glyph;
    bool _hover;

    public InputBox(TextBox input, string? glyph = null)
    {
        Input = input;
        _glyph = glyph;
        input.BorderStyle = BorderStyle.None;
        input.Font = Theme.Body;
        Controls.Add(input);
        input.GotFocus += (_, _) => RefreshColors();
        input.LostFocus += (_, _) => RefreshColors();
        input.MouseEnter += (_, _) => SetHover(true);
        input.MouseLeave += (_, _) => SetHover(false);
        Height = L(32);
        RefreshColors();
    }

    public TextBox Input { get; }

    Color Fill => Input.Focused ? Theme.InputFocused : _hover ? Theme.ControlHover : Theme.ControlFill;

    /// 主題切換、焦點變化時呼叫：內層 TextBox 的底色要跟外框一致
    public void RefreshColors()
    {
        Input.BackColor = Fill;
        Input.ForeColor = Theme.Text;
        Invalidate();
    }

    void SetHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        RefreshColors();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); SetHover(true); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHover(false); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int left = L(11), right = _glyph is null ? L(11) : L(34);
        Input.Bounds = new Rectangle(left, (Height - Input.Height) / 2, Math.Max(0, Width - left - right), Input.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Input.Focus();   // 點到邊框或圖示也算點輸入框
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        var r = ClientRectangle;
        int radius = L(Theme.ControlRadius);
        Draw.Control(g, r, radius, Fill);
        if (Input.Focused)
        {
            // 下緣 2 px 強調色：Fluent 輸入框的招牌
            using var p = new Pen(Theme.Accent, L(2));
            int y = r.Bottom - L(1);
            g.DrawLine(p, r.Left + radius / 2, y, r.Right - radius / 2 - 1, y);
        }
        if (_glyph is not null)
            Draw.Glyph(g, _glyph, Theme.Icon, new Rectangle(r.Right - L(32), 0, L(28), Height), Theme.TextSecondary);
    }
}

enum Filter { All, Text, Image, File }

/// 分類列：融合抽屜的底部（球在上方時是頂部）那一排，和球同一排、同高 56。
/// 只有圖示（全部 · 文字 · 圖片 · 檔案），🔍 在最外側；滑鼠停一下顯示文字提示。選中的是淡強調色膠囊，切換時滑過去。
/// 點 🔍 進入搜尋：整排變成膠囊搜尋框 + 圓形 ✕，搜尋所有類型；✕ 回到分頁。
/// 靠球那一側要讓出月牙凹口的位置（NotchReserve），和清單之間一條淡分隔線。
sealed class TabBar : PaintedControl
{
    const int SlideMs = 200, FadeMs = 120, TipDelayMs = 500;
    const int SearchSlot = 4;   // _hover 用：0..3 是分頁，4 是 🔍 或 ✕

    static readonly (Filter Value, string Label, string Glyph)[] Tabs =
        [(Filter.All, "全部", Theme.GlyphAll), (Filter.Text, "文字", Theme.GlyphText),
         (Filter.Image, "圖片", Theme.GlyphPhoto), (Filter.File, "檔案", Theme.GlyphFolder)];

    readonly System.Windows.Forms.Timer _timer = new() { Interval = Anim.TickMs };
    readonly System.Windows.Forms.Timer _tipTimer = new() { Interval = TipDelayMs };
    readonly ToolTip _tip = new() { ShowAlways = true };   // ShowAlways：面板沒被啟用（NOACTIVATE）也要能顯示
    readonly float[] _hot = new float[Tabs.Length + 1];
    Filter _value;
    bool _searching, _notchOnLeft, _separatorOnTop = true;
    int _notchReserve, _hover = -1;
    float _slide = 1, _fromX;   // 選中膠囊從 _fromX 滑到目前分頁

    public TabBar()
    {
        Input.BorderStyle = BorderStyle.None;
        Input.Font = Theme.Body;
        Input.PlaceholderText = "搜尋…";
        Input.Visible = false;
        Controls.Add(Input);
        Input.ActivationNeeded += () => ActivationNeeded?.Invoke();
        Height = L(56);
        RefreshColors();
        _timer.Tick += (_, _) => Tick();
        _tipTimer.Tick += (_, _) => ShowTip();
    }

    /// 搜尋框。點下去前先通知表單進鍵盤模式（拿掉 NOACTIVATE），才拿得到焦點
    public readonly ActivatingTextBox Input = new();

    public event Action? Changed;            // 切換分頁
    public event Action? SearchRequested;    // 點了 🔍
    public event Action? SearchClosed;       // 點了 ✕
    public event Action? ActivationNeeded;

    public bool Searching => _searching;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Filter Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _fromX = HighlightX();
            _value = value;
            _slide = 0;
            _timer.Start();
            Changed?.Invoke();
        }
    }

    /// 依球的角落設定：凹口在哪一側、要讓出多寬；分隔線在上（分類列在底部）還是在下
    public void SetNotch(bool onLeft, int reserve, bool separatorOnTop)
    {
        (_notchOnLeft, _notchReserve, _separatorOnTop) = (onLeft, reserve, separatorOnTop);
        PerformLayout();
        Invalidate();
    }

    public void BeginSearch()
    {
        if (!_searching)
        {
            _searching = true;
            Input.Visible = true;
            HideTip();
            PerformLayout();
            Invalidate();
        }
        Input.Focus();
        Input.SelectAll();
    }

    public void EndSearch()
    {
        if (!_searching) return;
        _searching = false;
        Input.Clear();
        Input.Visible = false;
        Invalidate();
    }

    /// 主題切換時呼叫：內層 TextBox 的底色要跟搜尋框一致
    public void RefreshColors()
    {
        Input.BackColor = Theme.InputFocused;
        Input.ForeColor = Theme.Text;
        Invalidate();
    }

    // ── 版面：內容區扣掉凹口那一側；順序固定由左到右（全部 … 檔案，🔍 / ✕ 在最右），球在左邊也不鏡像 ──

    Rectangle Content
    {
        get
        {
            int pad = L(8), left = (_notchOnLeft ? _notchReserve : 0) + pad;
            int right = Width - (_notchOnLeft ? 0 : _notchReserve) - pad;
            return new Rectangle(left, 0, Math.Max(0, right - left), Height);
        }
    }

    int PillH => L(40);
    int PillTop => (Height - PillH) / 2;

    /// 🔍（搜尋時是 ✕）：最右邊
    Rectangle SideButton => new(Content.Right - PillH, PillTop, PillH, PillH);

    Rectangle TabRect(int i)
    {
        var c = Content;
        int w = Math.Min(L(56), (c.Width - PillH - L(8)) / Tabs.Length);
        return new Rectangle(c.Left + i * w, PillTop, w, PillH);
    }

    Rectangle Field => new(Content.Left, PillTop, SideButton.Left - L(8) - Content.Left, PillH);

    static int Index(Filter f) => Array.FindIndex(Tabs, t => t.Value == f);

    float HighlightX() => _slide >= 1 ? TabRect(Index(_value)).Left : Anim.Lerp(_fromX, TabRect(Index(_value)).Left, Anim.Ease(_slide));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var f = Field;
        int left = f.Left + L(36);
        Input.Bounds = new Rectangle(left, f.Top + (f.Height - Input.Height) / 2, Math.Max(0, f.Right - L(14) - left), Input.Height);
    }

    // ── 動畫 ──

    void Tick()
    {
        bool active = false;
        if (_slide < 1) { _slide = Anim.Step(_slide, 1, SlideMs); active = true; }
        for (int i = 0; i < _hot.Length; i++)
        {
            float target = i == _hover ? 1 : 0;
            _hot[i] = Anim.Step(_hot[i], target, FadeMs);
            active |= _hot[i] != target;
        }
        Invalidate();
        if (!active) _timer.Stop();
    }

    // ── 提示文字：滑鼠停在圖示上 0.5 秒才出現 ──

    void ShowTip()
    {
        _tipTimer.Stop();
        if (_searching || _hover < 0) return;
        var r = _hover == SearchSlot ? SideButton : TabRect(_hover);
        string text = _hover == SearchSlot ? "搜尋" : Tabs[_hover].Label;
        int y = _separatorOnTop ? r.Top - L(30) : r.Bottom + L(6);   // 往清單那一側冒出來
        _tip.Show(text, this, r.Left + r.Width / 2 - L(14), y, 1500);
    }

    void HideTip()
    {
        _tipTimer.Stop();
        _tip.Hide(this);
    }

    // ── 滑鼠 ──

    int HitTest(Point p)
    {
        if (SideButton.Contains(p)) return SearchSlot;
        if (_searching) return -1;
        for (int i = 0; i < Tabs.Length; i++)
            if (TabRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitTest(e.Location);
        if (i == _hover) return;
        _hover = i;
        _timer.Start();
        HideTip();
        if (i >= 0) _tipTimer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _timer.Start();
        HideTip();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HideTip();
        // 點到搜尋框的空白處（圖示旁邊）也算點搜尋框
        if (_searching && e.Button == MouseButtons.Left && Field.Contains(e.Location))
        {
            ActivationNeeded?.Invoke();
            Input.Focus();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitTest(e.Location);
        if (i == SearchSlot) (_searching ? SearchClosed : SearchRequested)?.Invoke();
        else if (i >= 0) Value = Tabs[i].Value;
    }

    // ── 繪製 ──

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);

        // 和清單之間的淡分隔線（不畫進凹口那一側）
        var c = Content;
        using (var p = new Pen(Theme.Divider))
        {
            int y = _separatorOnTop ? 0 : Height - 1;
            g.DrawLine(p, c.Left, y, c.Right, y);
        }

        var side = SideButton;
        float sideHot = Anim.Ease(_hot[SearchSlot]);

        if (_searching)
        {
            var f = Field;
            Draw.FillRounded(g, Theme.InputFocused, f, f.Height);
            Draw.StrokeRounded(g, Theme.Accent, f, f.Height);
            Draw.Glyph(g, Theme.GlyphSearch, Theme.Icon, new Rectangle(f.Left + L(10), f.Top, L(20), f.Height), Theme.TextSecondary);
            Draw.FillRounded(g, Anim.Lerp(Theme.SubtleHover, Theme.SubtleStrong, sideHot), side, side.Height);
            Draw.Glyph(g, Theme.GlyphClose, Theme.IconSmall, side, Theme.Text);
            return;
        }

        // 選中膠囊：淡強調色，切換時滑過去
        var selected = TabRect(Index(_value));
        Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.Accent, 0.15f), selected with { X = (int)HighlightX() }, selected.Height);

        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = TabRect(i);
            bool on = Tabs[i].Value == _value;
            float hot = Anim.Ease(_hot[i]);
            if (!on && hot > 0) Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.SubtleHover, hot), r, r.Height);
            Draw.Glyph(g, Tabs[i].Glyph, Theme.IconMedium, r, on ? Theme.Accent : Anim.Lerp(Theme.TextSecondary, Theme.Text, hot));
        }

        if (sideHot > 0) Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.SubtleHover, sideHot), side, side.Height);
        Draw.Glyph(g, Theme.GlyphSearch, Theme.IconMedium, side, Anim.Lerp(Theme.TextSecondary, Theme.Text, sideHot));
    }
}



/// 訊息列（Fluent InfoBar）：面板頂部，顯示錯誤或提醒，幾秒後由表單收起
sealed class MessageBar : PaintedControl
{
    public MessageBar() => Height = L(40);

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        var r = new Rectangle(L(8), L(4), Width - L(16), Height - L(8));
        Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.Caution, 0.12f), r, L(Theme.ControlRadius));
        Draw.StrokeRounded(g, Anim.Lerp(Theme.Panel, Theme.Caution, 0.3f), r, L(Theme.ControlRadius));
        Draw.Glyph(g, Theme.GlyphWarning, Theme.IconSmall, new Rectangle(r.Left + L(6), r.Top, L(20), r.Height), Theme.Caution);
        TextRenderer.DrawText(g, Text, Theme.Caption, new Rectangle(r.Left + L(30), r.Top, r.Width - L(36), r.Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

enum ButtonKind { Standard, Accent }

/// Fluent 按鈕（對話框用）。實作 IButtonControl，可當 AcceptButton / CancelButton
sealed class FluentButton : PaintedControl, IButtonControl
{
    readonly ButtonKind _kind;
    bool _hover, _pressed, _isDefault;

    public FluentButton(string text, ButtonKind kind = ButtonKind.Standard) : base(selectable: true)
    {
        Text = text;
        _kind = kind;
        Size = new Size(Math.Max(L(96), TextRenderer.MeasureText(text, Theme.Body).Width + L(24)), L(32));
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { _isDefault = value; Invalidate(); }

    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } f) f.DialogResult = DialogResult;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space) PerformClick();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        var r = Rectangle.Inflate(ClientRectangle, -L(2), -L(2));   // 外圈留給焦點框
        int radius = L(Theme.ControlRadius);
        Color text;
        if (_kind == ButtonKind.Accent)
        {
            var bg = Parent?.BackColor ?? Theme.Panel;
            Draw.FillRounded(g, _pressed ? Anim.Lerp(Theme.Accent, bg, 0.2f) : _hover ? Anim.Lerp(Theme.Accent, bg, 0.1f) : Theme.Accent, r, radius);
            text = Theme.OnAccent;
        }
        else
        {
            Draw.Control(g, r, radius, _pressed ? Theme.ControlPressed : _hover ? Theme.ControlHover : Theme.ControlFill);
            text = _pressed ? Theme.TextSecondary : Theme.Text;
        }
        TextRenderer.DrawText(g, Text, Theme.Body, r, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        DrawFocus(g, Rectangle.Inflate(r, L(2), L(2)), radius);
    }
}

/// Fluent 開關（ToggleSwitch）：左邊顯示「開啟 / 關閉」，圓鈕滑動
sealed class ToggleSwitch : PaintedControl
{
    const int SlideMs = 120;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = Anim.TickMs };
    bool _checked, _hover;
    float _pos;   // 0 = 關，1 = 開

    public ToggleSwitch() : base(selectable: true)
    {
        Size = new Size(L(40) + L(12) + TextRenderer.MeasureText("關閉", Theme.Body).Width, L(32));
        _timer.Tick += (_, _) =>
        {
            _pos = Anim.Step(_pos, _checked ? 1 : 0, SlideMs);
            if (_pos == (_checked ? 1 : 0)) _timer.Stop();
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            if (!IsHandleCreated) _pos = value ? 1 : 0;   // 初始值不跑動畫
            else _timer.Start();
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) Checked = !Checked;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        var track = new Rectangle(Width - L(40), (Height - L(20)) / 2, L(40), L(20));
        TextRenderer.DrawText(g, _checked ? "開啟" : "關閉", Theme.Body, new Rectangle(0, 0, track.Left - L(12), Height), Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        float on = Anim.Ease(_pos);
        var bg = Parent?.BackColor ?? Theme.Panel;
        if (on > 0) Draw.FillRounded(g, Anim.Lerp(bg, Theme.Accent, on), track, track.Height);
        if (on < 1)
        {
            if (_hover) Draw.FillRounded(g, Anim.Lerp(Theme.SubtleHover, Theme.Accent, on), track, track.Height);
            Draw.StrokeRounded(g, Anim.Lerp(Theme.TextSecondary, Theme.Accent, on), track, track.Height);
        }

        int knob = _hover ? L(14) : L(12);
        float x = Anim.Lerp(track.Left + L(4), track.Right - L(4) - knob, on);
        var knobColor = Anim.Lerp(Theme.TextSecondary, Theme.OnAccent, on);
        Draw.FillRounded(g, knobColor, new Rectangle((int)x, track.Top + (track.Height - knob) / 2, knob, knob), knob);
        DrawFocus(g, Rectangle.Inflate(track, L(3), L(3)), track.Height / 2);
    }
}

/// 分段選擇：淡色軌道裡一張浮起的白卡標出選中項目，切換時滑過去
sealed class Segmented : PaintedControl
{
    const int SlideMs = 160;
    readonly string[] _options;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = Anim.TickMs };
    int _index;
    float _slide = 1;
    Rectangle _from;

    public Segmented(params string[] options) : base(selectable: true)
    {
        _options = options;
        int w = options.Sum(o => TextRenderer.MeasureText(o, Theme.Body).Width + L(24)) + L(4);
        Size = new Size(w, L(32));
        _timer.Tick += (_, _) =>
        {
            _slide = Anim.Step(_slide, 1, SlideMs);
            if (_slide == 1) _timer.Stop();
            Invalidate();
        };
    }

    public event Action? Changed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _index;
        set
        {
            value = Math.Clamp(value, 0, _options.Length - 1);
            if (value == _index) return;
            _from = CurrentCard();
            _index = value;
            if (IsHandleCreated) { _slide = 0; _timer.Start(); }
            Invalidate();
            Changed?.Invoke();
        }
    }

    Rectangle Segment(int i)
    {
        int x = L(2);
        for (int k = 0; k < i; k++) x += TextRenderer.MeasureText(_options[k], Theme.Body).Width + L(24);
        return new Rectangle(x, L(2), TextRenderer.MeasureText(_options[i], Theme.Body).Width + L(24), Height - L(4));
    }

    Rectangle CurrentCard()
    {
        var to = Segment(_index);
        if (_slide >= 1) return to;
        float t = Anim.Ease(_slide);
        return new Rectangle((int)Anim.Lerp(_from.X, to.X, t), to.Y, (int)Anim.Lerp(_from.Width, to.Width, t), to.Height);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        for (int i = 0; i < _options.Length; i++)
            if (Segment(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        else if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        int radius = L(Theme.ControlRadius);
        var track = ClientRectangle;
        Draw.FillRounded(g, Theme.SubtleHover, track, radius);
        Draw.StrokeRounded(g, Theme.ControlStroke, track, radius);
        Draw.Control(g, CurrentCard(), radius - 1, Theme.ControlFill);

        for (int i = 0; i < _options.Length; i++)
            TextRenderer.DrawText(g, _options[i], Theme.Body, Segment(i), i == _index ? Theme.Text : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        DrawFocus(g, Rectangle.Inflate(track, L(2), L(2)), radius);
    }
}
