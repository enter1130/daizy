using System.Drawing.Drawing2D;
using Shelf.Models;
using Shelf.Storage;

namespace Shelf.UI;

enum RowAction { None, Pin, Delete }

/// 自繪清單，只畫可見列。
///
/// 不用 ListBox / ListView：它們被點擊時會呼叫 SetFocus，而 SetFocus 會啟用整個視窗，
/// 等於繞過 WS_EX_NOACTIVATE 搶走使用者的焦點（6.1）。這裡關掉 Selectable 就不會取得焦點。
///
/// 動畫：新項目從 0 高度長出、刪除的縮回 0（各 180 ms）；懸停底色漸變（120 ms）；複製後的閃光淡出（450 ms）。
/// 每一列有自己的高度進度，所以位置一律由 RowRects() 逐列累加，不能用 index × 列高。
///
/// 捲動自己管（不用 AutoScroll）：原生捲軸跟 Fluent 不搭。滾輪平滑捲動；
/// 捲軸是浮在內容上的細線，滑鼠在清單上才出現，靠近右緣變寬可拖曳，停止操作 1 秒後淡出。
sealed class ItemListView : Control
{
    const int MoveMs = 180, HoverMs = 120, FlashMs = 450, BarFadeMs = 150, BarIdleMs = 1000;

    const TextFormatFlags OneLine =
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

    // 只有釘選和刪除：單擊整列就是複製，不需要複製鈕
    static readonly RowAction[] Actions = [RowAction.Pin, RowAction.Delete];

    /// 顯示中的一列。Size：0 = 高度 0，1 = 完整；Removing 的列縮到 0 後移除
    sealed class Row(ShelfItem item, float size)
    {
        public ShelfItem Item = item;
        public float Size = size;
        public bool Removing;
    }

