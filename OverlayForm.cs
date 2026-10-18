using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

class OverlayForm : Form
{
    const int WS_EX_TRANSPARENT = 0x20;   // click-through
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_LAYERED = 0x80000;

    readonly System.Windows.Forms.Timer _timer = new();
    OverlayModel? _model;
    AppSettings? _settings;
    Image? _itemImage;      // picture of the item (shared cache, never disposed here)

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
        _fade.Tick += (_, _) => FadeStep();
    }

    // ---- soft fade-in (about 120 ms) ----
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    double _fadeTarget = 1;
    DateTime _fadeStart;
    const double FadeMs = 120;

    void FadeStep()
    {
        double t = Math.Clamp((DateTime.UtcNow - _fadeStart).TotalMilliseconds / FadeMs, 0, 1);
        double eased = 1 - (1 - t) * (1 - t);   // fast start, soft end
        Opacity = _fadeTarget * eased;
        if (t >= 1) _fade.Stop();
    }

    protected override bool ShowWithoutActivation => true;

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;   // Windows 10 2004+

    // true = the overlay never appears in screenshots, so it does not have to be hidden before capturing
    public bool ExcludedFromCapture { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateCapture();
    }

    // visible in screenshots / recordings or not (setting); excluded = no need to hide it before capturing
    public void UpdateCapture()
    {
        if (!IsHandleCreated) return;
        ExcludedFromCapture = CaptureMode.Apply(Handle, SettingsStore.Current.ShowInScreenshots);
        Log.Write("Overlay excluded from screen capture: " + ExcludedFromCapture);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_model != null && _settings != null)
            OverlayPainter.Paint(e.Graphics, Point.Empty, _model, _settings, _itemImage);
    }

    // Short message ("Not recognized" ...): first line as the title, the rest as small notes
    public void ShowMessage(string message, Point cursor, AppSettings s)
    {
        var lines = message.Replace("\r", "").Split('\n');
        _item = null;
        var m = new OverlayModel { Name = lines[0], Tier = Themes.FromArgb(s.TextColorArgb) };
        m.Notes.AddRange(lines.Skip(1).Where(l => l.Trim().Length > 0).Select(l => new PriceNote("", l)));
        Present(m, null, cursor, s);
    }

    // Prices of an item
    public void ShowItem(Item item, Point cursor, AppSettings s, Image? itemImage, int? stackCount = null, (int Cur, int Max)? durability = null)
    {
        _item = item;
        _rawImage = itemImage;
        _cursor = cursor;
        _count = stackCount;
        _durability = durability;
        Present(TextBuilder.BuildModel(item, s, stackCount, durability), s.ShowItemImage ? itemImage : null, cursor, s);
    }

    int? _count;
    (int Cur, int Max)? _durability;

    Item? _item;          // shown item (redrawn when more facts arrive, e.g. the price history)
    Image? _rawImage;
    Point _cursor;

    // new facts for the shown item: redraw in place, the display time keeps running
    public void RefreshItem(string? id = null)
    {
        if (!Visible || _item == null || _settings == null || (id != null && _item.Id != id)) return;
        Present(TextBuilder.BuildModel(_item, _settings, _count, _durability), _settings.ShowItemImage ? _rawImage : null, _cursor, _settings, restart: false);
    }

    void Present(OverlayModel m, Image? img, Point cursor, AppSettings s, bool restart = true)
    {
        if (restart == false && _model == null) return;
        BackColor = Themes.FromArgb(s.BackColorArgb);
        _fadeTarget = s.OverlayOpacityPercent / 100.0;
        bool fadeIn = s.OverlayFade && restart && !Visible;
        if (fadeIn) Opacity = 0.01;
        else if (!_fade.Enabled) Opacity = _fadeTarget;
        _timer.Interval = Math.Max(1, s.OverlaySeconds) * 1000;

        _model = m;
        _settings = s;
        _itemImage = img;
        ClientSize = OverlayPainter.Measure(m, s, img);

        // rounded window
        var oldRegion = Region;
        using (var path = Themes.RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), OverlayPainter.CornerRadius(s)))
            Region = new Region(path);
        oldRegion?.Dispose();

        Invalidate();
        Place(cursor, s, restart);
    }

    // Puts the overlay next to the cursor (or into a screen corner) and shows it
    void Place(Point cursor, AppSettings s, bool restart = true)
    {
        var area = Screen.FromPoint(cursor).WorkingArea;
        int w = Width;
        int h = Height;
        Point loc;

        switch (s.Position)
        {
            case "TopLeft":
                loc = new Point(area.Left + 20, area.Top + 20);
                break;
            case "TopRight":
                loc = new Point(area.Right - w - 20, area.Top + 20);
                break;
            case "BottomLeft":
                loc = new Point(area.Left + 20, area.Bottom - h - 20);
                break;
            case "BottomRight":
                loc = new Point(area.Right - w - 20, area.Bottom - h - 20);
                break;
            default:
                int x = cursor.X + s.OffsetX;
                int y = cursor.Y + s.OffsetY;
                if (x + w > area.Right) x = cursor.X - w - Math.Abs(s.OffsetX);
                if (y + h > area.Bottom) y = area.Bottom - h;
                loc = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
                break;
        }

        Location = loc;
        TopMost = true;
        bool fadeIn = s.OverlayFade && restart && !Visible;
        Show();
        if (fadeIn)
        {
            _fadeStart = DateTime.UtcNow;
            _fade.Start();
        }
        if (!restart) return;
        _timer.Stop();
        _timer.Start();
    }

    public void HideNow()
    {
        _timer.Stop();
        _fade.Stop();
        Hide();
    }
}
