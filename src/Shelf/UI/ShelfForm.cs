using System.Diagnostics;
using System.Runtime.InteropServices;
using Shelf.Models;
using Shelf.Services;
using Shelf.Storage;

namespace Shelf.UI;

/// 主面板：無框、置頂、不出現在工作列與 Alt+Tab、點擊不搶焦點（6.1）。
/// 平常隱藏，只留浮動球（BubbleForm）；點球、按熱鍵才在球旁邊展開（6.7）。
sealed class ShelfForm : Form
{
    readonly HotkeyService _hotkey = new();
    readonly ClipboardMonitor _clipboard = new();
    readonly ShelfStore _store = new();
    Settings _settings = Settings.Load();

    readonly Panel _content = new() { Dock = DockStyle.Fill };
    readonly TabBar _tabs = new() { Dock = DockStyle.Bottom };   // 全部 · 文字 · 圖片 · 檔案 · 🔍（含搜尋框）
    readonly MessageBar _message = new() { Dock = DockStyle.Top, Visible = false };   // 錯誤訊息（檔案不存在等）
    readonly ItemListView _list;
    readonly TrayService _tray;
    readonly BubbleForm _bubble;
    readonly OutsideClickWatcher _outside = new();

    readonly System.Windows.Forms.Timer _clock = new() { Interval = 60_000 };       // 更新「N 分鐘前」
    readonly System.Windows.Forms.Timer _messageTimer = new() { Interval = 3000 };  // 錯誤訊息顯示多久
    readonly System.Windows.Forms.Timer _panelTimer = new() { Interval = Anim.TickMs };
    bool _pinOpen;             // 釘住視窗：點外面也不收

    const int PanelMs = 160;
    bool _showing;             // 面板的目標狀態；動畫進行中 Visible 仍是 true，判斷「開著沒」一律看這個
    float _shown;              // 動畫進度：0 = 隱藏，1 = 完全顯示
    Rectangle _rest;           // Place() 算出的抽屜位置
    BallCorner _corner;        // 球在抽屜的哪個角
    readonly DrawerFrame _frame = new();   // 抽屜的外框層（平滑外形、外框線、陰影），在內容視窗正後方

    bool _allowShow;           // 啟動時不顯示面板，只顯示球
    bool _keyboardMode, _dragging, _trayPaused;
    int _busy;                 // 選單、對話框開著時，點外面不收
    long _outsideClosedAt;     // 剛被「點外面」收掉的時間：那一下若是點系統匣圖示，別又馬上打開
    IntPtr _prevForeground;    // 進鍵盤模式前的前景視窗，離開時把焦點還給它
    List<ShelfItem> _view = [];