    readonly ThumbnailCache _thumbs;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = Anim.TickMs };
    readonly Dictionary<Guid, float> _hoverHeat = [];   // 用 Id 當鍵：ShelfItem 是 record，Pinned 一改雜湊值就變
    List<Row> _rows = [];
    IReadOnlyList<ShelfItem> _items = [];   // 不含正在刪除的列
    ShelfItem? _hover, _selected, _pressed, _flashed;
    RowAction _hoverAction;
    Point _pressedAt;
    float _flash;
    bool _dropActive;
    string? _dropHint;
    string[] _emptyLines = [];

    // 捲動與捲軸
    float _scroll, _scrollTarget;       // 目前位置與平滑捲動的目標（像素）
    float _barShow, _barWide;           // 捲軸顯示程度、變寬程度（0..1）
    long _lastScrollAt = long.MinValue / 2;
    bool _mouseInside, _barDrag;
    int _barDragOffset;

    public ItemListView(ThumbnailCache thumbs)
    {
        _thumbs = thumbs;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        _timer.Tick += (_, _) => Tick();
    }

    public event Action<ShelfItem>? ItemClicked;
    public event Action<ShelfItem, RowAction>? ActionClicked;
    public event Action<ShelfItem>? ItemDragStarted;
    public event Action<ShelfItem, Point>? ItemRightClicked;   // 螢幕座標

    int RowHeight => LogicalToDeviceUnits(56);

    public ShelfItem? SelectedItem => _selected;

    /// animate：收到新項目、刪除時 true；搜尋打字、切換篩選時 false（一次大量變動，動畫反而亂）
    public void SetItems(IReadOnlyList<ShelfItem> items, bool animate)
    {
        _items = items;
        _rows = animate ? Merge(_rows, items) : [.. items.Select(i => new Row(i, 1))];

        var ids = items.Select(i => i.Id).ToHashSet();
        if (_selected is not null && !ids.Contains(_selected.Id)) _selected = null;
        if (_hover is not null && !ids.Contains(_hover.Id)) _hover = null;

        if (!animate) _scroll = _scrollTarget = Math.Clamp(_scrollTarget, 0, MaxScroll);
        Invalidate();
        Kick();
    }

    /// 新清單的順序為準；舊列沿用動畫進度，新項目從 0 長出；
    /// 消失的項目先留在原來的相對位置（下一個留下來的項目前面），標成 Removing 慢慢縮掉
    static List<Row> Merge(List<Row> old, IReadOnlyList<ShelfItem> items)
    {
        var keep = items.Select(i => i.Id).ToHashSet();
        var existing = old.Where(r => keep.Contains(r.Item.Id)).ToDictionary(r => r.Item.Id);

        var removedBefore = new Dictionary<Guid, List<Row>>();
        var pending = new List<Row>();
        foreach (var r in old)
        {
            if (!keep.Contains(r.Item.Id))
            {
                r.Removing = true;
                pending.Add(r);
            }
            else if (pending.Count > 0)
            {
                removedBefore[r.Item.Id] = pending;
                pending = [];
            }
        }

        var result = new List<Row>(items.Count + old.Count);
        foreach (var item in items)
        {
            if (removedBefore.TryGetValue(item.Id, out var removed)) result.AddRange(removed);
            if (existing.TryGetValue(item.Id, out var row))
            {
                row.Item = item;
                row.Removing = false;
                result.Add(row);
            }
            else
            {
                result.Add(new Row(item, 0));
            }
        }
        result.AddRange(pending);
        return result;
    }

    /// 清單為空時中央顯示的文字，第一行粗體
    public void SetEmptyText(params string[] lines)
    {
        _emptyLines = lines;
        if (_items.Count == 0) Invalidate();
    }

    public void SetDropState(bool active, string? hint)
    {
        if (_dropActive == active && _dropHint == hint) return;
        (_dropActive, _dropHint) = (active, hint);
        Invalidate();
    }

    public void Flash(ShelfItem item)
    {
        (_flashed, _flash) = (item, 1f);
        Kick();
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        int i = _selected is null ? -1 : IndexOf(_selected);
        var selected = _items[i < 0 ? 0 : Math.Clamp(i + delta, 0, _items.Count - 1)];
        _selected = selected;

        // 捲到看得見
        foreach (var (row, rect) in RowRects())
        {
            if (row.Item.Id != selected.Id) continue;
            if (rect.Top < 0) ScrollTo(_scroll + rect.Top);
            else if (rect.Top + RowHeight > ClientSize.Height) ScrollTo(_scroll + rect.Top + RowHeight - ClientSize.Height);
            break;
        }
        Invalidate();
    }

    public void ClearSelection()
    {
        if (_selected is null) return;
        _selected = null;
        Invalidate();
    }

    int IndexOf(ShelfItem item)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Id == item.Id) return i;
        return -1;
    }

    // ── 動畫 ──

    void Kick()
    {
        if (!_timer.Enabled) _timer.Start();
    }

    /// 每格只重畫有變化的地方：整片重畫一次要畫十幾列圓角與文字，滑鼠一動就 60 fps 全畫會吃掉半顆 CPU
    void Tick()
    {
        bool active = false, full = false;
        long now = Environment.TickCount64;

        // 列長出 / 縮回：下面的列全部跟著移動，只能整片重畫
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var r = _rows[i];
            if (r.Size == (r.Removing ? 0 : 1)) continue;
            r.Size = Anim.Step(r.Size, r.Removing ? 0 : 1, MoveMs);
            if (r.Removing && r.Size == 0) _rows.RemoveAt(i);
            active = full = true;
        }

        // 平滑捲動：每格走剩下距離的 30%
        _scrollTarget = Math.Clamp(_scrollTarget, 0, MaxScroll);
        if (Math.Abs(_scrollTarget - _scroll) > 0.5f)
        {
            _scroll += (_scrollTarget - _scroll) * 0.3f;
            active = full = true;
        }
        else if (_scroll != _scrollTarget)
        {
            _scroll = _scrollTarget;
            full = true;
        }

        // 懸停底色、複製閃光：只重畫那一列
        var dirtyRows = new HashSet<Guid>();
        if (_hover is not null) _hoverHeat.TryAdd(_hover.Id, 0);
        foreach (var id in _hoverHeat.Keys.ToList())
        {
            float target = id == _hover?.Id ? 1 : 0;
            float heat = Anim.Step(_hoverHeat[id], target, HoverMs);
            if (heat != _hoverHeat[id]) dirtyRows.Add(id);
            if (heat == 0 && target == 0) _hoverHeat.Remove(id);
            else _hoverHeat[id] = heat;
            active |= heat != target;
        }
        if (_flash > 0 && _flashed is { } flashed)
        {
            _flash = Anim.Step(_flash, 0, FlashMs);
            dirtyRows.Add(flashed.Id);
            if (_flash == 0) _flashed = null;
            active = true;
        }

        // 捲軸：滑鼠在清單上、拖曳中、剛捲過 1 秒內才顯示；游標靠近右緣時變寬。只重畫右緣那一條
        bool recent = now - _lastScrollAt < BarIdleMs;
        float show = CanScroll && (_mouseInside || _barDrag || recent) ? 1 : 0;
        float wide = _barDrag || (_mouseInside && BarZone.Contains(PointToClient(Cursor.Position))) ? 1 : 0;
        float oldShow = _barShow, oldWide = _barWide;
        _barShow = Anim.Step(_barShow, show, BarFadeMs);
        _barWide = Anim.Step(_barWide, wide, BarFadeMs);
        active |= _barShow != show || _barWide != wide || recent;

        if (full) Invalidate();
        else
        {
            if (dirtyRows.Count > 0)
                foreach (var (row, rect) in RowRects())
                    if (dirtyRows.Contains(row.Item.Id)) Invalidate(rect);
            if (_barShow != oldShow || _barWide != oldWide) Invalidate(BarZone);
        }
        if (!active) _timer.Stop();
    }

    // ── 捲動 ──

    int ContentHeight => (int)Math.Ceiling(_rows.Sum(r => Anim.Ease(r.Size)) * RowHeight);

    float MaxScroll => Math.Max(0, ContentHeight - ClientSize.Height);

    bool CanScroll => MaxScroll > 0;

    void ScrollTo(float y)
    {
        _scrollTarget = Math.Clamp(y, 0, MaxScroll);
        _lastScrollAt = Environment.TickCount64;
        Kick();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_scrollTarget - e.Delta / 120f * RowHeight);   // 滾一格捲一列
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = _scrollTarget = Math.Clamp(_scrollTarget, 0, MaxScroll);
    }

    /// 右緣這一條是捲軸的感應區：游標進來捲軸變寬，按下去是拖捲軸而不是點項目
    Rectangle BarZone => new(ClientSize.Width - L(14), 0, L(14), ClientSize.Height);

    Rectangle Track => new(ClientSize.Width - L(10), L(4), L(8), ClientSize.Height - L(8));

    (int Top, int Height) Thumb()
    {
        var track = Track;
        int h = Math.Max(L(32), (int)((long)track.Height * ClientSize.Height / Math.Max(1, ContentHeight)));
        int top = track.Top + (int)((track.Height - h) * (MaxScroll == 0 ? 0 : _scroll / MaxScroll));
        return (top, h);
    }

    void ScrollToThumb(int thumbTop)
    {
        var track = Track;
        var (_, h) = Thumb();
        float t = Math.Clamp((float)(thumbTop - track.Top) / Math.Max(1, track.Height - h), 0, 1);
        _scroll = _scrollTarget = t * MaxScroll;   // 拖曳時直接跟手，不做平滑
        _lastScrollAt = Environment.TickCount64;
        Invalidate();
        Kick();
    }

    void DrawScrollBar(Graphics g)
    {
        if (_barShow <= 0 || !CanScroll) return;
        float show = Anim.Ease(_barShow), wide = Anim.Ease(_barWide);
        var (top, h) = Thumb();
        int w = (int)Anim.Lerp(L(2), L(6), wide);
        int right = ClientSize.Width - L(4);

        // 變寬時才露出淡淡的軌道
        if (wide > 0)
        {
            var track = Track;
            Draw.FillRounded(g, Color.FromArgb((int)(show * wide * 255), Theme.SubtleHover),
                new Rectangle(right - L(6), track.Top, L(6), track.Height), L(3));
        }
        var thumbColor = _barDrag ? Theme.TextSecondary : Theme.TextTertiary;
        Draw.FillRounded(g, Color.FromArgb((int)(show * 255), thumbColor), new Rectangle(right - w, top, w, h), w);
    }

    // ── 版面與命中測試 ──

    /// 每一列目前的矩形（控制項座標，已含捲動）。高度依動畫進度，0 到 RowHeight
    IEnumerable<(Row Row, Rectangle Rect)> RowRects()
    {
        float y = -_scroll;
        int rowH = RowHeight;
        foreach (var r in _rows)
        {
            float h = Anim.Ease(r.Size) * rowH;
            yield return (r, new Rectangle(0, (int)y, ClientSize.Width, (int)Math.Ceiling(h)));
            y += h;
        }
    }

    /// 游標下的項目（正在刪除的不算）與它的完整列矩形
    (ShelfItem Item, Rectangle Rect)? Hit(Point p)
    {
        foreach (var (row, rect) in RowRects())
        {
            if (rect.Top > p.Y) break;
            if (!row.Removing && rect.Contains(p)) return (row.Item, rect with { Height = RowHeight });
        }
        return null;
    }

    /// 懸停動作鈕：複製、釘選、刪除，靠右排；右邊留出捲軸的感應區
    Rectangle ActionRect(Rectangle row, int index)
    {
        int s = L(24), gap = L(2);
        int x = row.Right - L(16) - (Actions.Length - index) * s - (Actions.Length - 1 - index) * gap;
        return new Rectangle(x, row.Top + (row.Height - s) / 2, s, s);
    }

    RowAction ActionAt(Point p, Rectangle row)
    {
        for (int i = 0; i < Actions.Length; i++)
            if (ActionRect(row, i).Contains(p)) return Actions[i];
        return RowAction.None;
    }

    bool OnScrollBar(Point p) => CanScroll && BarZone.Contains(p);

    // ── 滑鼠 ──

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && OnScrollBar(e.Location))
        {
            // 按在滑塊上就抓著拖；按在軌道空白處就把滑塊中心跳過去
            var (top, h) = Thumb();
            _barDragOffset = e.Y >= top && e.Y < top + h ? e.Y - top : h / 2;
            _barDrag = true;
            ScrollToThumb(e.Y - _barDragOffset);
            _pressed = null;
            return;
        }
        // 按在動作鈕上不算開始拖曳
        _pressed = e.Button == MouseButtons.Left && Hit(e.Location) is { } h2 && ActionAt(e.Location, h2.Rect) == RowAction.None
            ? h2.Item : null;
        _pressedAt = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = null;
        if (_barDrag)
        {
            _barDrag = false;
            Kick();
            return;
        }
        if (e.Button == MouseButtons.Right && Hit(e.Location) is { } h)
            ItemRightClicked?.Invoke(h.Item, PointToScreen(e.Location));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _mouseInside = true;
        Kick();
    }

    bool _wasInBarZone;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // 只有狀態真的變了才啟動計時器，不要滑鼠一動就跑動畫
        bool inBar = BarZone.Contains(e.Location);
        if (!_mouseInside || inBar != _wasInBarZone) Kick();
        (_mouseInside, _wasInBarZone) = (true, inBar);
        if (_barDrag)
        {
            ScrollToThumb(e.Y - _barDragOffset);
            return;
        }

        var hit = OnScrollBar(e.Location) ? null : Hit(e.Location);
        var action = hit is { } h ? ActionAt(e.Location, h.Rect) : RowAction.None;
        SetHover(hit?.Item, action);

        if (_pressed is { } item && e.Button == MouseButtons.Left &&
            (Math.Abs(e.X - _pressedAt.X) >= SystemInformation.DragSize.Width ||
             Math.Abs(e.Y - _pressedAt.Y) >= SystemInformation.DragSize.Height))
        {
            _pressed = null;
            ItemDragStarted?.Invoke(item);   // DoDragDrop 會吃掉 MouseUp，所以拖曳後不會再觸發單擊
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        SetHover(null, RowAction.None);
    }

    void SetHover(ShelfItem? item, RowAction action)
    {
        if (item?.Id == _hover?.Id && action == _hoverAction) return;
        var old = _hover;
        (_hover, _hoverAction) = (item, action);
        // 只重畫舊的和新的那兩列（動作鈕出現 / 消失）；底色漸變由計時器接手
        foreach (var (row, rect) in RowRects())
            if (row.Item.Id == old?.Id || row.Item.Id == item?.Id) Invalidate(rect);
        Kick();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || OnScrollBar(e.Location) || Hit(e.Location) is not { } h) return;
        var action = ActionAt(e.Location, h.Rect);
        if (action == RowAction.None) ItemClicked?.Invoke(h.Item);
        else ActionClicked?.Invoke(h.Item, action);
    }

    // ── 繪製（Fluent 清單：無分隔線，懸停是內縮的圓角底色，選取時左側出現強調色指示條）──

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_dropActive ? Anim.Lerp(Theme.Panel, Theme.Accent, 0.08f) : Theme.Panel);

        if (_rows.Count == 0)
        {
            DrawEmpty(g);
        }
        else
        {
            foreach (var (row, rect) in RowRects())
            {
                if (rect.Bottom < 0 || rect.Height <= 0) continue;
                if (rect.Top > ClientSize.Height) break;
                if (!rect.IntersectsWith(e.ClipRectangle)) continue;   // 只畫需要重畫的列（懸停時通常只有一兩列）

                float visible = Anim.Ease(row.Size);
                if (visible >= 1)
                {
                    DrawRow(g, row, rect);
                    continue;
                }
                // 長出 / 縮回中：裁到目前高度，再蓋一層半透明底色當作淡入淡出（TextRenderer 不支援透明度）。
                // 裁切讓 TextRenderer 每次都要轉換區域、很貴，所以只用在動畫中的列
                var state = g.Save();
                g.SetClip(rect);
                DrawRow(g, row, rect with { Height = RowHeight });
                using (var veil = new SolidBrush(Color.FromArgb((int)((1 - visible) * 255), Theme.Panel)))
                    g.FillRectangle(veil, rect);
                g.Restore(state);
            }
        }

        DrawScrollBar(g);
        if (_dropActive) Draw.StrokeRounded(g, Theme.Accent, Rectangle.Inflate(ClientRectangle, -L(4), -L(4)), L(Theme.OverlayRadius), L(2));
    }

    int L(int logical) => LogicalToDeviceUnits(logical);

    void DrawRow(Graphics g, Row row, Rectangle r)
    {
        var item = row.Item;
        bool hover = item.Id == _hover?.Id && !row.Removing && !_dropActive;
        bool selected = item.Id == _selected?.Id;
        var card = Rectangle.Inflate(r, -L(4), -L(2));

        // 底色：懸停漸變；鍵盤選取同懸停色再加指示條；複製閃光是淡淡的強調色，慢慢退掉
        var bg = Anim.Lerp(Theme.Panel, Theme.SubtleHover, _dropActive ? 0 : selected ? 1 : _hoverHeat.GetValueOrDefault(item.Id));
        if (item.Id == _flashed?.Id) bg = Anim.Lerp(bg, Anim.Lerp(Theme.Panel, Theme.Accent, 0.18f), Anim.Ease(_flash));
        if (bg != Theme.Panel) Draw.FillRounded(g, bg, card, L(Theme.ControlRadius));
        if (selected) Draw.Pill(g, new Rectangle(card.Left, card.Top + (card.Height - L(16)) / 2, L(3), L(16)));

        int iconSize = L(36);
        var iconRect = new Rectangle(card.Left + L(8), r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
        DrawIcon(g, item, iconRect);

        int x = iconRect.Right + L(12);
        int right = hover ? ActionRect(r, 0).Left - L(4) : card.Right - L(10);
        int titleRight = right;
        if (item.Pinned && !hover)
        {
            // 釘選：標題列最右邊一個小圖釘
            var pin = new Rectangle(right - L(14), r.Top + L(9), L(14), L(20));
            Draw.Glyph(g, Theme.GlyphPinned, Theme.IconSmall, pin, Theme.Caution);
            titleRight = pin.Left - L(4);
        }
        TextRenderer.DrawText(g, item.Title, Theme.Body,
            new Rectangle(x, r.Top + L(8), titleRight - x, L(22)), Theme.Text, OneLine);
        // 次行：來源程式圖示 + 時間。類型看左邊的圖示，大小、字數用處不大，來源名稱又常被截斷
        int subX = x, subY = r.Top + L(30), subH = L(18);
        if (item.SourcePath is { } exe && FileIcons.App(exe) is { } appIcon)
        {
            int s = L(14);
            g.DrawIcon(appIcon, new Rectangle(subX, subY + (subH - s) / 2, s, s));
            subX += s + L(6);
        }
        TextRenderer.DrawText(g, Fmt.Ago(item.CreatedAt), Theme.Caption,
            new Rectangle(subX, subY, right - subX, subH), Theme.TextSecondary, OneLine);

        if (hover) DrawActions(g, item, r);
    }

    void DrawActions(Graphics g, ShelfItem item, Rectangle row)
    {
        for (int i = 0; i < Actions.Length; i++)
        {
            var a = Actions[i];
            var r = ActionRect(row, i);
            bool hot = a == _hoverAction;
            if (hot) Draw.FillRounded(g, Theme.SubtleStrong, r, L(Theme.ControlRadius));
            var glyph = a == RowAction.Pin ? (item.Pinned ? Theme.GlyphUnpin : Theme.GlyphPin) : Theme.GlyphDelete;
            var color = a == RowAction.Delete && hot ? Theme.Danger
                : a == RowAction.Pin && item.Pinned ? Theme.Caution
                : hot ? Theme.Text : Theme.TextSecondary;
            Draw.Glyph(g, glyph, Theme.IconSmall, r, color);
        }
    }

    void DrawIcon(Graphics g, ShelfItem item, Rectangle r)
    {
        int radius = L(Theme.ControlRadius);

        // 圖片：縮圖置中裁成正方形，圓角
        if (item.Kind == ItemKind.Image && _thumbs.Get(item) is { } img)
        {
            int side = Math.Min(img.Width, img.Height);
            using var path = Draw.RoundedPath(r, radius);
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, r, new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            g.Restore(state);
            Draw.StrokeRounded(g, Theme.ControlStroke, r, radius);
            return;
        }

        Draw.FillRounded(g, Theme.ControlFill, r, radius);
        Draw.StrokeRounded(g, Theme.ControlStroke, r, radius);

        if (item.Kind == ItemKind.File && FileIcons.Get(item) is { } icon)
        {
            int s = L(24);
            g.DrawIcon(icon, new Rectangle(r.Left + (r.Width - s) / 2, r.Top + (r.Height - s) / 2, s, s));
            return;
        }

        // 文字、或縮圖遺失的圖片：Fluent 圖示
        Draw.Glyph(g, item.Kind == ItemKind.Image ? Theme.GlyphPhoto : Theme.GlyphText, Theme.Icon, r, Theme.TextSecondary);
    }

    /// 空狀態（7.4）、搜尋無結果、拖入中且清單為空（7.3）：大圖示 + 標題 + 說明，置中
    void DrawEmpty(Graphics g)
    {
        string[] lines = _dropActive ? ["放開以加入", _dropHint ?? ""] : _emptyLines;
        int iconH = L(48), titleH = Theme.BodyStrong.Height + L(6), lineH = Theme.Caption.Height + L(4);
        int total = iconH + L(12) + titleH + (lines.Length - 1) * lineH;
        int y = (ClientSize.Height - total) / 2, pad = L(24);

        Draw.Glyph(g, _dropActive ? Theme.GlyphDownload : Theme.GlyphClipboard, Theme.IconLarge,
            new Rectangle(0, y, ClientSize.Width, iconH), _dropActive ? Theme.Accent : Theme.TextTertiary);
        y += iconH + L(12);

        var flags = OneLine | TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, lines.FirstOrDefault() ?? "", Theme.BodyStrong, new Rectangle(pad, y, ClientSize.Width - pad * 2, titleH), Theme.Text, flags);
        y += titleH;
        foreach (var line in lines.Skip(1))
        {
            TextRenderer.DrawText(g, line, Theme.Caption, new Rectangle(pad, y, ClientSize.Width - pad * 2, lineH), Theme.TextSecondary, flags);
            y += lineH;
        }
    }
}
