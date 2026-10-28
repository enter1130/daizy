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

/// 分類列（設計稿 docs/ui-ios.html）：iOS 分頁列——分類膠囊（全部 · 文字 · 圖片 · 檔案，圖示 + 小字）+ 獨立的搜尋圓鈕。
/// 選中的是淡強調色膠囊，切換時滑過去。
/// 點搜尋：膠囊縮成左邊一顆圓鈕（顯示目前分類，點了回到分類），搜尋圓鈕展開成搜尋框，內含 ✕ 清空；搜尋所有類型。
/// 兩塊連續變形（_searchT 0 → 1），每一格通知表單重做外形。
/// 控制項高度 = 分類列 46 + 和卡片之間的空隙 8；空隙和兩塊之間的縫在面板 Region 之外，看不到也點不到。
sealed class TabBar : PaintedControl
{
    const int SlideMs = 200, FadeMs = 120, MorphMs = 260;
    const int RoundSlot = 4, ClearSlot = 5;   // _hover 用：0..3 是分類，4 是圓鈕（搜尋 / 回到分類），5 是 ✕

    static readonly (Filter Value, string Label, string Glyph)[] Tabs =
        [(Filter.All, "全部", Theme.GlyphAll), (Filter.Text, "文字", Theme.GlyphText),
         (Filter.Image, "圖片", Theme.GlyphPhoto), (Filter.File, "檔案", Theme.GlyphFolder)];

    readonly System.Windows.Forms.Timer _timer = new() { Interval = Anim.TickMs };
    readonly float[] _hot = new float[Tabs.Length + 2];
    Filter _value;
    bool _searching, _barOnTop;
    int _hover = -1;
    float _slide = 1, _fromX;   // 選中膠囊從 _fromX 滑到目前分類
    float _searchT;             // 搜尋變形的進度：0 = 分類，1 = 搜尋

    public TabBar()
    {
        Input.BorderStyle = BorderStyle.None;
        Input.Font = Theme.Body;
        Input.PlaceholderText = "搜尋…";
        Input.Visible = false;
        Controls.Add(Input);
        Input.ActivationNeeded += () => ActivationNeeded?.Invoke();
        Input.TextChanged += (_, _) => Invalidate();   // ✕ 有字才出現
        Height = L(DrawerShape.BarBand);
        RefreshColors();
        _timer.Tick += (_, _) => Tick();
    }

    /// 搜尋框。點下去前先通知表單進鍵盤模式（拿掉 NOACTIVATE），才拿得到焦點
    public readonly ActivatingTextBox Input = new();

    public event Action? Changed;            // 切換分類
    public event Action? SearchRequested;    // 點了搜尋圓鈕
    public event Action? SearchClosed;       // 搜尋中點了左邊的分類圓鈕
    public event Action? ShapeChanged;       // 變形動畫的每一格：面板外形要跟著換
    public event Action? ActivationNeeded;

    public bool Searching => _searching;

    /// 外形用的變形進度（已套緩動）
    public float SearchProgress => Anim.Ease(_searchT);

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

    /// 分類列在卡片上方（面板開在錨點下方）還是下方：決定空隙在哪一邊
    public void SetBarOnTop(bool onTop)
    {
        _barOnTop = onTop;
        PerformLayout();
        Invalidate();
    }

    /// animate = false：面板還沒出現（熱鍵叫出直接進搜尋），不播變形
    public void BeginSearch(bool animate = true)
    {
        if (!_searching)
        {
            _searching = true;
            Input.Visible = true;   // 要先顯示才拿得到焦點；寬度跟著搜尋框一起長出來
            StartMorph(animate);
        }
        Input.Focus();
        Input.SelectAll();
    }

    public void EndSearch(bool animate = true)
    {
        if (!_searching) return;
        _searching = false;
        Input.Clear();
        Input.Visible = false;
        StartMorph(animate);
    }

    void StartMorph(bool animate)
    {
        if (!animate) _searchT = _searching ? 1 : 0;
        _timer.Start();
        PerformLayout();
        Invalidate();
        ShapeChanged?.Invoke();
    }

    /// 主題切換時呼叫：內層 TextBox 的底色要跟搜尋框（膠囊的底色 = 面板色）一致
    public void RefreshColors()
    {
        Input.BackColor = Theme.Panel;
        Input.ForeColor = Theme.Text;
        Invalidate();
    }

    // ── 版面：和面板外形共用 DrawerShape.BarParts ──

    int BarTop => _barOnTop ? 0 : L(DrawerShape.Gap);

    (Rectangle Left, Rectangle Right) Parts
    {
        get
        {
            var (left, right) = DrawerShape.BarParts(Width, L(DrawerShape.BarHeight), L(DrawerShape.ButtonGap), SearchProgress);
            return (left with { Y = BarTop }, right with { Y = BarTop });
        }
    }

    /// 平常是右邊的搜尋圓鈕，搜尋中是左邊的分類圓鈕
    Rectangle Round => _searching ? Parts.Left : Parts.Right;

    Rectangle TabRect(int i)
    {
        var inner = Rectangle.Inflate(Parts.Left, -L(3), -L(3));
        int w = inner.Width / Tabs.Length;
        return new Rectangle(inner.Left + i * w, inner.Top, w, inner.Height);
    }

    /// 搜尋框右邊的 ✕
    Rectangle ClearButton
    {
        get
        {
            var f = Parts.Right;
            int s = L(24);
            return new Rectangle(f.Right - L(10) - s, f.Top + (f.Height - s) / 2, s, s);
        }
    }

    static int Index(Filter f) => Array.FindIndex(Tabs, t => t.Value == f);