    public ShelfForm()
    {
        Text = "Shelf";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Font = Theme.Body;
        Theme.Apply(_settings.Theme);
        _store.Limit = _settings.Limit;

        _list = new ItemListView(new ThumbnailCache(_store.Blobs)) { Dock = DockStyle.Fill };

        // 頂部什麼都沒有，清單從最上面開始；分頁列在底部，最靠近球（面板開在球上方）。
        // 釘住視窗、設定、清空等移到「右鍵點球」與系統匣選單
        _content.Controls.Add(_list);
        _content.Controls.Add(_message);
        _content.Controls.Add(_tabs);
        Controls.Add(_content);

        // 拖入（F1 到 F3）
        _list.AllowDrop = true;
        _list.DragEnter += OnDragEnter;
        _list.DragLeave += (_, _) => _list.SetDropState(false, null);
        _list.DragDrop += OnDragDrop;

        // 使用與管理
        _list.ItemClicked += item => CopyItem(item);
        _list.ItemDragStarted += DragItem;
        _list.ItemRightClicked += ShowItemMenu;
        _list.ActionClicked += (item, action) =>
        {
            if (action == RowAction.Pin) _store.SetPinned(item, !item.Pinned);
            else _store.Remove(item);
        };

        // 分頁與搜尋（6.1 鍵盤模式）
        _tabs.Changed += () => RefreshList(animate: false);
        _tabs.SearchRequested += () => { EnterKeyboardMode(); _tabs.BeginSearch(); RefreshList(animate: false); };
        _tabs.SearchClosed += CloseSearch;
        _tabs.ActivationNeeded += EnterKeyboardMode;
        _tabs.Input.TextChanged += (_, _) => { _list.ClearSelection(); RefreshList(animate: false); };
        _tabs.Input.KeyDown += OnSearchKeyDown;

        _store.Changed += () =>
        {
            RefreshList(animate: true);   // 新增、刪除：列長出 / 縮回
            if (_showing) Place();        // 面板高度跟著筆數
        };
        _messageTimer.Tick += (_, _) => { _messageTimer.Stop(); _message.Visible = false; };
        _panelTimer.Tick += (_, _) => AnimatePanel();
        _clock.Tick += (_, _) => { if (Visible) _list.Invalidate(); };
        _clock.Start();

        // 浮動球
        _bubble = new BubbleForm(CanAccept);
        _bubble.Clicked += () => TogglePanel(keyboard: false);
        _bubble.RightClicked += ShowBallMenu;
        _bubble.DragStarted += Dismiss;   // 拖球時面板先收起，不要留在原地
        _bubble.Moved += p =>
        {
            (_settings.BubbleX, _settings.BubbleY) = (p.X, p.Y);
            TrySaveSettings();
        };
        _bubble.Dropped += async data => await AddAsync(ItemFactory.Capture(data, preferImage: true, null, _store));
        _bubble.PlaceAt(_settings.BubbleX is int bx && _settings.BubbleY is int by ? new Point(bx, by) : null);
        _bubble.Show();

        _outside.ButtonDown += OnOutsideButtonDown;

        _tray = new TrayService(
            toggle: () => { if (Environment.TickCount64 - _outsideClosedAt > 300) TogglePanel(keyboard: false); },
            populateMenu: items => PopulateAppMenu(items, forTray: true));

        ApplyTheme();
        UpdatePaused();
        RefreshList(animate: false);
    }

    string HotkeyText => HotkeyService.Format(_settings.HotkeyModifiers, _settings.HotkeyKey);

    int L(int logical) => LogicalToDeviceUnits(logical);

