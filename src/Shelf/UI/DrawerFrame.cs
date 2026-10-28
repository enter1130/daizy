using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shelf.UI;

/// 抽屜的外框層：在內容視窗正後方，畫平滑的外形（含月牙凹口）、外框線與陰影。
///
/// 為什麼分兩層：月牙凹口和 22 px 圓角要平滑，只能用分層視窗逐像素畫（UpdateLayeredWindow）；
/// 但那種視窗畫不出子控制項（搜尋框）。所以外框自己一層，內容視窗疊在上面、用 Region 裁成往內縮 1.5 px 的同一個形狀——
/// Region 的鋸齒邊剛好落在外框的同色底上，看不出來。
/// 外框帶 WS_EX_TRANSPARENT：滑鼠直接穿透，點陰影等於點到後面的視窗。
sealed class DrawerFrame : Form
{
    const int ShadowMargin = 20;   // 外形四周留給陰影的空間（邏輯像素）

    Bitmap? _bitmap;

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

    /// 依抽屜大小與球的角落重畫外框（大小或主題改變時呼叫）
    public void SetShape(Size size, BallCorner corner)
    {
        int m = M;
        _bitmap?.Dispose();
        _bitmap = new Bitmap(size.Width + 2 * m, size.Height + 2 * m, PixelFormat.Format32bppArgb);
        float scale = DeviceDpi / 96f;
        using var g = Graphics.FromImage(_bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(m, m);
        using var path = DrawerShape.Build(size, corner, scale);

        // 柔和陰影：同一個外形往下偏一點，用由粗到細、很淡的黑色描邊疊出模糊感
        var shadowState = g.Save();
        g.TranslateTransform(0, 6 * scale);
        for (int i = 10; i >= 1; i--)
            using (var pen = new Pen(Color.FromArgb(5, 0, 0, 0), i * 2.4f * scale) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);
        g.Restore(shadowState);

        using (var fill = new SolidBrush(Theme.Panel)) g.FillPath(fill, path);
        using (var border = new Pen(Theme.WindowBorder, 1f)) g.DrawPath(border, path);
    }

    /// 放到抽屜本體的位置（螢幕座標）並套上透明度；動畫每一格呼叫
    public void Present(Rectangle drawer, byte alpha)
    {
        if (_bitmap is null || !IsHandleCreated) return;
        int m = M;
        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var hBitmap = _bitmap.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(memDc, hBitmap);
        try
        {
            var dst = new Native.POINT { X = drawer.X - m, Y = drawer.Y - m };
            var size = new Native.SIZE { Cx = _bitmap.Width, Cy = _bitmap.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap?.Dispose();
        base.Dispose(disposing);
    }
}
