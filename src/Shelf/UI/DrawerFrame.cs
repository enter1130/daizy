using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shelf.UI;

/// 面板的外框層：在內容視窗正後方，畫平滑的外形（卡片 + 分類膠囊 + 圓鈕）、外框線與陰影。
///
/// 為什麼分兩層：圓角要平滑，只能用分層視窗逐像素畫（UpdateLayeredWindow）；
/// 但那種視窗畫不出子控制項（搜尋框）。所以外框自己一層，內容視窗疊在上面、用 Region 裁成往內縮 1.5 px 的同一個形狀——
/// Region 的鋸齒邊剛好落在外框的同色底上，看不出來。
/// 外框帶 WS_EX_TRANSPARENT：滑鼠直接穿透，點陰影等於點到後面的視窗。
///
/// 畫布是一張 DIB section，GDI+ 直接畫在它的記憶體上，UpdateLayeredWindow 也直接用它：
/// 搜尋切換的變形動畫每格都要重畫，但大小不變就不配置任何新東西（逐格配置點陣圖會讓原生記憶體越積越多，見開發文件 1.3）。
sealed class DrawerFrame : Form
{
    const int ShadowMargin = 20;   // 外形四周留給陰影的空間（邏輯像素）

    IntPtr _dib, _bits;     // 畫布（32 位元、由上而下、預乘 alpha）
    Size _dibSize;
    Bitmap? _canvas;        // 包住 _bits 的 GDI+ 點陣圖，不另外配置像素

    public DrawerFrame()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST |
                          Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    int M => LogicalToDeviceUnits(ShadowMargin);

    /// 重畫外框（大小、分類列位置、搜尋動畫、主題改變時呼叫）
    public void SetShape(Size size, bool barOnTop, float searchT)
    {
        int m = M;
        var canvas = EnsureCanvas(new Size(size.Width + 2 * m, size.Height + 2 * m));
        float scale = DeviceDpi / 96f;
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(m, m);
        using var path = DrawerShape.Build(size, barOnTop, searchT, scale);

        // 柔和陰影：四周平均、只往下偏 2 px、短而淡（由粗到細的淡黑描邊疊出模糊感）。
        // 所有陰影先畫完才填底色，所以卡片的陰影不會蓋到分類列，反之亦然
        var shadowState = g.Save();
        g.TranslateTransform(0, 2 * scale);
        for (int i = 8; i >= 1; i--)
            using (var pen = new Pen(Color.FromArgb(6, 0, 0, 0), i * 1.4f * scale) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);
        g.Restore(shadowState);

        using (var fill = new SolidBrush(Theme.Panel)) g.FillPath(fill, path);
        using (var border = new Pen(Theme.WindowBorder, 1f)) g.DrawPath(border, path);
    }

    Bitmap EnsureCanvas(Size size)
    {
        if (_canvas is not null && size == _dibSize) return _canvas;
        FreeCanvas();
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = size.Width,
            biHeight = -size.Height,   // 負數：由上而下，和 GDI+ 的列順序一致
            biPlanes = 1,
            biBitCount = 32,
        };
        _dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        _dibSize = size;
        _canvas = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, _bits);
        return _canvas;
    }

    void FreeCanvas()
    {
        _canvas?.Dispose();
        _canvas = null;
        if (_dib != IntPtr.Zero) Native.DeleteObject(_dib);
        (_dib, _bits) = (IntPtr.Zero, IntPtr.Zero);
    }

    /// 放到面板本體的位置（螢幕座標）並套上透明度；動畫每一格呼叫
    public void Present(Rectangle drawer, byte alpha)
    {
        if (_dib == IntPtr.Zero || !IsHandleCreated) return;
        int m = M;
        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var old = Native.SelectObject(memDc, _dib);
        try
        {
            var dst = new Native.POINT { X = drawer.X - m, Y = drawer.Y - m };
            var size = new Native.SIZE { Cx = _dibSize.Width, Cy = _dibSize.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        FreeCanvas();
        base.Dispose(disposing);
    }
}
