namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Message / question window in the Tarkov Appraiser design (instead of the white Windows MessageBox).
//  UiDialog.Show(...) returns the index of the clicked button, -1 when closed with Esc.
// ------------------------------------------------------------
sealed class UiDialog : Form
{
    // scaled by Windows like the rest of the desktop (see Dpi.cs)
    protected override void CreateHandle()
    {
        using (Dpi.ScaledByWindows()) base.CreateHandle();
    }

    public sealed record Palette(Color Back, Color Panel, Color Text, Color Accent);

    const int Pad = 24;
    readonly int W = 420;   // wider when the buttons need it
    int _result = -1;
    readonly Color _border;
    bool _regionFallback;

    UiDialog(Palette p, string title, string text, int accentIndex, string[] buttons)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Font = UiFont.Text(9.5f);
        BackColor = p.Panel;
        Text = title;
        _border = Themes.Blend(p.Panel, p.Text, 0.16);
        int buttonsW = buttons.Sum(b => Math.Max(96, TextRenderer.MeasureText(b, Font).Width + 36) + 8) - 8;
        W = Math.Max(W, buttonsW + 2 * Pad);
        // a short text that just misses one line ("... bottom" / "right).") gets a slightly wider window instead
        const int MaxW = 560;
        if (!text.Contains('\n'))
        {
            int oneLine = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + 6;
            if (oneLine + 2 * Pad <= MaxW) W = Math.Max(W, oneLine + 2 * Pad);
        }

        var titleLbl = new Txt
        {
            Text = title,
            Font = UiFont.Semi(13f),
            ForeColor = p.Text,
            BackColor = p.Panel,
            UseMnemonic = false,
            Left = Pad, Top = 20, Width = W - 2 * Pad, Height = 30
        };
        Controls.Add(titleLbl);

        int textH = TextRenderer.MeasureText(text, Font, new Size(W - 2 * Pad, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4;
        Controls.Add(new Txt
        {
            Text = text,
            ForeColor = Themes.Blend(p.Text, p.Panel, 0.25),
            BackColor = p.Panel,
            UseMnemonic = false,
            Left = Pad, Top = 58, Width = W - 2 * Pad, Height = textH
        });

        int y = 58 + textH + 22;
        int x = W - Pad;
        var input = Themes.Blend(p.Panel, p.Text, 0.08);
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            int index = i;
            bool accent = i == accentIndex;
            int bw = Math.Max(96, TextRenderer.MeasureText(buttons[i], Font).Width + 36);
            x -= bw;
            var b = new Pill
            {
                Text = buttons[i],
                Left = x, Top = y, Width = bw, Height = 34,
                BackColor = accent ? p.Accent : input,
                ForeColor = accent ? Themes.Contrast(p.Accent) : p.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = accent ? p.Accent : Themes.Blend(p.Panel, p.Text, 0.14);
            b.FlatAppearance.MouseOverBackColor = accent ? Themes.Blend(p.Accent, Color.White, 0.18) : Themes.Blend(input, p.Text, 0.10);
            b.FlatAppearance.MouseDownBackColor = accent ? Themes.Blend(p.Accent, Color.Black, 0.18) : Themes.Blend(input, p.Text, 0.18);
            b.Click += (_, _) => { _result = index; Close(); };
            Controls.Add(b);
            if (accent) AcceptButton = b;
            x -= 8;
        }
        ClientSize = new Size(W, y + 34 + 20);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _regionFallback = !Themes.RoundCorners(Handle, _border);
        if (_regionFallback)
        {
            using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 12);
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_regionFallback) return;   // Windows 11 draws the border itself
        UiPaint.Smooth(e.Graphics);
        using var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var pen = new Pen(_border);
        e.Graphics.DrawPath(pen, path);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            _result = -1;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public static int Show(IWin32Window? owner, Palette p, string title, string text, int accentIndex, params string[] buttons)
    {
        using var dlg = new UiDialog(p, title, text, accentIndex, buttons.Length == 0 ? new[] { "OK" } : buttons);
        if (owner == null) dlg.StartPosition = FormStartPosition.CenterScreen;
        dlg.ShowDialog(owner);
        return dlg._result;
    }
}
