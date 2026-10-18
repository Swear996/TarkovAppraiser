using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Small number field at the mouse: "How many?  [ 18 ] / 60" - Enter applies, Esc cancels.
//  Opened with the stack hotkey when the count in the cell could not be read.
// ------------------------------------------------------------
sealed class StackInputForm : Form
{
    public int? Count { get; private set; }

    readonly TextBox _box = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, MaxLength = 4 };
    readonly int _max;
    readonly Color _border;

    public StackInputForm(Item item, AppSettings s, Point at)
    {
        _max = Math.Max(1, item.StackMax);
        var panel = Themes.FromArgb(s.ThemePanelArgb);
        var text = Themes.FromArgb(s.ThemeTextArgb);
        var accent = Themes.FromArgb(s.ThemeAccentArgb);
        _border = accent;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = panel;
        Font = UiFont.Text(10f);
        ClientSize = new Size(260, 96);

        var area = Screen.FromPoint(at).WorkingArea;
        Location = new Point(Math.Min(at.X + 16, area.Right - Width), Math.Min(at.Y + 16, area.Bottom - Height));

        Controls.Add(new Txt
        {
            Text = TextBuilder.DisplayName(item), Font = UiFont.Semi(10.5f), ForeColor = text, BackColor = panel,
            UseMnemonic = false, Left = 16, Top = 12, Width = 228, Height = 22
        });
        Controls.Add(new Txt
        {
            Text = L.T("How many?"), ForeColor = Themes.Blend(text, panel, 0.35), BackColor = panel,
            UseMnemonic = false, Left = 16, Top = 50, Width = 100, Height = 22
        });
        var host = new Host
        {
            Left = 120, Top = 44, Width = 70, Height = 32,
            Fill = Themes.Blend(panel, text, 0.08), Border = Themes.Blend(panel, text, 0.2), Accent = accent
        };
        _box.BackColor = host.Fill;
        _box.ForeColor = text;
        _box.Font = UiFont.Semi(11f);
        host.Controls.Add(_box);
        Controls.Add(host);
        Controls.Add(new Txt
        {
            Text = "/ " + _max, ForeColor = Themes.Blend(text, panel, 0.35), BackColor = panel,
            UseMnemonic = false, Left = 198, Top = 50, Width = 50, Height = 22
        });

        _box.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        Shown += (_, _) => { Activate(); _box.Focus(); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.Enter)
        {
            if (int.TryParse(_box.Text, out int n) && n >= 1) Count = Math.Min(n, _max);
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // closes by itself when the user clicks somewhere else
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(new Action(() => { if (!IsDisposed && Visible) Close(); }));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Themes.RoundCorners(Handle, _border))
        {
            using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 12);
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UiPaint.Smooth(e.Graphics);
        using var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var pen = new Pen(_border, 1.5f);
        e.Graphics.DrawPath(pen, path);
    }
}
