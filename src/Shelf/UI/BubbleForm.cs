using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Shelf.UI;

/// 浮動球（6.7）：面板收起時留在螢幕上的圓球。
/// 可拖到任何位置；點一下展開面板；拖檔案到球上直接收進來。
///
/// 用分層視窗 + UpdateLayeredWindow 逐像素畫：圓的邊緣平滑，圓外完全透明的地方滑鼠會直接點穿。
/// 分層視窗不走 OnPaint，任何外觀變化都要呼叫 Render()。
sealed class BubbleForm : Form
{
    const int BallSize = 48, HotBallSize = 60, WindowSize = 72;   // 邏輯像素；視窗留邊給陰影與放大
    const int AnimationMs = 120;

    readonly Func<IDataObject, bool> _canAccept;
    readonly System.Windows.Forms.Timer _animation = new() { Interval = Anim.TickMs };
    Point _downAt, _origin;
    bool _down, _moved, _hot, _paused;
    float _heat;   // 0 = 平常，1 = 拖入中；動畫在兩者之間漸變

    public BubbleForm(Func<IDataObject, bool> canAccept)
    {
        _canAccept = canAccept;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AllowDrop = true;
        Size = new Size(LogicalToDeviceUnits(WindowSize), LogicalToDeviceUnits(WindowSize));
        _animation.Tick += (_, _) => Animate();
        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); _check = false; Render(); };
    }

    /// 點一下（沒有拖動）
    public event Action? Clicked;

    /// 右鍵點球，參數是螢幕座標：顯示釘住視窗、設定、清空等選單
    public event Action<Point>? RightClicked;

    /// 開始拖動球（超過 DragSize 的那一刻），呼叫端先把面板收起來
    public event Action? DragStarted;

    /// 拖動結束，參數是新位置（螢幕座標），呼叫端存起來
    public event Action<Point>? Moved;

    public event Action<IDataObject>? Dropped;

    readonly System.Windows.Forms.Timer _checkTimer = new() { Interval = 900 };
    bool _check;

    /// 複製成功：球上的圖示短暫變成 ✓（取代原本狀態列的「已複製」）
    public void ShowCheck()
    {
        _check = true;
        Render();
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    public bool Paused
    {
        get => _paused;
        set { if (_paused != value) { _paused = value; Render(); } }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
    }

    /// 建立 handle 時 WinForms 還會依 DPI 調整位置與大小，第一次畫的位置可能不對；顯示後再畫一次
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
    }

    /// 放到指定位置；不在任何螢幕的工作區內（例如那個螢幕拔掉了）就移到主螢幕右下
    public void PlaceAt(Point? saved)
    {
        var p = saved ?? DefaultLocation();
        if (!Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Rectangle(p, Size)))) p = DefaultLocation();
        Location = p;
        if (IsHandleCreated) Render();
    }

    Point DefaultLocation()
    {
        var work = Screen.PrimaryScreen!.WorkingArea;
        return new Point(work.Right - Width - LogicalToDeviceUnits(12), work.Top + work.Height * 2 / 3);
    }

    // ── 拖動與點擊 ──

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        (_down, _moved, _downAt, _origin) = (true, false, Cursor.Position, Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_down) return;
        var c = Cursor.Position;
        if (!_moved)
        {
            if (Math.Abs(c.X - _downAt.X) < SystemInformation.DragSize.Width &&
                Math.Abs(c.Y - _downAt.Y) < SystemInformation.DragSize.Height) return;
            _moved = true;
            DragStarted?.Invoke();
        }

        // 跟著游標走，但不超出游標所在螢幕的工作區
        var work = Screen.FromPoint(c).WorkingArea;
        int x = Math.Clamp(_origin.X + c.X - _downAt.X, work.Left, work.Right - Width);
        int y = Math.Clamp(_origin.Y + c.Y - _downAt.Y, work.Top, work.Bottom - Height);
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { RightClicked?.Invoke(Cursor.Position); return; }
        if (!_down || e.Button != MouseButtons.Left) return;
        _down = false;
        if (_moved) Moved?.Invoke(Location);
        else Clicked?.Invoke();
    }

    // ── 拖入：球放大變色，放開直接收 ──

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        bool ok = e.Data is { } d && _canAccept(d);
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetHot(ok);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        SetHot(false);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        SetHot(false);
        if (e.Data is { } d) Dropped?.Invoke(d);
    }

    void SetHot(bool hot)
    {
        if (_hot == hot) return;
        _hot = hot;
        _animation.Start();
    }

    void Animate()
    {
        float target = _hot ? 1f : 0f;
        _heat = Anim.Step(_heat, target, AnimationMs);
        if (_heat == target) _animation.Stop();
        Render();
    }

    // ── 繪製 ──

    public void Render()
    {
        if (!IsHandleCreated) return;
        int size = Width;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // 透明底不能用 ClearType
            PaintBall(g, size);
        }

        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(memDc, hBitmap);
        try
        {
            var dst = new Native.POINT { X = Left, Y = Top };
            var sz = new Native.SIZE { Cx = size, Cy = size };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };   // AC_SRC_OVER + AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    void PaintBall(Graphics g, int size)
    {
        float e = Anim.Ease(_heat);
        int d = LogicalToDeviceUnits((int)(BallSize + (HotBallSize - BallSize) * e));
        var ball = new Rectangle((size - d) / 2, (size - d) / 2, d, d);

        // 陰影：往下偏一點的幾圈淡黑
        for (int i = 4; i >= 1; i--)
        {
            int grow = LogicalToDeviceUnits(i);
            using var shadow = new SolidBrush(Color.FromArgb(14, 0, 0, 0));
            g.FillEllipse(shadow, Rectangle.Inflate(ball with { Y = ball.Y + LogicalToDeviceUnits(2) }, grow, grow));
        }

        // 平常是品牌紅底 + 口袋標誌；暫停收集變灰；拖入中漸變成綠色
        var normal = _paused ? Color.FromArgb(0x8A, 0x8A, 0x8A) : Logo.Brand;
        var color = Anim.Lerp(normal, Theme.Success, e);
        if (e <= 0.5f && !_check)
        {
            Logo.Draw(g, ball, color, Logo.Shape.Circle);
            return;
        }

        using (var fill = new SolidBrush(color)) g.FillEllipse(fill, ball);
        // 拖入中：向下箭頭；複製成功：勾勾。都用線條畫，不用圖示字型
        float s = d / 48f, cx = ball.Left + d / 2f, cy = ball.Top + d / 2f;
        using var pen = new Pen(Color.White, 3.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (e > 0.5f)
        {
            g.DrawLine(pen, cx, cy - 10 * s, cx, cy + 9 * s);
            g.DrawLines(pen, [new PointF(cx - 8 * s, cy + 1 * s), new PointF(cx, cy + 9 * s), new PointF(cx + 8 * s, cy + 1 * s)]);
        }
        else
        {
            g.DrawLines(pen, [new PointF(cx - 9 * s, cy + 1 * s), new PointF(cx - 3 * s, cy + 7 * s), new PointF(cx + 10 * s, cy - 6 * s)]);
        }
    }
}