    // 置頂用 WS_EX_TOPMOST 而不用 TopMost 屬性：
    // TopMost = true 會在 SetWindowPos 時不帶 SWP_NOACTIVATE，讓 ShowWithoutActivation 失效。
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED：淡入淡出用 SetLayeredWindowAttributes 調整整個視窗的透明度。
            // 不用 CS_DROPSHADOW、DWM 圓角：外形是抽屜（Region），陰影和平滑邊緣由後面的 DrawerFrame 畫
            cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetLayeredWindowAttributes(Handle, 0, 0, Native.LWA_ALPHA);   // 分層視窗沒設過透明度就不會顯示
        int noRound = 1;   // DWMWCP_DONOTROUND：外形自己用 Region 決定，別讓 DWM 再圓一次角
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        _clipboard.Start(Handle);
        RegisterHotkey();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _clipboard.Stop(Handle);
        _hotkey.Unregister();
        base.OnHandleDestroyed(e);
    }

    /// 啟動時 Application.Run 會把主視窗顯示出來；這裡擋下第一次，只建立 handle（熱鍵、剪貼簿監聽要用）
    protected override void SetVisibleCore(bool value)
    {
        if (_allowShow)
        {
            base.SetVisibleCore(value);
            return;
        }
        // 先把可見狀態設回 false 再建 handle：反過來的話 CreateParams 會帶 WS_VISIBLE，視窗一建立就出現
        base.SetVisibleCore(false);
        if (!IsHandleCreated) CreateHandle();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _outside.Dispose();
        _store.Dispose();   // 把還在 debounce 的異動寫進 index.json
        _frame.Close();
        _bubble.Close();
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_CLIPBOARDUPDATE) OnClipboardUpdate();
        else if (m.Msg == Native.WM_HOTKEY && m.WParam == HotkeyService.Id) TogglePanel(keyboard: true);
        else if (m.Msg == Native.WM_SHELF_SHOW) Expand(keyboard: false);
        else if (m.Msg == Native.WM_SETTINGCHANGE)
        {
            // 系統切換深淺色或強調色（就算外觀固定淺色 / 深色，強調色仍跟系統走）
            if (Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet") ApplyTheme();
        }
        else if (m.Msg == Native.WM_DISPLAYCHANGE)
        {
            // 螢幕拔插：球所在的螢幕可能不在了，PlaceAt 會把它移回主螢幕
            _bubble.PlaceAt(_bubble.Location);
            if (_showing) Place();
        }
        base.WndProc(ref m);
    }

    // ── 展開 / 收起（6.7）──

    void TogglePanel(bool keyboard)
    {
        if (_showing) Dismiss();
        else Expand(keyboard);
    }

    /// 在球旁邊展開（淡入並從球的方向滑出）。keyboard：熱鍵叫出時直接把焦點放到搜尋框
    void Expand(bool keyboard)
    {
        Place();
        if (!_showing)
        {
            _showing = true;
            if (!Visible)
            {
                _shown = 0;
                _frame.Show();
                ApplyPanelFrame();
                _allowShow = true;
                Show();
            }
            _outside.Start();
            _panelTimer.Start();
        }
        if (keyboard)
        {
            // 熱鍵叫出：直接進搜尋，可以馬上打字
            EnterKeyboardMode();
            _tabs.BeginSearch();
            RefreshList(animate: false);
        }
    }

    /// 點外面、再點一次球、熱鍵再按一次、Esc、開始拖球。淡出完才真的 Hide
    void Dismiss()
    {
        if (!_showing) return;
        _showing = false;
        _outside.Stop();
        ReleaseKeyboard();
        _panelTimer.Start();
    }

    /// 點 ✕：回到分頁、清掉搜尋字，焦點還給原本的程式（分頁模式不需要打字）
    void CloseSearch()
    {
        _tabs.EndSearch();
        ReleaseKeyboard();
        RefreshList(animate: false);
    }

    void AnimatePanel()
    {
        _shown = Anim.Step(_shown, _showing ? 1 : 0, PanelMs);
        ApplyPanelFrame();
        if (_shown != (_showing ? 1 : 0)) return;
        _panelTimer.Stop();
        if (_showing) return;
        Hide();
        _frame.Hide();
        // 淡出完才重設（淡出途中別讓畫面跳）：下次打開從分頁開始，不留上次的搜尋字
        if (_tabs.Searching)
        {
            _tabs.EndSearch();
            RefreshList(animate: false);
        }
    }

    /// 依動畫進度設位置與透明度：從球的方向斜斜滑出 12 px，同時淡入。
    /// 外框層、內容層、球三個一起動，Z 順序由下往上：外框 → 內容 → 球
    void ApplyPanelFrame()
    {
        if (!IsHandleCreated) return;
        float e = Anim.Ease(_shown);
        int off = (int)((1 - e) * L(12));
        int dx = _corner is BallCorner.BottomRight or BallCorner.TopRight ? off : -off;
        int dy = _corner is BallCorner.BottomRight or BallCorner.BottomLeft ? off : -off;
        var r = _rest with { X = _rest.X + dx, Y = _rest.Y + dy };
        byte alpha = (byte)(e * 255);

        _frame.Present(r, alpha);
        const uint keep = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        Native.SetWindowPos(_frame.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, keep);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE);
        Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
        Native.SetWindowPos(_bubble.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, keep);
    }

    /// 點到面板和球以外的地方就收起；選單、對話框開著或釘住視窗時不收
    void OnOutsideButtonDown(Point p)
    {
        if (!_showing || _pinOpen || _busy > 0 || _dragging) return;
        if (Bounds.Contains(p) || _bubble.Bounds.Contains(p)) return;   // 點球交給球自己的 Clicked 處理
        _outsideClosedAt = Environment.TickCount64;
        BeginInvoke(Dismiss);   // hook 回呼要快速返回，收起的動作排到訊息迴圈去做
    }

    /// 融合抽屜（設計稿 docs/ui-lshape.html）：球嵌在抽屜的一個角，球心在分類列那一排的正中央。
    /// 球在螢幕右半邊就往左展開，下半邊就往上展開（分類列在底部、和球同一排）；上面放不下才往下開（分類列改到頂部）。
    /// 高度跟著筆數：剛好放得下全部項目，最少 200 px（空狀態），最多工作區 70%；用總筆數算，搜尋時不伸縮。
    void Place()
    {
        if (!IsHandleCreated) return;
        var ball = _bubble.Bounds;
        var c = new Point(ball.Left + ball.Width / 2, ball.Top + ball.Height / 2);   // 球心
        var work = Screen.FromRectangle(ball).WorkingArea;
        int half = L(DrawerShape.RowHalf), w = L(340), min = L(200);
        int want = Math.Clamp(_tabs.Height + _store.Items.Count * L(56) + L(16), min, (int)(work.Height * 0.7));

        int above = c.Y + half - work.Top, below = work.Bottom - (c.Y - half);
        bool up = above >= want || above >= below;
        bool right = c.X > work.Left + work.Width / 2;
        int h = Math.Max(min, Math.Min(want, up ? above : below));
        var corner = (up, right) switch
        {
            (true, true) => BallCorner.BottomRight,
            (true, false) => BallCorner.BottomLeft,
            (false, true) => BallCorner.TopRight,
            _ => BallCorner.TopLeft,
        };

        var rect = new Rectangle(right ? c.X + half - w : c.X - half, up ? c.Y + half - h : c.Y - half, w, h);
        bool reshape = rect.Size != _rest.Size || corner != _corner;
        (_rest, _corner) = (rect, corner);
        if (reshape) ApplyShape();
        if (Visible) ApplyPanelFrame();
    }

    /// 大小或角落改變時：內容層裁成往內縮 1.5 px 的抽屜形狀、外框層重畫、分類列換到球那一排並讓開凹口
    void ApplyShape()
    {
        using (var path = DrawerShape.Build(_rest.Size, _corner, DeviceDpi / 96f, inset: 1.5f))
        {
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }
        _frame.SetShape(_rest.Size, _corner);

        bool bottom = _corner is BallCorner.BottomRight or BallCorner.BottomLeft;
        _tabs.Dock = bottom ? DockStyle.Bottom : DockStyle.Top;
        _message.Dock = bottom ? DockStyle.Top : DockStyle.Bottom;
        _tabs.SetNotch(onLeft: _corner is BallCorner.BottomLeft or BallCorner.TopLeft,
            reserve: L(DrawerShape.NotchReach), separatorOnTop: bottom);
    }

    // ── 鍵盤模式（6.1）──
    // 平常帶 WS_EX_NOACTIVATE，點擊不搶焦點；要打字時才暫時拿掉並啟用視窗，失去焦點就恢復。

    void EnterKeyboardMode()
    {
        if (_keyboardMode) return;
        var fg = Native.GetForegroundWindow();
        _prevForeground = fg == Handle ? IntPtr.Zero : fg;
        _keyboardMode = true;
        SetNoActivate(false);
        Activate();   // 剛收到使用者的點擊或熱鍵，系統允許我們切到前景
    }

    /// 主動結束鍵盤模式並把焦點還給原本的視窗；不靠系統在 Hide 時自己挑下一個視窗，免得挑錯。
    void ReleaseKeyboard()
    {
        if (!_keyboardMode) return;
        if (_prevForeground != IntPtr.Zero) Native.SetForegroundWindow(_prevForeground);
        ExitKeyboardMode();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        ExitKeyboardMode();
    }

    void ExitKeyboardMode()
    {
        if (!_keyboardMode) return;
        _keyboardMode = false;
        SetNoActivate(true);
        _list.ClearSelection();
    }

    void SetNoActivate(bool on)
    {
        int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
        ex = on ? ex | Native.WS_EX_NOACTIVATE : ex & ~Native.WS_EX_NOACTIVATE;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
    }

    void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: _list.MoveSelection(1); break;
            case Keys.Up: _list.MoveSelection(-1); break;
            case Keys.PageDown: _list.MoveSelection(5); break;
            case Keys.PageUp: _list.MoveSelection(-5); break;
            case Keys.Enter:
                // 沒選就複製第一筆：打幾個字、按 Enter 是最快的用法
                if ((_list.SelectedItem ?? _view.FirstOrDefault()) is { } item && CopyItem(item)) Dismiss();
                break;
            case Keys.Escape:
                if (_tabs.Input.TextLength > 0) _tabs.Input.Clear();   // 有字先清空，再按一次收起面板
                else Dismiss();
                break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;   // 不讓 TextBox 發出「叮」聲
    }

    // ── 設定與主題（7.7、7.8）──

    void RegisterHotkey()
    {
        if (!_hotkey.Register(Handle, _settings.HotkeyModifiers, _settings.HotkeyKey))
            _tray.Warn($"熱鍵 {HotkeyText} 已被其他程式佔用，請到設定換一組，或從系統匣圖示開啟 Shelf。");
    }

    void OpenSettings()
    {
        _busy++;
        _hotkey.Unregister();   // 設定裡要按下新的組合鍵來錄製，不先放掉的話會被自己攔走
        try
        {
            var current = _settings.Clone();
            current.AutoStart = AutoStart.IsEnabled();   // 安裝程式也會設定開機啟動，以登錄檔為準
            using var dlg = new SettingsForm(current, $"{_store.Items.Count} 項 · {Fmt.Size(_store.DiskBytes)}") { TopMost = true };
            if (dlg.ShowDialog() == DialogResult.OK) ApplySettings(dlg.Result);
        }
        finally
        {
            _busy--;
            RegisterHotkey();
        }
    }

    void ApplySettings(Settings s)
    {
        // 球的位置是拖動時存的，設定對話框拿的是開啟當下的副本，別用舊值蓋掉
        (s.BubbleX, s.BubbleY) = (_settings.BubbleX, _settings.BubbleY);
        _settings = s;
        TrySaveSettings();
        try
        {
            AutoStart.Set(s.AutoStart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _tray.Warn($"開機啟動設定失敗：{ex.Message}");
        }
        _store.Limit = s.Limit;
        UpdatePaused();
        ApplyTheme();
        RefreshList(animate: false);
    }

    void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _tray.Warn($"設定儲存失敗：{ex.Message}");
        }
    }

    void UpdatePaused()
    {
        bool paused = !_settings.CollectClipboard || _trayPaused;
        _clipboard.Paused = paused;
        _tray.SetPaused(paused);
        _bubble.Paused = paused;
    }

    void ApplyTheme()
    {
        Theme.Apply(_settings.Theme);
        BackColor = Theme.Panel;
        foreach (var c in Descendants(this))
        {
            if (c is TextBox) continue;   // 搜尋框的底色由分頁列決定
            c.BackColor = Theme.Panel;
            c.ForeColor = Theme.Text;
            if (c is TabBar tabs) tabs.RefreshColors();
        }
        if (!_rest.IsEmpty)
        {
            _frame.SetShape(_rest.Size, _corner);   // 外框層的底色、外框線跟著主題
            if (Visible) ApplyPanelFrame();
        }
        Invalidate(invalidateChildren: true);
        _bubble.Render();
    }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    // ── 清單內容（F12、F13）──

    void RefreshList(bool animate)
    {
        // 搜尋時找所有類型；分頁模式才依分頁過濾
        var q = _tabs.Searching ? _tabs.Input.Text.Trim() : "";
        var filter = _tabs.Searching ? Filter.All : _tabs.Value;

        // 釘選的排最前（F11）；OrderBy 是穩定排序，其餘維持新到舊
        _view = [.. _store.Items
            .Where(i => filter switch
            {
                Filter.File => i.Kind == ItemKind.File,
                Filter.Text => i.Kind == ItemKind.Text,
                Filter.Image => i.Kind == ItemKind.Image,
                _ => true,
            })
            .Where(i => q.Length == 0 || Matches(i, q))
            .OrderByDescending(i => i.Pinned)];
        _list.SetItems(_view, animate);

        if (_store.Items.Count == 0)
            _list.SetEmptyText("把檔案、文字或圖片拖到這裡", "按 Ctrl+C 複製的內容也會自動收進來", $"隨時按 {HotkeyText} 叫出這個架子");
        else if (q.Length > 0)
            _list.SetEmptyText($"找不到「{q}」", "試試其他關鍵字");
        else if (_tabs.Searching)
            _list.SetEmptyText("輸入關鍵字搜尋", "標題、內容、檔案路徑、來源程式都會比對");
        else
            _list.SetEmptyText("這個分類沒有項目");
    }

    /// 標題、短文字全文、檔案路徑、來源程式都能搜；長文字只搜標題（前 80 字），避免每次都讀檔
    static bool Matches(ShelfItem i, string q) =>
        i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (i.Inline?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (i.SourceApp?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

    // ── 收集 ──

    async void OnClipboardUpdate()
    {
        if (await _clipboard.ReadAsync() is not { } c) return;
        await AddAsync(ItemFactory.Capture(c.Data, preferImage: false, c.Source, _store));
    }

    /// 從自己拖出又拖回來的不收
    static bool CanAccept(IDataObject data) =>
        !data.GetDataPresent(ClipboardMonitor.SelfFormat) && ItemFactory.HasSupportedData(data);

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        bool ok = e.Data is { } d && CanAccept(d);
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok) _list.SetDropState(true, DropHint(e.Data!));
    }

    async void OnDragDrop(object? sender, DragEventArgs e)
    {
        _list.SetDropState(false, null);
        if (e.Data is null) return;
        // Capture 同步取完資料就返回，拖曳來源不用等我們存檔
        await AddAsync(ItemFactory.Capture(e.Data, preferImage: true, null, _store));
    }

    static string DropHint(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] dropped && ItemFactory.Collectable(dropped) is { Length: > 0 } paths
            ? paths.Length == 1 ? Path.GetFileName(paths[0]) : $"{paths.Length} 個項目"
            : data.GetDataPresent(DataFormats.UnicodeText) ? "文字" : "圖片";

    async Task AddAsync(Func<ShelfItem?>? build)
    {
        if (build is null) return;
        ShelfItem? item;
        try
        {
            item = await Task.Run(build);   // 雜湊、PNG 編碼、縮圖在背景跑，UI 不卡
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        {
            Debug.WriteLine($"收集失敗：{ex}");   // 壞圖、磁碟滿等：略過這一筆，不要讓整個程式掛掉
            return;
        }
        if (item is not null) _store.Add(item);
    }

    // ── 使用（F7、F9、F10）──

    /// 單擊：複製到剪貼簿
    bool CopyItem(ShelfItem item)
    {
        using var data = DragOutBuilder.Build(item, _store.Blobs);
        if (data is null) { ShowMessage(MissingMessage(item)); return false; }
        if (!SetClipboard(data.Data)) { ShowMessage("剪貼簿被其他程式佔用，請再試一次"); return false; }
        _list.Flash(item);
        _bubble.ShowCheck();   // 狀態列拿掉了，「已複製」改成球上短暫出現勾勾
        return true;
    }

    /// copy: true 會呼叫 OleFlushClipboard 把所有格式立刻寫進剪貼簿，之後原本的 Bitmap 就可以釋放
    static bool SetClipboard(DataObject data)
    {
        try
        {
            Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 50);
            return true;
        }
        catch (ExternalException)
        {
            return false;   // 其他程式鎖著剪貼簿
        }
    }

    /// 按住拖曳：拖到其他程式
    void DragItem(ShelfItem item)
    {
        using var data = DragOutBuilder.Build(item, _store.Blobs);
        if (data is null) { ShowMessage(MissingMessage(item)); return; }
        _dragging = true;
        try { _list.DoDragDrop(data.Data, DragDropEffects.Copy); }
        finally { _dragging = false; }
    }

    void ShowItemMenu(ShelfItem item, Point screen)
    {
        var menu = new ContextMenuStrip();
        MenuRenderer.Style(menu);
        menu.Items.Add("複製", null, (_, _) => CopyItem(item));
        menu.Items.Add(item.Pinned ? "取消釘選" : "釘選", null, (_, _) => _store.SetPinned(item, !item.Pinned));
        if (item.Kind == ItemKind.File)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("開啟所在資料夾", null, (_, _) => OpenContainingFolder(item));
        }
        if (item.BlobPath is not null)   // 圖片、長文字
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("另存新檔…", null, (_, _) => SaveAs(item));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("刪除", null, (_, _) => _store.Remove(item)) { Tag = MenuRenderer.DangerTag });

        _busy++;
        menu.Closed += (_, _) =>
        {
            _busy--;
            BeginInvoke(menu.Dispose);   // 等點擊事件跑完再釋放
        };
        menu.Show(screen);
    }

    void OpenContainingFolder(ShelfItem item)
    {
        var path = (item.Inline ?? "").Split('\n')[0];
        if (!File.Exists(path) && !Directory.Exists(path)) { ShowMessage(MissingMessage(item)); return; }
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    void SaveAs(ShelfItem item)
    {
        var src = _store.Blobs.FullPath(item.BlobPath!);
        if (!File.Exists(src)) { ShowMessage(MissingMessage(item)); return; }

        bool image = item.Kind == ItemKind.Image;
        using var dlg = new SaveFileDialog
        {
            FileName = $"{(image ? "圖片" : "文字")}_{item.CreatedAt:yyyyMMdd_HHmmss}",
            DefaultExt = image ? "png" : "txt",
            Filter = image ? "PNG 圖片|*.png" : "文字檔|*.txt",
        };
        _busy++;
        try
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.Copy(src, dlg.FileName, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"另存失敗：{ex.Message}");
        }
        finally
        {
            _busy--;
        }
    }

    /// 右鍵點球與系統匣共用的選單：不常用的東西都在這裡（面板上不放任何按鈕）
    void PopulateAppMenu(ToolStripItemCollection items, bool forTray)
    {
        items.Clear();
        if (forTray)
            items.Add(new ToolStripMenuItem("顯示 Shelf", null, (_, _) => Expand(keyboard: false)) { ShortcutKeyDisplayString = HotkeyText });
        items.Add(new ToolStripMenuItem("釘住視窗", null, (_, _) => _pinOpen = !_pinOpen) { Checked = _pinOpen, ToolTipText = "點面板外面也不收起" });
        items.Add(new ToolStripMenuItem("暫停收集剪貼簿", null, (_, _) => { _trayPaused = !_trayPaused; UpdatePaused(); }) { Checked = _trayPaused });
        items.Add(new ToolStripSeparator());
        items.Add("設定…", null, (_, _) => BeginInvoke(OpenSettings));   // 等選單關掉再開對話框
        items.Add("開啟資料夾", null, (_, _) => Process.Start(new ProcessStartInfo(ShelfStore.Root) { UseShellExecute = true }));
        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem("清空未釘選…", null, (_, _) => BeginInvoke(ClearUnpinned)) { Tag = MenuRenderer.DangerTag });
        items.Add(new ToolStripSeparator());
        items.Add("結束", null, (_, _) => Close());
    }

    void ShowBallMenu(Point screen)
    {
        var menu = new ContextMenuStrip();
        PopulateAppMenu(menu.Items, forTray: false);
        MenuRenderer.Style(menu, showCheckMargin: true);
        _busy++;
        menu.Closed += (_, _) =>
        {
            _busy--;
            BeginInvoke(menu.Dispose);
        };
        menu.Show(screen, ToolStripDropDownDirection.AboveLeft);   // 往球的上方開，不擋住球
    }

    void ClearUnpinned()
    {
        int n = _store.Items.Count(i => !i.Pinned);
        if (n == 0) { ShowMessage("沒有未釘選的項目"); return; }
        _busy++;
        try
        {
            if (ConfirmDialog.Ask("清空 Shelf？", $"將刪除 {n} 個未釘選的項目，釘選的項目會保留。", "清空"))
                _store.ClearUnpinned();
        }
        finally
        {
            _busy--;
        }
    }

    static string MissingMessage(ShelfItem item) =>
        item.Kind == ItemKind.File ? "檔案已不存在（可能被移動或刪除）" : "內容檔案遺失";

    /// 錯誤或提醒：面板頂部的訊息列，3 秒後自動消失
    void ShowMessage(string message)
    {
        _message.Text = message;
        _message.Visible = true;
        _messageTimer.Stop();
        _messageTimer.Start();
    }
}