    float HighlightX() => _slide >= 1 ? TabRect(Index(_value)).Left : Anim.Lerp(_fromX, TabRect(Index(_value)).Left, Anim.Ease(_slide));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var f = Parts.Right;
        int left = f.Left + L(38);
        Input.Bounds = new Rectangle(left, f.Top + (f.Height - Input.Height) / 2,
            Math.Max(0, ClearButton.Left - L(6) - left), Input.Height);
    }

    // ── 動畫 ──

    void Tick()
    {
        bool active = false;
        if (_slide < 1) { _slide = Anim.Step(_slide, 1, SlideMs); active = true; }
        float morphTarget = _searching ? 1 : 0;
        if (_searchT != morphTarget)
        {
            _searchT = Anim.Step(_searchT, morphTarget, MorphMs);
            PerformLayout();
            ShapeChanged?.Invoke();
            active |= _searchT != morphTarget;
        }
        for (int i = 0; i < _hot.Length; i++)
        {
            float target = i == _hover ? 1 : 0;
            _hot[i] = Anim.Step(_hot[i], target, FadeMs);
            active |= _hot[i] != target;
        }
        Invalidate();
        if (!active) _timer.Stop();
    }

    // ── 滑鼠 ──

    int HitTest(Point p)
    {
        if (Round.Contains(p)) return RoundSlot;
        if (_searching) return Input.TextLength > 0 && ClearButton.Contains(p) ? ClearSlot : -1;
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
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _timer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // 點到搜尋框的空白處（圖示旁邊）也算點搜尋框
        if (_searching && e.Button == MouseButtons.Left && Parts.Right.Contains(e.Location) && HitTest(e.Location) != ClearSlot)
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
        if (i == RoundSlot) (_searching ? SearchClosed : SearchRequested)?.Invoke();
        else if (i == ClearSlot) { ActivationNeeded?.Invoke(); Input.Clear(); Input.Focus(); }
        else if (i >= 0) Value = Tabs[i].Value;
    }

    // ── 繪製（兩塊的底色、外框、陰影由面板的外框層畫，這裡只畫裡面的東西）──
    // 變形前半段分類淡出，後半段左邊圓鈕的分類圖示淡入；搜尋圖示從圓鈕中央滑到搜尋框左邊

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ClearBackground(g);
        var (left, right) = Parts;
        float t = SearchProgress;

        var round = Round;
        float roundHot = Anim.Ease(_hot[RoundSlot]);
        var roundIn = Rectangle.Inflate(round, -L(3), -L(3));
        if (roundHot > 0 && round.Width == round.Height) Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.SubtleHover, roundHot), roundIn, roundIn.Height);

        // 左塊：分類（前半段淡出）或目前分類的圖示（後半段淡入）
        var state = g.Save();
        g.SetClip(left);
        if (t < 0.5f) DrawTabs(g, 1 - t * 2);
        else Draw.Glyph(g, Tabs[Index(_value)].Glyph, Theme.Icon, left,
            Anim.Lerp(Theme.Panel, Anim.Lerp(Theme.TextSecondary, Theme.Text, roundHot), t * 2 - 1));
        g.Restore(state);

        // 右塊：搜尋圖示從中央滑到左邊；搜尋框外框隨變形淡入
        int iconW = L(20);
        int iconX = (int)Anim.Lerp(right.Left + (right.Width - iconW) / 2f, right.Left + L(14), t);
        var searchColor = _searching ? Theme.TextSecondary : Anim.Lerp(Theme.TextSecondary, Theme.Text, roundHot);
        Draw.Glyph(g, Theme.GlyphSearch, Theme.Icon, new Rectangle(iconX, right.Top, iconW, right.Height), searchColor);
        if (t > 0)
        {
            var f = Rectangle.Inflate(right, -L(1), -L(1));
            Draw.StrokeRounded(g, Anim.Lerp(Theme.Panel, Theme.Accent, t), f, f.Height);
        }
        if (_searching && t >= 1 && Input.TextLength > 0)
        {
            var c = ClearButton;
            Draw.FillRounded(g, Anim.Lerp(Theme.SubtleHover, Theme.SubtleStrong, Anim.Ease(_hot[ClearSlot])), c, c.Height);
            Draw.Glyph(g, Theme.GlyphClose, Theme.IconSmall, c, Theme.TextSecondary);
        }
    }

    /// 分類膠囊的內容；opacity 用「往面板底色混」做淡出（TextRenderer 不支援透明度）
    void DrawTabs(Graphics g, float opacity)
    {
        var selected = TabRect(Index(_value));
        Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Anim.Lerp(Theme.Panel, Theme.Accent, 0.15f), opacity),
            selected with { X = (int)HighlightX() }, selected.Height);

        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = TabRect(i);
            bool on = Tabs[i].Value == _value;
            float hot = _searching ? 0 : Anim.Ease(_hot[i]);
            if (!on && hot > 0) Draw.FillRounded(g, Anim.Lerp(Theme.Panel, Theme.SubtleHover, hot), r, r.Height);
            var color = Anim.Lerp(Theme.Panel, on ? Theme.Accent : Anim.Lerp(Theme.TextSecondary, Theme.Text, hot), opacity);
            int mid = r.Top + r.Height / 2;
            Draw.Glyph(g, Tabs[i].Glyph, Theme.Icon, new Rectangle(r.Left, mid - L(16), r.Width, L(18)), color);
            TextRenderer.DrawText(g, Tabs[i].Label, Theme.Label, new Rectangle(r.Left, mid + L(2), r.Width, L(14)), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
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
