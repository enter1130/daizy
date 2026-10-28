using System.Drawing.Drawing2D;

namespace Shelf.UI;

/// 球在抽屜的哪個角（抽屜往相反方向展開）
enum BallCorner { BottomRight, BottomLeft, TopRight, TopLeft }

/// 融合抽屜的外形（設計稿 docs/ui-lshape.html）：
/// 遠離分類列的兩角 22 px 圓角；分類列那一側離球最遠的角 28 px（分類列高度的一半，像膠囊的一端）；
/// 球那一角沿「圓心 = 球心、半徑 = 球半徑 + 8」的圓挖出月牙凹口，兩個尖角修圓。
///
/// 一律先以「球在右下」的座標畫，再依實際角落翻轉。
///
/// 球心在分類列那一排的正中央：離抽屜的兩個邊各 RowHalf。凹口半徑 = 球半徑 + Gap。
static class DrawerShape
{
    public const int TopRadius = 22, FootRadius = 28, RowHalf = 28, BallRadius = 24, Gap = 8;
    const float TipRound = 6;   // 凹口尖角修圓的角度（度）

    /// 凹口佔掉分類列多寬（從抽屜邊緣量起）：分類列內容要讓開這段
    public const int NotchReach = RowHalf + BallRadius + Gap;

    /// inset：整個外形往內縮多少像素（內容視窗用，讓鋸齒邊落在外框的同色底上）
    public static GraphicsPath Build(SizeF size, BallCorner corner, float scale, float inset = 0)
    {
        float w = size.Width, h = size.Height, d = inset;
        float rTop = TopRadius * scale - d, rFoot = FootRadius * scale - d;
        float off = RowHalf * scale, cut = (BallRadius + Gap) * scale + d;
        float cx = w - off, cy = h - off;                                   // 球心
        float horn = MathF.Sqrt(cut * cut - (off - d) * (off - d));        // 凹口和邊緣的交點離球心的距離

        // 凹口兩端（修圓後）在圓上的角度
        float aRight = Deg(MathF.Atan2(-horn, off - d)) - TipRound;         // 右緣那一端，往上
        float aBottom = Deg(MathF.Atan2(off - d, -horn)) + TipRound;        // 底緣那一端，往左
        var pRight = OnCircle(cx, cy, cut, aRight);
        var pBottom = OnCircle(cx, cy, cut, aBottom);

        var p = new GraphicsPath();
        p.AddLine(d + rTop, d, w - d - rTop, d);
        p.AddArc(w - d - 2 * rTop, d, 2 * rTop, 2 * rTop, -90, 90);                      // 右上
        var rightTip = new PointF(w - d, cy - horn);
        p.AddLine(w - d, d + rTop, w - d, rightTip.Y - 4 * scale);
        AddQuad(p, new PointF(w - d, rightTip.Y - 4 * scale), rightTip, pRight);       // 修圓右邊的尖角
        p.AddArc(cx - cut, cy - cut, 2 * cut, 2 * cut, aRight, (aBottom - 360) - aRight); // 凹口：逆時針繞過球的左上
        var bottomTip = new PointF(cx - horn, h - d);
        AddQuad(p, pBottom, bottomTip, new PointF(bottomTip.X - 4 * scale, h - d));    // 修圓底邊的尖角
        p.AddLine(bottomTip.X - 4 * scale, h - d, d + rFoot, h - d);
        p.AddArc(d, h - d - 2 * rFoot, 2 * rFoot, 2 * rFoot, 90, 90);                    // 左下（膠囊端）
        p.AddLine(d, h - d - rFoot, d, d + rTop);
        p.AddArc(d, d, 2 * rTop, 2 * rTop, 180, 90);                                     // 左上
        p.CloseFigure();

        // 翻到實際的角落
        bool flipX = corner is BallCorner.BottomLeft or BallCorner.TopLeft;
        bool flipY = corner is BallCorner.TopRight or BallCorner.TopLeft;
        if (flipX || flipY)
            using (var m = new Matrix(flipX ? -1 : 1, 0, 0, flipY ? -1 : 1, flipX ? w : 0, flipY ? h : 0))
                p.Transform(m);
        return p;
    }

    static float Deg(float rad) => rad * 180 / MathF.PI;

    static PointF OnCircle(float cx, float cy, float r, float deg) =>
        new(cx + r * MathF.Cos(deg * MathF.PI / 180), cy + r * MathF.Sin(deg * MathF.PI / 180));

    /// GraphicsPath 沒有二次貝茲曲線，換算成三次的
    static void AddQuad(GraphicsPath p, PointF p0, PointF q, PointF p2) =>
        p.AddBezier(p0,
            new PointF(p0.X + 2f / 3 * (q.X - p0.X), p0.Y + 2f / 3 * (q.Y - p0.Y)),
            new PointF(p2.X + 2f / 3 * (q.X - p2.X), p2.Y + 2f / 3 * (q.Y - p2.Y)),
            p2);
}
