using System.Drawing.Drawing2D;

namespace Shelf.UI;

/// 面板的外形（設計稿 docs/ui-ios.html）：分成兩個部分，中間空開——
/// 內容卡片（22 px 圓角），和一排 iOS 分頁列：分類膠囊 + 獨立的搜尋圓鈕。
/// 搜尋中膠囊縮成左邊一顆圓鈕（回到分類），搜尋圓鈕展開成搜尋框；切換時兩塊連續變形（searchT 0 → 1）。
/// 分類列放在靠近游標（或球）的那一邊：barOnTop = 面板開在錨點下方。
static class DrawerShape
{
    public const int CardRadius = 22, BarHeight = 46, Gap = 8, ButtonGap = 8;

    /// 分類列（含和卡片之間的空隙）佔的高度
    public const int BarBand = BarHeight + Gap;

    /// 分類列那一排裡的兩塊，座標相對於那一排的左上：
    /// Left 平常是分類膠囊、搜尋中縮成圓鈕；Right 平常是搜尋圓鈕、搜尋中展開成搜尋框。t 是搜尋的動畫進度
    public static (Rectangle Left, Rectangle Right) BarParts(int width, int barH, int gap, float t)
    {
        int leftW = (int)Anim.Lerp(width - barH - gap, barH, t);
        int rightX = (int)Anim.Lerp(width - barH, barH + gap, t);
        return (new Rectangle(0, 0, leftW, barH), new Rectangle(rightX, 0, width - rightX, barH));
    }

    /// 三塊各自是一個封閉圖形。inset：整個外形往內縮多少像素（內容視窗用，讓鋸齒邊落在外框的同色底上）
    public static GraphicsPath Build(Size size, bool barOnTop, float searchT, float scale, float inset = 0)
    {
        int w = size.Width, h = size.Height;
        int barH = (int)(BarHeight * scale), band = (int)(BarBand * scale);
        int barY = barOnTop ? 0 : h - barH;
        var card = barOnTop ? new RectangleF(0, band, w, h - band) : new RectangleF(0, 0, w, h - band);
        var (left, right) = BarParts(w, barH, (int)(ButtonGap * scale), searchT);

        var p = new GraphicsPath(FillMode.Winding);
        AddRounded(p, card, CardRadius * scale, inset);
        AddRounded(p, left with { Y = barY }, barH / 2f, inset);
        AddRounded(p, right with { Y = barY }, barH / 2f, inset);
        return p;
    }

    static void AddRounded(GraphicsPath p, RectangleF r, float radius, float inset)
    {
        r.Inflate(-inset, -inset);
        float d = Math.Min(2 * (radius - inset), Math.Min(r.Width, r.Height));
        p.StartFigure();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
    }
}
