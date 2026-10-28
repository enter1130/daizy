using Shelf.UI;

namespace Shelf.Services;

/// 系統匣圖示：左鍵切換顯示，右鍵選單（7.6）。暫停收集時圖示變灰。
/// 選單內容由表單提供（和右鍵點球同一份），每次打開時重建，勾選狀態才會是最新的。
sealed class TrayService : IDisposable
{
    readonly NotifyIcon _icon;
    // 用系統小圖示尺寸畫（150% 縮放時是 24 px），不讓系統從大圖縮小而糊掉
    readonly Icon _normalIcon = Logo.CreateIcon(SystemInformation.SmallIconSize.Width, Logo.Brand);
    readonly Icon _pausedIcon = Logo.CreateIcon(SystemInformation.SmallIconSize.Width, Color.FromArgb(0x8A, 0x8A, 0x8A));

    public TrayService(Action toggle, Action<ToolStripItemCollection> populateMenu)
    {
        var menu = new ContextMenuStrip();
        // 先重建項目、再套樣式：MenuRenderer.Style 也掛在 Opening 上幫項目設間距，順序不能反
        menu.Opening += (_, _) => populateMenu(menu.Items);
        MenuRenderer.Style(menu, showCheckMargin: true);
        menu.Items.Add(new ToolStripMenuItem());   // 空選單不會觸發 Opening，先放一個佔位

        _icon = new NotifyIcon { Icon = _normalIcon, Text = Brand.Name, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) toggle(); };
    }

    public void SetPaused(bool paused)
    {
        _icon.Icon = paused ? _pausedIcon : _normalIcon;
        _icon.Text = paused ? $"{Brand.Name}（暫停收集）" : Brand.Name;
    }

    public void Warn(string text) => _icon.ShowBalloonTip(5000, Brand.Name, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;   // 不設的話圖示會殘留在系統匣直到滑鼠滑過
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _normalIcon.Dispose();
        _pausedIcon.Dispose();
    }
}
