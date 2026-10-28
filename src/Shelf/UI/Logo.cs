using System.Drawing.Drawing2D;

namespace Shelf.UI;

/// Shelf 標誌「口袋」：一張卡片插在口袋裡，露出上半截（先收起來，等一下再拿）。
/// 浮動球、系統匣、程式圖示都用這裡畫，形狀才會一致。設計稿見 docs/logo-options.html 方案 1。
///
/// 座標以 48 × 48 的格子定義，畫的時候縮放到目標大小。扁平：只有底色、白、底色加深一階。
static class Logo
{
    /// 品牌色：Fluent 色票的紅。標誌固定用這個，不跟系統強調色走
    public static readonly Color Brand = Color.FromArgb(0xC4, 0x31, 0x4B);

    /// 底形：浮動球用圓形，程式圖示與系統匣用圓角方塊
    public enum Shape { Circle, RoundedSquare }

    /// 畫完整標誌（底形 + 口袋）
    public static void Draw(Graphics g, RectangleF bounds, Color background, Shape shape)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(background))
        {
            if (shape == Shape.Circle) g.FillEllipse(bg, bounds);
            else
            {
                float r = bounds.Width * 11 / 48f;
                using var path = new GraphicsPath();
                path.AddRoundedRectangle(bounds, new SizeF(r * 2, r * 2));
                g.FillPath(bg, path);
            }
        }
        DrawMark(g, bounds, background);
        g.SmoothingMode = old;
    }

    /// 只畫口袋圖形（不含底形）。background 用來算口袋的深色與卡片上的標籤色
    public static void DrawMark(Graphics g, RectangleF bounds, Color background)
    {
        var state = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y);
        g.ScaleTransform(bounds.Width / 48f, bounds.Height / 48f);
        bool detailed = bounds.Width >= 32;   // 16、24 px 時省略標籤與縫線，不然糊成一團

        // 卡片：往左傾 9°
        var card = g.Save();
        g.TranslateTransform(22, 17);
        g.RotateTransform(-9);
        g.TranslateTransform(-22, -17);
        using (var white = new SolidBrush(Color.White))
        using (var path = Rounded(15, 7, 15, 20, 3))
            g.FillPath(white, path);
        if (detailed)
            using (var label = new SolidBrush(Color.FromArgb(140, background)))
            using (var path = Rounded(19.5f, 10.5f, 7, 2.4f, 1.2f))
                g.FillPath(label, path);
        g.Restore(card);

        // 口袋：底色加深一階的實心塊，下緣是尖的，擋住卡片下半截
        using (var pocket = new GraphicsPath())
        {
            pocket.AddLine(11, 21, 37, 21);
            pocket.AddLine(37, 21, 37, 30.5f);
            pocket.AddBezier(37, 30.5f, 37, 31.6f, 36.4f, 32.6f, 35.4f, 33.2f);
            pocket.AddLine(35.4f, 33.2f, 24, 39.4f);
            pocket.AddLine(24, 39.4f, 12.6f, 33.2f);
            pocket.AddBezier(12.6f, 33.2f, 11.6f, 32.6f, 11, 31.6f, 11, 30.5f);
            pocket.CloseFigure();
            using var fill = new SolidBrush(Darken(background, 0.28f));
            g.FillPath(fill, pocket);
        }

        // 口袋袋口的白線；大尺寸再加一排縫線
        using (var rim = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(rim, 11, 21, 37, 21);
        if (detailed)
        {
            // 5 段等距短線。不用 DashPattern：虛線兩端會被截成半圓，大尺寸很明顯
            using var stitch = new Pen(Color.FromArgb(90, Color.White), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (float x = 15.5f; x < 33; x += 4.25f)
                g.DrawLine(stitch, x, 26.5f, x + 1.4f, 26.5f);
        }

        g.Restore(state);
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddRoundedRectangle(new RectangleF(x, y, w, h), new SizeF(r * 2, r * 2));
        return p;
    }

    public static Color Darken(Color c, float k) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)));

    /// 輸出程式圖示 .ico（含 16 到 256 px 共 8 種尺寸，每張是 PNG）。
    /// 由 `Shelf.exe --export-icon <路徑>` 呼叫，讓 Shelf.ico 和球、系統匣出自同一份繪製程式
    public static void SaveIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var pngs = sizes.Select(s =>
        {
            using var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
                Draw(g, new RectangleF(0, 0, s, s), Brand, Shape.RoundedSquare);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        // ICO：6 bytes 檔頭 + 每個尺寸 16 bytes 目錄 + PNG 資料
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);   // 256 在目錄裡記成 0
            w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32); w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    /// 系統匣、視窗用的 Icon。size 用系統小圖示尺寸（150% 縮放時是 24），避免被縮放糊掉
    public static Icon CreateIcon(int size, Color background)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
            Draw(g, new RectangleF(0, 0, size, size), background, Shape.RoundedSquare);
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();   // Clone 自己持有一份，原本的 handle 要手動釋放
        Native.DestroyIcon(handle);
        return icon;
    }
}
