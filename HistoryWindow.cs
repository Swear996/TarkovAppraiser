using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  In-game history list: a small window on top of the game with the last checked items.
//  Shown / hidden with its hotkey or the tray menu. Drag it at the title bar,
//  resize it at the edges; position and size are remembered.
//  It never takes the focus away from the game and is invisible in screenshots
//  (so it cannot disturb the item recognition).
// ------------------------------------------------------------
sealed class HistoryWindow : Form
{
    const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
              HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    const int HeaderH = 34, FooterH = 30, Edge = 6;

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    public event Action? BoundsSaved;   // moved or resized by the user

    readonly HistoryList _list = new();
    readonly Font _title = UiFont.Semi(9.5f);
    Color _text = Color.White, _muted = Color.Gray, _border = Color.Gray;
    bool _closeHover;
    public event Action? CloseClicked;

    Rectangle CloseRect => new(Width - 30, 7, 20, 20);
    Rectangle ResetRect => new(Width - Edge - 8 - _resetW, Height - FooterH + 5, _resetW, FooterH - 10);
    int _resetW = 60;
    bool _resetHover;
    readonly Font _small = UiFont.Text(8.5f), _sum = UiFont.Strong(9.5f);

    public HistoryWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        MinimumSize = new Size(240, 140);
        Padding = new Padding(Edge, HeaderH, Edge, FooterH);
        _list.AutoHeight = false;
        _list.Dock = DockStyle.Fill;
        Controls.Add(_list);
        History.Changed += OnHistoryChanged;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateCapture();
        Themes.RoundCorners(Handle, _border);
    }

    // true = not in screenshots, so it never has to be hidden while Tarkov Appraiser reads the screen
    public bool ExcludedFromCapture { get; private set; }

    public void UpdateCapture()
    {
        if (IsHandleCreated) ExcludedFromCapture = CaptureMode.Apply(Handle, SettingsStore.Current.ShowInScreenshots);
    }

    void OnHistoryChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnHistoryChanged)); return; }
        _list.Reload();
        Invalidate();   // raid total in the footer
    }

    // colors of the price box, opacity of the price box
    public void ApplyStyle(AppSettings s)
    {
        var back = Themes.FromArgb(s.BackColorArgb);
        _text = Themes.FromArgb(s.TextColorArgb);
        _muted = Themes.Blend(_text, back, 0.45);
        _border = Themes.Blend(back, _text, 0.18);
        BackColor = back;
        Opacity = Math.Clamp(s.HistoryWinOpacity, 20, 100) / 100.0;
        _list.SetScale(Math.Clamp(s.OverlayScalePercent, 50, 200) / 100f * 0.9f);   // follows the size of the price box
        _list.BackColor = back;
        _list.TextColor = _text;
        _list.Muted = _muted;
        _list.Hover = Themes.Blend(back, _text, 0.07);
        _list.Tile = Themes.Blend(back, _text, 0.09);
        _list.Line = Themes.Blend(back, _text, 0.08);
        _list.Reload();
        if (IsHandleCreated) Themes.RoundCorners(Handle, _border);
        Invalidate(true);
    }

    // saved position; first time: top right of the main screen
    public void PlaceFrom(AppSettings s)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 728);
        int w = Math.Max(MinimumSize.Width, s.HistoryWinW), h = Math.Max(MinimumSize.Height, s.HistoryWinH);
        var r = s.HistoryWinX < 0 ? new Rectangle(area.Right - w - 20, area.Top + 80, w, h)
                                  : new Rectangle(s.HistoryWinX, s.HistoryWinY, w, h);
        // still on a screen? (monitor removed, resolution changed)
        if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, HeaderH))))
            r = new Rectangle(area.Right - w - 20, area.Top + 80, w, h);
        Bounds = r;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }   // clicks do not take the focus from the game
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            m.Result = (IntPtr)HitTest(p);
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_EXITSIZEMOVE) BoundsSaved?.Invoke();
    }

    int HitTest(Point p)
    {
        bool l = p.X < Edge, r = p.X >= Width - Edge, t = p.Y < Edge, b = p.Y >= Height - Edge;
        bool grip = p.X >= Width - 16 && p.Y >= Height - 16;
        if (grip || (r && b)) return HTBOTTOMRIGHT;
        if (l && b) return HTBOTTOMLEFT;
        if (l && t) return HTTOPLEFT;
        if (r && t) return HTTOPRIGHT;
        if (l) return HTLEFT;
        if (r) return HTRIGHT;
        if (t) return HTTOP;
        if (b) return HTBOTTOM;
        if (p.Y < HeaderH && !CloseRect.Contains(p)) return HTCAPTION;
        if (p.Y >= Height - FooterH && !ResetRect.Contains(p)) return HTCAPTION;   // the footer can be dragged too
        return HTCLIENT;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool h = CloseRect.Contains(e.Location);
        if (h != _closeHover) { _closeHover = h; Invalidate(CloseRect); }
        bool rh = History.RaidCount > 0 && ResetRect.Contains(e.Location);
        if (rh != _resetHover) { _resetHover = rh; Invalidate(); }
        Cursor = h || rh ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_closeHover || _resetHover) { _closeHover = _resetHover = false; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location)) CloseClicked?.Invoke();
        else if (e.Button == MouseButtons.Left && History.RaidCount > 0 && ResetRect.Contains(e.Location)) History.ResetRaid();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Environment.OSVersion.Version.Build < 22000 && Width > 0 && Height > 0)
        {
            using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 10);
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        UiPaint.Smooth(g);

        // title + item count
        const TextFormatFlags F = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        Icons.Draw(g, "history", new RectangleF(12, 9, 16, 16), _muted, 2f);
        TextRenderer.DrawText(g, L.T("Recent items"), _title, new Rectangle(34, 0, Width - 70, HeaderH), _text, BackColor, F);

        // close button
        var cr = CloseRect;
        if (_closeHover)
        {
            using var path = Themes.RoundRect(cr, 5);
            using var b = new SolidBrush(Themes.Blend(BackColor, _text, 0.12));
            g.FillPath(b, path);
        }
        using (var pen = new Pen(_closeHover ? _text : _muted, 1.6f))
        {
            g.DrawLine(pen, cr.X + 6, cr.Y + 6, cr.Right - 6, cr.Bottom - 6);
            g.DrawLine(pen, cr.Right - 6, cr.Y + 6, cr.X + 6, cr.Bottom - 6);
        }

        // line under the title
        using (var pen = new Pen(Themes.Blend(BackColor, _text, 0.1)))
            g.DrawLine(pen, Edge + 8, HeaderH - 1, Width - Edge - 8, HeaderH - 1);

        // footer: value of this raid + reset
        using (var pen = new Pen(Themes.Blend(BackColor, _text, 0.1)))
            g.DrawLine(pen, Edge + 8, Height - FooterH, Width - Edge - 8, Height - FooterH);
        const TextFormatFlags L1 = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        string resetText = L.T("Reset");
        _resetW = TextRenderer.MeasureText(resetText, _small).Width + 12;
        var rr = ResetRect;
        int footY = Height - FooterH;
        string label = L.T("This raid") + (History.RaidCount > 0 ? $" ({History.RaidCount})" : "");
        var ls = TextRenderer.MeasureText(label, _small, Size.Empty, L1 | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, label, _small, new Rectangle(Edge + 8, footY, ls.Width + 4, FooterH), _muted, BackColor, L1 | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, TextBuilder.Money(History.RaidTotal), _sum,
            new Rectangle(Edge + 8 + ls.Width + 8, footY, Math.Max(10, rr.X - (Edge + 8 + ls.Width + 8) - 6), FooterH), _text, BackColor, L1 | TextFormatFlags.EndEllipsis);
        if (History.RaidCount > 0)
        {
            var rb = new RectangleF(rr.X, footY + 5, rr.Width, FooterH - 10);
            using (var path = Themes.RoundRect(rb, 5))
            using (var b = new SolidBrush(Themes.Blend(BackColor, _text, _resetHover ? 0.16 : 0.08)))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, resetText, _small, Rectangle.Round(rb), _resetHover ? _text : _muted, Color.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        // resize grip (three small dots in the corner)
        using (var dot = new SolidBrush(Themes.Blend(BackColor, _text, 0.3)))
            foreach (var (x, y) in new[] { (Width - 7, Height - 7), (Width - 11, Height - 7), (Width - 7, Height - 11) })
                g.FillEllipse(dot, x - 1, y - 1, 2.4f, 2.4f);

        // outline on Windows 10 (Windows 11 draws its own)
        if (Environment.OSVersion.Version.Build < 22000)
        {
            using var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10);
            using var pen = new Pen(_border);
            g.DrawPath(pen, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            History.Changed -= OnHistoryChanged;
            _title.Dispose();
            _small.Dispose();
            _sum.Dispose();
        }
        base.Dispose(disposing);
    }
}
