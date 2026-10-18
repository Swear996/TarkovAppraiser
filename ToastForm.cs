using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Short notice at the top of the screen (e.g. "Automatic mode paused"): fades in, stays about
//  1.5 seconds, fades out. Never takes the focus, ignores the mouse, invisible to screenshots.
// ------------------------------------------------------------
sealed class ToastForm : Form
{
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    string _text = "";
    bool _paused;
    Color _back, _fore, _accent;
    int _tick;
    Font _font = UiFont.Semi(11f);

    public ToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Step();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateCapture();
    }

    public void UpdateCapture()
    {
        if (IsHandleCreated) CaptureMode.Apply(Handle, SettingsStore.Current.ShowInScreenshots);
    }

    // paused = pause sign, otherwise a play sign
    public void Pop(string text, bool paused, AppSettings s)
    {
        _text = text;
        _paused = paused;
        _back = Themes.FromArgb(s.ThemePanelArgb);
        _fore = Themes.FromArgb(s.ThemeTextArgb);
        _accent = Themes.FromArgb(s.ThemeAccentArgb);

        int textW = TextRenderer.MeasureText(text, _font).Width;
        int w = 18 + 18 + 12 + textW + 20, h = 44;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        SetBounds(area.X + (area.Width - w) / 2, area.Top + 48, w, h);
        var old = Region;
        using (var path = Themes.RoundRect(new RectangleF(0, 0, w, h), h / 2f))
            Region = new Region(path);
        old?.Dispose();

        BackColor = _back;
        _tick = 0;
        Opacity = 0;
        if (!Visible) Show();
        Invalidate();
        _timer.Start();
    }

    // fade in (10 frames), stay, fade out (20 frames)
    void Step()
    {
        _tick++;
        const int fadeIn = 10, stay = 95, fadeOut = 20;
        if (_tick <= fadeIn) Opacity = 0.96 * _tick / fadeIn;
        else if (_tick <= fadeIn + stay) Opacity = 0.96;
        else if (_tick <= fadeIn + stay + fadeOut) Opacity = 0.96 * (fadeIn + stay + fadeOut - _tick) / fadeOut;
        else
        {
            _timer.Stop();
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f - 1))
        using (var pen = new Pen(Themes.Blend(_back, _accent, 0.6), 1.5f))
            g.DrawPath(pen, path);

        // pause bars or play triangle
        using (var br = new SolidBrush(_accent))
        {
            float x = 18, y = Height / 2f - 8;
            if (_paused)
            {
                using var p1 = Themes.RoundRect(new RectangleF(x + 2, y, 5, 16), 1.5f);
                using var p2 = Themes.RoundRect(new RectangleF(x + 11, y, 5, 16), 1.5f);
                g.FillPath(br, p1);
                g.FillPath(br, p2);
            }
            else
                g.FillPolygon(br, new[] { new PointF(x + 3, y), new PointF(x + 17, y + 8), new PointF(x + 3, y + 16) });
        }
        TextRenderer.DrawText(g, _text, _font, new Rectangle(48, 0, Width - 48 - 16, Height), _fore, _back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
