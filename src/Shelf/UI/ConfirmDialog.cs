namespace Shelf.UI;

/// Fluent 確認對話框（ContentDialog），取代系統 MessageBox。
/// 上方內容區放標題與說明，下方頁尾放按鈕；圓角、陰影交給 DWM。
sealed class ConfirmDialog : Form
{
    ConfirmDialog(string title, string body, string confirmText)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;
        BackColor = Theme.Dialog;
        Font = Theme.Body;

        int width = L(400), pad = L(24);
        var titleLabel = new Label
        {
            Text = title, Font = Theme.Subtitle, ForeColor = Theme.Text, AutoSize = true,
            Location = new Point(pad, pad),
        };
        var bodyLabel = new Label
        {
            Text = body, ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(width - pad * 2, 0),
            Location = new Point(pad, pad + titleLabel.PreferredHeight + L(12)),
        };

        var confirm = new FluentButton(confirmText, ButtonKind.Accent) { DialogResult = DialogResult.OK };
        var cancel = new FluentButton("取消") { DialogResult = DialogResult.Cancel };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = L(80), BackColor = Theme.Window };
        // 兩顆按鈕等寬並排（Fluent ContentDialog 的版面）
        int bw = (width - pad * 2 - L(8)) / 2;
        confirm.Bounds = new Rectangle(pad, L(24), bw, L(32));
        cancel.Bounds = new Rectangle(pad + bw + L(8), L(24), bw, L(32));
        footer.Controls.Add(confirm);
        footer.Controls.Add(cancel);
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Divider); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };

        Controls.Add(titleLabel);
        Controls.Add(bodyLabel);
        Controls.Add(footer);
        AcceptButton = confirm;
        CancelButton = cancel;
        ClientSize = new Size(width, bodyLabel.Bottom + L(28) + footer.Height);
        ActiveControl = cancel;   // 預設焦點在「取消」：危險動作不要按 Enter 就執行
    }

    /// 回傳使用者是否按了確認
    public static bool Ask(string title, string body, string confirmText)
    {
        using var dlg = new ConfirmDialog(title, body, confirmText);
        return dlg.ShowDialog() == DialogResult.OK;
    }

    int L(int logical) => LogicalToDeviceUnits(logical);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= Native.CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2, border = Theme.ColorRef(Theme.WindowBorder);
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
