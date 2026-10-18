using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  The price overlay (Fluent card): picture, name with tier pill, price rows, recommendation bar.
//  Drawn from structured data (TextBuilder.BuildModel). The layout runs twice:
//  once without Graphics to measure the size, once to paint.
//  The order of the parts and the side of the picture come from the settings
//  (arranged by drag & drop in the live preview of the settings window).
// ------------------------------------------------------------

// Key: which part of the layout the row belongs to ("flea", "net", "avg", "trader", "stack")
public sealed record PriceRow(string Key, string Label, long Value, string? Sub, string? Trader);
public sealed record PriceNote(string Key, string Text);

public sealed class OverlayModel
{
    public string Name = "";
    public int Slots = 1;
    public bool UseTier;
    public Color Tier = Color.White;       // tier color (text color when tiers are off)
    public Color PriceColor = Color.White; // color of the main price: always the tier of what the item sells for
    public string? TierName;               // null = no tier label
    public bool ColorName;                 // color the name with the tier color
    public bool Frame;                     // frame the card in the tier color
    public List<PriceRow> Rows = new();
    public List<PriceNote> Notes = new();  // "Flea: no offers" ...
    public string? BestTitle;              // "Flea market" or a trader
    public string? BestText;               // own text of the bar (barter / craft), instead of "Sell to …"
    public string? BestTrader;             // trader portrait for the recommendation
    public string? BestDetail;             // "+13,887 ₽ vs. Therapist"
    public List<InfoLine> Infos = new();   // ammo values, armor class, key maps, barter hint ...
    public bool Junk;                      // below the first tier: drawn dimmed
}

// A fact line in the price box: label (dim) + text (bold), e.g. "Ammo  Pen 54 · Dmg 45"
public sealed record InfoLine(string Key, string Label, string Text, Color? Color = null);

// One part of the overlay as drawn (for drag & drop in the live preview), in overlay pixels
public sealed record OverlayPart(string Key, Rectangle Rect);

static class OverlayPainter
{
    const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    // Parts of the overlay that can be arranged; "image" is the picture (left or right)
    public static readonly string[] DefaultOrder =
        { "name", "ammo", "armor", "key", "flea", "net", "avg", "trader", "stack", "use", "best" };

    // The saved order, cleaned up: unknown keys removed, missing ones added at their default place
    public static List<string> OrderOf(AppSettings s)
    {
        var list = (s.OverlayOrder ?? Array.Empty<string>())
            .Where(k => DefaultOrder.Contains(k)).Distinct().ToList();
        for (int i = 0; i < DefaultOrder.Length; i++)
            if (!list.Contains(DefaultOrder[i]))
                list.Insert(Math.Min(i, list.Count), DefaultOrder[i]);
        return list;
    }

    // Parts that can be hidden (dragged into the tray of the live preview); "name" always stays
    public static readonly string[] Hideable =
        { "image", "ammo", "armor", "key", "flea", "net", "avg", "trader", "use", "best" };

    public static string PartName(string key) => key switch
    {
        "name" => L.T("Name"),
        "image" => L.T("Picture"),
        "ammo" => L.T("Ammo values"),
        "armor" => L.T("Armor class"),
        "key" => L.T("Key info"),
        "use" => L.T("Barter / Craft"),
        "flea" => L.T("Lowest flea"),
        "net" => L.T("After fee"),
        "avg" => L.T("24h average"),
        "trader" => L.T("Traders"),
        "stack" => L.T("Stack"),
        "best" => L.T("Where to sell"),
        _ => key
    };

    // a part is shown when its switch in "What to show" is on
    public static bool IsShown(AppSettings s, string key) => key switch
    {
        "image" => s.ShowItemImage,
        "ammo" => s.ShowAmmoStats,
        "armor" => s.ShowArmor,
        "key" => s.ShowKeyInfo,
        "use" => s.ShowUses,
        "flea" => s.ShowFleaLow,
        "net" => s.ShowFleaNet,
        "avg" => s.ShowFleaAvg,
        "trader" => s.ShowBestTrader || s.ShowAllTraders,
        "best" => s.ShowBestOption,
        _ => true
    };

    public static void SetShown(AppSettings s, string key, bool on)
    {
        switch (key)
        {
            case "image": s.ShowItemImage = on; break;
            case "ammo": s.ShowAmmoStats = on; break;
            case "armor": s.ShowArmor = on; break;
            case "key": s.ShowKeyInfo = on; break;
            case "use": s.ShowUses = on; break;
            case "flea": s.ShowFleaLow = on; break;
            case "net": s.ShowFleaNet = on; break;
            case "avg": s.ShowFleaAvg = on; break;
            case "best": s.ShowBestOption = on; break;
            case "trader":
                if (on) s.ShowBestTrader = true;
                else { s.ShowBestTrader = false; s.ShowAllTraders = false; }
                break;
        }
    }

    // Base font size 11 at 100 %; everything in the overlay scales with it
    public static float FontSize(AppSettings s) => 11f * s.OverlayScalePercent / 100f;

    public static Size Measure(OverlayModel m, AppSettings s, Image? img) =>
        Run(null, Point.Empty, m, s, img, null);

    public static void Paint(Graphics g, Point origin, OverlayModel m, AppSettings s, Image? img,
                             List<OverlayPart>? parts = null) =>
        Run(g, origin, m, s, img, parts);

    // corner radius of the overlay window
    public static float CornerRadius(AppSettings s) => 14f * FontSize(s) / 11f;

    // ---- shared drawing context ----

    sealed class Ctx : IDisposable
    {
        public Graphics? G;
        public OverlayModel M = null!;
        public Image? Img;
        public string ImageSide = "Left";   // Left | Right | Top
        public List<string> Order = null!;
        public List<OverlayPart>? Parts;
        public float K;                    // scale: font size / 11
        public Color Back, Text, Dim, Line, Accent, Tile;
        public Font Title = null!, Body = null!, Bold = null!, Small = null!;

        public int P(float v) => (int)Math.Round(v * K);

        public void Dispose()
        {
            Title.Dispose(); Body.Dispose(); Bold.Dispose(); Small.Dispose();
        }
    }

    static Size Run(Graphics? g, Point o, OverlayModel m, AppSettings s, Image? img, List<OverlayPart>? parts)
    {
        float fs = Math.Max(5f, FontSize(s));
        var back = Themes.FromArgb(s.BackColorArgb);
        var text = Themes.FromArgb(s.TextColorArgb);
        if (m.Junk) text = Themes.Blend(text, back, 0.4);   // not worth the space: everything a bit dimmer

        using var c = new Ctx
        {
            G = g,
            M = m,
            Img = img,
            ImageSide = s.ImageSide,
            Order = OrderOf(s),
            Parts = parts,
            K = fs / 11f,
            Back = back,
            Text = text,
            Dim = Themes.Blend(text, back, 0.45),
            Line = Themes.Blend(back, text, 0.2),
            Accent = Themes.FromArgb(s.ThemeAccentArgb),
            Tile = img != null && AverageLuma(img) < 95 ? Themes.Blend(back, text, 0.36) : Themes.Blend(back, text, 0.09),
            Title = UiFont.Semi(fs + 2.5f),
            Body = UiFont.Text(fs),
            Bold = UiFont.Strong(fs),
            Small = UiFont.Text(Math.Max(5f, fs - 1.5f))
        };
        if (g != null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
        return Fluent(c, o);
    }

    static Size Ms(string t, Font f) => TextRenderer.MeasureText(t, f, new Size(int.MaxValue, int.MaxValue), F);

    static void Put(Ctx c, string t, Font f, int x, int y, Color col, Color back)
    {
        if (c.G != null) TextRenderer.DrawText(c.G, t, f, new Point(x, y), col, back, F);
    }

    static void Fill(Ctx c, Color col, RectangleF r, float radius = 0)
    {
        if (c.G == null) return;
        using var br = new SolidBrush(col);
        if (radius <= 0.5f) c.G.FillRectangle(br, r);
        else { using var p = Themes.RoundRect(r, radius); c.G.FillPath(br, p); }
    }

    static void Stroke(Ctx c, Color col, RectangleF r, float radius = 0, float width = 1f)
    {
        if (c.G == null) return;
        using var pen = new Pen(col, width);
        if (radius <= 0.5f) c.G.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        else { using var p = Themes.RoundRect(r, radius); c.G.DrawPath(pen, p); }
    }

    static void Picture(Ctx c, Rectangle r, float radius)
    {
        if (c.G == null || c.Img == null) return;
        Fill(c, c.Tile, r, radius);
        int inset = Math.Max(2, r.Width / 24);
        c.G.DrawImage(c.Img, new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2));
    }

    static void Portrait(Ctx c, string trader, int x, int y, int size)
    {
        if (c.G == null) return;
        var img = TraderImages.Get(trader, size);
        if (img != null) c.G.DrawImage(img, new Rectangle(x, y, size, size));
    }

    // ---- the parts of the text column, in the chosen order ----

    enum Kind { Header, Row, Note, Best, Info }

    sealed record Block(string Key, Kind Kind, PriceRow? Row = null, string? Text = null, InfoLine? Info = null);

    static List<Block> Blocks(Ctx c)
    {
        var m = c.M;
        var list = new List<Block>();
        var used = new HashSet<object>();
        foreach (var key in c.Order)
        {
            switch (key)
            {
                case "name":
                    list.Add(new Block(key, Kind.Header));
                    break;
                case "best":
                    if (m.BestTitle != null) list.Add(new Block(key, Kind.Best));
                    break;
                default:
                    foreach (var r in m.Rows.Where(r => r.Key == key)) { list.Add(new Block(key, Kind.Row, r)); used.Add(r); }
                    foreach (var n in m.Notes.Where(n => n.Key == key)) { list.Add(new Block(key, Kind.Note, null, n.Text)); used.Add(n); }
                    foreach (var f in m.Infos.Where(f => f.Key == key)) { list.Add(new Block(key, Kind.Info, Info: f)); used.Add(f); }
                    break;
            }
        }
        // rows / notes without a known place (e.g. messages) at the end, in their own order
        foreach (var r in m.Rows.Where(r => !used.Contains(r))) list.Add(new Block(r.Key, Kind.Row, r));
        foreach (var n in m.Notes.Where(n => !used.Contains(n))) list.Add(new Block(n.Key, Kind.Note, null, n.Text));
        foreach (var f in m.Infos.Where(f => !used.Contains(f))) list.Add(new Block(f.Key, Kind.Info, Info: f));   // e.g. "prices are old"
        return list;
    }

    // ============================================================
    //  Fluent: rounded card, big picture, tier pill, portrait rows, recommendation bar
    // ============================================================
    static Size Fluent(Ctx c, Point o)
    {
        var m = c.M;
        int pad = c.P(14), gap = c.P(14);
        int imgPx = c.Img != null ? c.P(92) : 0;
        int portrait = c.P(22);

        var ns = Ms(m.Name, c.Title);
        var ps = m.TierName != null ? Ms(m.TierName, c.Small) : Size.Empty;
        int pillW = m.TierName != null ? ps.Width + c.P(16) : 0;
        int headerW = ns.Width + (pillW > 0 ? c.P(10) + pillW : 0);

        bool anyPortrait = m.Rows.Any(r => r.Trader != null);
        int indent = anyPortrait ? portrait + c.P(6) : 0;
        // notes ("Flea: banned") are drawn like a price row: label in the label column, the rest in the value column
        var noteParts = m.Notes.Select(NoteParts).ToList();
        int labelTextW = Math.Max(m.Rows.Count == 0 ? 0 : m.Rows.Max(r => Ms(Colon(r.Label), c.Body).Width),
                                  noteParts.Count == 0 ? 0 : noteParts.Max(n => Ms(n.Label, c.Body).Width));
        int labelW = labelTextW == 0 ? 0 : labelTextW + indent;
        int valueW = m.Rows.Count == 0 ? 0 : m.Rows.Max(r => Ms(TextBuilder.Money(r.Value), c.Bold).Width);
        int subW = m.Rows.Count == 0 ? 0 : m.Rows.Max(r => r.Sub == null ? 0 : Ms("· " + r.Sub, c.Small).Width);
        int rowH = Math.Max(c.Body.Height, anyPortrait ? portrait : 0) + c.P(5);
        int rowsW = labelW + c.P(10) + valueW + (subW > 0 ? c.P(8) + subW : 0);
        int notesW = noteParts.Count == 0 ? 0 : noteParts.Max(n => labelW + c.P(10) + Ms(n.Value, c.Body).Width);
        int infoW = m.Infos.Count == 0 ? 0 : m.Infos.Max(f => Ms(Colon(f.Label), c.Body).Width + c.P(10) + Ms(f.Text, c.Bold).Width);

        int barH = m.BestTitle != null ? Math.Max(c.Bold.Height, c.P(14)) + c.P(14) : 0;
        string bestText = m.BestText ?? (m.BestTitle != null ? (m.BestTitle == "Flea market" ? L.T("Sell on the flea market") : L.T("Sell to") + " " + m.BestTitle) : "");
        string bestDetail = m.BestDetail != null ? m.BestDetail.Split(" vs.")[0] : "";   // "+13,887 ₽"
        int barW = m.BestTitle != null ? c.P(10) + c.P(14) + c.P(8) + Ms(bestText, c.Bold).Width + c.P(16) + Ms(bestDetail, c.Bold).Width + c.P(10) : 0;

        int colW = Math.Max(Math.Max(Math.Max(headerW, rowsW), Math.Max(barW, notesW)), infoW);
        bool top = imgPx > 0 && c.ImageSide == "Top";
        if (top) colW = Math.Max(colW, imgPx);

        // heights of the parts in the chosen order (header and bar keep some air around them)
        var blocks = Blocks(c);
        int last = blocks.Count - 1;
        int[] above = new int[blocks.Count], height = new int[blocks.Count], below = new int[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i].Kind)
            {
                case Kind.Header:
                    above[i] = i > 0 ? c.P(4) : 0;
                    height[i] = ns.Height;
                    below[i] = i < last ? c.P(6) : 0;
                    break;
                case Kind.Best:
                    above[i] = i > 0 ? c.P(6) : 0;
                    height[i] = barH;
                    below[i] = i < last ? c.P(6) : 0;
                    break;
                case Kind.Row:
                case Kind.Info:
                    height[i] = rowH;
                    break;
                default:
                    height[i] = rowH;
                    break;
            }
        }
        int colH = 0;
        for (int i = 0; i < blocks.Count; i++) colH += above[i] + height[i] + below[i];

        int contentH = top ? imgPx + gap + colH : Math.Max(imgPx, colH);
        int w = pad + (imgPx > 0 && !top ? imgPx + gap : 0) + colW + pad;
        int h = pad + contentH + pad;
        if (c.G == null && c.Parts == null) return new Size(w, h);

        float radius = 14f * c.K;
        Fill(c, c.Back, new RectangleF(o.X, o.Y, w, h), radius);
        if (m.Frame && !m.Junk)
            Stroke(c, m.Tier, new RectangleF(o.X + 1f, o.Y + 1f, w - 2.5f, h - 2.5f), radius, Math.Max(1.5f, 2f * c.K));
        else
            Stroke(c, c.Line, new RectangleF(o.X + 0.5f, o.Y + 0.5f, w - 1.5f, h - 1.5f), radius);

        // picture left, right or above the text column
        int x = o.X + pad, y = o.Y + pad;
        if (imgPx > 0)
        {
            Rectangle pr;
            if (top) pr = new Rectangle(x + (colW - imgPx) / 2, y, imgPx, imgPx);
            else if (c.ImageSide == "Right") pr = new Rectangle(o.X + pad + colW + gap, y + (contentH - imgPx) / 2, imgPx, imgPx);
            else pr = new Rectangle(x, y + (contentH - imgPx) / 2, imgPx, imgPx);
            Picture(c, pr, 10f * c.K);
            c.Parts?.Add(new OverlayPart("image", pr));
            if (top) y += imgPx + gap;
            else if (c.ImageSide != "Right") x += imgPx + gap;
        }
        int right = x + colW;
        if (!top) y += (contentH - colH) / 2;

        bool firstRow = true;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            y += above[i];
            c.Parts?.Add(new OverlayPart(b.Key, new Rectangle(x, y - above[i], colW, above[i] + height[i] + below[i])));

            switch (b.Kind)
            {
                case Kind.Header:
                    Put(c, m.Name, c.Title, x, y, m.ColorName ? m.Tier : c.Text, c.Back);
                    if (m.TierName != null)
                    {
                        var pill = new RectangleF(right - pillW, y + (ns.Height - ps.Height - c.P(4)) / 2f, pillW, ps.Height + c.P(4));
                        var pillBack = Themes.Blend(c.Back, m.Tier, 0.18);
                        Fill(c, pillBack, pill, pill.Height / 2f);
                        Put(c, m.TierName, c.Small, (int)pill.X + c.P(8), (int)pill.Y + c.P(2), m.Tier, pillBack);
                    }
                    break;

                case Kind.Row:
                {
                    var r = b.Row!;
                    int lx = x;
                    if (r.Trader != null)
                    {
                        Portrait(c, r.Trader, x, y + (rowH - c.P(5) - portrait) / 2, portrait);
                        lx += portrait + c.P(6);
                    }
                    else if (anyPortrait) lx += portrait + c.P(6);
                    int ty = y + (rowH - c.P(5) - c.Body.Height) / 2;
                    Put(c, Colon(r.Label), c.Body, lx, ty, c.Dim, c.Back);
                    string v = TextBuilder.Money(r.Value);
                    int vx = x + labelW + c.P(10);
                    Put(c, v, c.Bold, vx, ty, firstRow && m.UseTier ? m.PriceColor : c.Text, c.Back);
                    if (r.Sub != null)
                        Put(c, "· " + r.Sub, c.Small, vx + valueW + c.P(8), ty + (c.Body.Height - c.Small.Height), c.Dim, c.Back);
                    firstRow = false;
                    break;
                }

                case Kind.Note:
                {
                    var (label, value) = NoteParts(new PriceNote(b.Key, b.Text!));
                    int ty = y + (rowH - c.P(5) - c.Body.Height) / 2;
                    Put(c, label, c.Body, x + indent, ty, c.Dim, c.Back);
                    Put(c, value, c.Body, x + labelW + c.P(10), ty, c.Dim, c.Back);
                    break;
                }

                case Kind.Info:
                {
                    var f = b.Info!;
                    int ty = y + (rowH - c.P(5) - c.Body.Height) / 2;
                    Put(c, Colon(f.Label), c.Body, x, ty, c.Dim, c.Back);
                    Put(c, f.Text, c.Bold, x + Ms(Colon(f.Label), c.Body).Width + c.P(10), ty, f.Color ?? c.Text, c.Back);
                    break;
                }

                case Kind.Best:
                    if (c.G != null)
                    {
                        var barBack = Themes.Blend(c.Back, c.Accent, 0.2);
                        var light = Themes.Blend(c.Accent, Color.White, 0.45);
                        Fill(c, barBack, new RectangleF(x, y, colW, barH), 8f * c.K);
                        int ix = x + c.P(10), icon = c.P(14), cy = y + barH / 2;
                        using (var pen = new Pen(light, Math.Max(1.5f, 2.2f * c.K)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            c.G.DrawLines(pen, new[]
                            {
                                new PointF(ix + icon * 0.15f, cy),
                                new PointF(ix + icon * 0.42f, cy + icon * 0.28f),
                                new PointF(ix + icon * 0.9f, cy - icon * 0.3f)
                            });
                        Put(c, bestText, c.Bold, ix + icon + c.P(8), cy - c.Bold.Height / 2, light, barBack);
                        Put(c, bestDetail, c.Bold, x + colW - c.P(10) - Ms(bestDetail, c.Bold).Width, cy - c.Bold.Height / 2, light, barBack);
                    }
                    break;
            }
            y += height[i] + below[i];
        }
        return new Size(w, h);
    }

    // labels end with a colon: "Flea:", "Therapist:", "Craft:"
    static string Colon(string label) =>
        label.Length == 0 || label.EndsWith(':') ? label : label + ":";

    // "Flea: banned" -> ("Flea:", "banned"); a note without ": " is all value
    static (string Label, string Value) NoteParts(PriceNote n)
    {
        int i = n.Text.IndexOf(": ", StringComparison.Ordinal);
        return i > 0 ? (n.Text[..(i + 1)], n.Text[(i + 2)..]) : ("", n.Text);   // the label keeps its ":"
    }

    // Average brightness (0-255) of the visible (non-transparent) pixels of a picture
    public static double AverageLuma(Image img)
    {
        try
        {
            using var small = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, 0, 0, 24, 24);
            }
            double sum = 0, weight = 0;
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    var px = small.GetPixel(x, y);
                    if (px.A < 40) continue;
                    double a = px.A / 255.0;
                    sum += (0.299 * px.R + 0.587 * px.G + 0.114 * px.B) * a;
                    weight += a;
                }
            return weight < 1 ? 128 : sum / weight;
        }
        catch
        {
            return 128;
        }
    }
}

// Result of a drag & drop in the live preview: new order, picture position, part hidden / shown
public sealed record LayoutEdit(List<string> Order, string ImageSide, string? Hide, string? Show);

// ------------------------------------------------------------
//  Live preview of the overlay inside the settings window, with drag & drop:
//  - drag a part: it follows the mouse, the other parts slide aside (animated), the preview
//    already shows the result while dragging
//  - drag the picture to the left, right or top
//  - drag a part out of the box: it is hidden and goes to the tray below; drag it back to show it
//  - hovering a part highlights it and shows a grip
//  The overlay is painted at its real size (into a bitmap) and shrunk to fit when needed.
// ------------------------------------------------------------
sealed class OverlayPreview : Control
{
    const int ChipH = 26, TrayPad = 8, HintH = 24;

    AppSettings? _settings;
    Item? _item;
    Image? _img;

    // the rendered overlay: Full = complete picture, Bg = the empty card (parts cut out)
    sealed class Frame : IDisposable
    {
        public Bitmap Full = null!, Bg = null!;
        public List<OverlayPart> Parts = new();
        public float Radius;
        public int Opacity = 100;
        public void Dispose() { Full.Dispose(); Bg.Dispose(); }
    }
    Frame? _frame;
    Rectangle _dest;
    float _scale = 1f;
    string _lastSig = "";

    // animation: shown top-left (overlay pixels) of every part slice, id "key#n"
    readonly Dictionary<string, PointF> _pos = new();
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    // tray with the hidden parts
    readonly List<(string Key, RectangleF Rect)> _chips = new();
    Rectangle _tray;

    // drag & hover state
    string? _dragKey;
    bool _fromTray, _dragging;
    Point _downAt, _mouse;
    SizeF _grab, _ghostSize;
    AppSettings? _tentative;
    string? _hide, _show;
    string? _hoverKey;
    bool _hoverChip;

    public Color Grid { get; set; } = Color.FromArgb(40, 42, 46);
    public Color Accent { get; set; } = Color.DodgerBlue;
    public Color HintColor { get; set; } = Color.Gray;
    public Color TextColor { get; set; } = Color.White;
    public Color ChipBack { get; set; } = Color.FromArgb(36, 38, 44);

    public event Action<LayoutEdit>? LayoutChanged;

    public OverlayPreview()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _anim.Tick += (_, _) => Animate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim.Dispose();
            _frame?.Dispose();
        }
        base.Dispose(disposing);
    }

    public void Show(AppSettings s, Item item, Image? img)
    {
        if (_item != null && _item.Name != item.Name) _pos.Clear();   // other sample: no sliding
        _settings = s;
        _item = item;
        _img = img;
        if (!_dragging) Rebuild(s);
        Invalidate();
    }

    // ---- rendering ----

    static string Sig(AppSettings s) =>
        string.Join(",", OverlayPainter.OrderOf(s)) + "|" + s.ImageSide + "|" +
        string.Concat(OverlayPainter.Hideable.Select(k => OverlayPainter.IsShown(s, k) ? "1" : "0"));

    void Rebuild(AppSettings s)
    {
        if (_item == null) return;
        var img = s.ShowItemImage ? _img : null;
        var model = TextBuilder.BuildModel(_item, s);
        var size = OverlayPainter.Measure(model, s, img);
        var back = Themes.FromArgb(s.BackColorArgb);
        var parts = new List<OverlayPart>();

        // 24 bpp: GDI text needs an opaque target (on 32 bpp ARGB the text would turn transparent)
        var full = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.Clear(back);
            OverlayPainter.Paint(g, Point.Empty, model, s, img, parts);
        }
        var bg = new Bitmap(full);
        using (var g = Graphics.FromImage(bg))
        using (var br = new SolidBrush(back))
            foreach (var p in parts) g.FillRectangle(br, p.Rect);

        var old = _frame;
        _frame = new Frame
        {
            Full = full, Bg = bg, Parts = parts,
            Radius = OverlayPainter.CornerRadius(s),
            Opacity = s.OverlayOpacityPercent
        };
        old?.Dispose();

        var slices = Slices();
        foreach (var (id, r) in slices)
            if (!_pos.ContainsKey(id)) _pos[id] = r.Location;
        foreach (var id in _pos.Keys.Except(slices.Select(x => x.Id)).ToList())
            _pos.Remove(id);
        _anim.Start();
    }

    List<(string Id, Rectangle Rect)> Slices()
    {
        var list = new List<(string Id, Rectangle Rect)>();
        if (_frame == null) return list;
        var count = new Dictionary<string, int>();
        foreach (var p in _frame.Parts)
        {
            count.TryGetValue(p.Key, out int n);
            count[p.Key] = n + 1;
            list.Add((p.Key + "#" + n, p.Rect));
        }
        return list;
    }

    // all rectangles of one part merged (all trader rows are one part)
    Rectangle? UnionOf(string key)
    {
        if (_frame == null) return null;
        var rects = _frame.Parts.Where(p => p.Key == key).Select(p => p.Rect).ToList();
        if (rects.Count == 0) return null;
        var u = rects[0];
        foreach (var r in rects) u = Rectangle.Union(u, r);
        return u;
    }

    // slide every slice a bit closer to its place
    void Animate()
    {
        bool moving = false;
        foreach (var (id, r) in Slices())
        {
            if (!_pos.TryGetValue(id, out var p)) { _pos[id] = r.Location; continue; }
            float dx = r.X - p.X, dy = r.Y - p.Y;
            if (Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f)
            {
                if (dx != 0 || dy != 0) _pos[id] = r.Location;
                continue;
            }
            _pos[id] = new PointF(p.X + dx * 0.28f, p.Y + dy * 0.28f);
            moving = true;
        }
        if (!moving) _anim.Stop();
        Invalidate();
    }

    // ---- geometry ----

    PointF ToOverlay(Point p) => new((p.X - _dest.X) / _scale, (p.Y - _dest.Y) / _scale);

    RectangleF ToControl(RectangleF r) =>
        new(_dest.X + r.X * _scale, _dest.Y + r.Y * _scale, r.Width * _scale, r.Height * _scale);

    void LayoutChips()
    {
        _chips.Clear();
        var keys = _settings == null ? new List<string>()
            : OverlayPainter.Hideable.Where(k => !OverlayPainter.IsShown(_settings, k)).ToList();
        using var f = UiFont.Text(8.5f);
        float left = 12 + TrayPad, maxX = Width - 12 - TrayPad, x = left, y = 0;
        int rows = 1;
        var temp = new List<(string Key, RectangleF Rect)>();
        foreach (var k in keys)
        {
            float w = TextRenderer.MeasureText(OverlayPainter.PartName(k), f).Width + 22;
            if (x + w > maxX && x > left) { x = left; y += ChipH + 6; rows++; }
            temp.Add((k, new RectangleF(x, y, w, ChipH)));
            x += w + 6;
        }
        int trayH = rows * (ChipH + 6) - 6 + 2 * TrayPad;
        _tray = new Rectangle(12, Height - HintH - trayH, Width - 24, trayH);
        foreach (var (k, r) in temp)
            _chips.Add((k, new RectangleF(r.X, _tray.Y + TrayPad + r.Y, r.Width, r.Height)));
    }

    string? ChipAt(Point p) => _chips.FirstOrDefault(c => c.Rect.Contains(p)).Key;

    string? PartAt(Point p)
    {
        if (_frame == null || !_dest.Contains(p)) return null;
        var o = ToOverlay(p);
        var hit = _frame.Parts.FirstOrDefault(x => x.Key == "image" && x.Rect.Contains((int)o.X, (int)o.Y))
               ?? _frame.Parts.FirstOrDefault(x => x.Key != "image" && x.Rect.Contains((int)o.X, (int)o.Y));
        return hit?.Key;
    }

    // ---- drag & drop ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _settings == null) return;
        _downAt = _mouse = e.Location;
        _dragging = false;

        var chip = _chips.FirstOrDefault(c => c.Rect.Contains(e.Location));
        if (chip.Key != null)
        {
            _dragKey = chip.Key;
            _fromTray = true;
            _grab = new SizeF(e.X - chip.Rect.X, e.Y - chip.Rect.Y);
            _ghostSize = chip.Rect.Size;
        }
        else
        {
            _dragKey = PartAt(e.Location);
            _fromTray = false;
            if (_dragKey != null && UnionOf(_dragKey) is Rectangle u)
            {
                var r = ToControl(u);
                _grab = new SizeF(e.X - r.X, e.Y - r.Y);
                _ghostSize = r.Size;
            }
        }
        if (_dragKey != null) Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.Location;
        if (_dragKey == null)
        {
            // hover: highlight + grip
            string? chip = ChipAt(e.Location);
            string? key = chip ?? PartAt(e.Location);
            bool isChip = chip != null;
            if (key != _hoverKey || isChip != _hoverChip)
            {
                _hoverKey = key;
                _hoverChip = isChip;
                Invalidate();
            }
            Cursor = key != null ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        if (!_dragging && (Math.Abs(e.X - _downAt.X) > 4 || Math.Abs(e.Y - _downAt.Y) > 4))
        {
            _dragging = true;
            _hoverKey = null;
        }
        if (_dragging)
        {
            UpdateTentative();
            Invalidate();
        }
    }

    // what the layout would look like if the part were dropped here (shown live while dragging)
    void UpdateTentative()
    {
        if (_settings == null || _dragKey == null) return;
        var s = _settings.Clone();
        _hide = null;
        _show = null;
        bool inside = _scale > 0 && Rectangle.Inflate(_dest, 18, 18).Contains(_mouse) && _mouse.Y < _tray.Top - 4;
        if (!inside)
        {
            // out of the box: hide it (the name always stays)
            if (!_fromTray && _dragKey != "name")
            {
                OverlayPainter.SetShown(s, _dragKey, false);
                _hide = _dragKey;
            }
        }
        else
        {
            if (_fromTray)
            {
                OverlayPainter.SetShown(s, _dragKey, true);
                _show = _dragKey;
            }
            if (_dragKey == "image")
                s.ImageSide = ImageZone();
            else
            {
                var order = OverlayPainter.OrderOf(s);
                order.Remove(_dragKey);
                order.Insert(InsertIndex(order), _dragKey);
                s.OverlayOrder = order.ToArray();
            }
        }
        _tentative = s;
        string sig = Sig(s);
        if (sig != _lastSig)
        {
            _lastSig = sig;
            Rebuild(s);
        }
    }

    // position in the order where the dragged part goes: before the first part below its middle
    int InsertIndex(List<string> order)
    {
        if (_frame == null) return order.Count;
        var groups = new List<(string Key, int Top, int Bottom)>();
        foreach (var p in _frame.Parts.Where(x => x.Key != "image" && x.Key != _dragKey))
        {
            int i = groups.FindIndex(g => g.Key == p.Key);
            if (i < 0) groups.Add((p.Key, p.Rect.Top, p.Rect.Bottom));
            else groups[i] = (p.Key, Math.Min(groups[i].Top, p.Rect.Top), Math.Max(groups[i].Bottom, p.Rect.Bottom));
        }
        if (groups.Count == 0) return order.Count;
        float ghostMid = (_mouse.Y - _grab.Height + _ghostSize.Height / 2 - _dest.Y) / _scale;
        foreach (var g in groups.OrderBy(g => g.Top))
            if (ghostMid < (g.Top + g.Bottom) / 2f)
                return Math.Max(0, order.IndexOf(g.Key));
        var last = groups.OrderBy(g => g.Top).Last();
        return Math.Min(order.Count, order.IndexOf(last.Key) + 1);
    }

    // picture: upper part of the box = above the text, otherwise left / right half
    string ImageZone()
    {
        if (_frame == null) return "Left";
        var o = ToOverlay(_mouse);
        if (o.Y < _frame.Full.Height * 0.25f) return "Top";
        return o.X < _frame.Full.Width / 2f ? "Left" : "Right";
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        if (!_dragging || _dragKey == null || _settings == null)
        {
            _dragKey = null;
            _dragging = false;
            return;
        }

        _mouse = e.Location;
        UpdateTentative();
        string key = _dragKey;
        var result = _tentative ?? _settings;
        string? hide = _hide, show = _show;

        // the dropped part slides from where it was let go into its place
        if (hide == null && UnionOf(key) is Rectangle u && _scale > 0)
        {
            var ghost = new PointF((_mouse.X - _grab.Width - _dest.X) / _scale, (_mouse.Y - _grab.Height - _dest.Y) / _scale);
            foreach (var (id, r) in Slices().Where(x => x.Id.StartsWith(key + "#")))
                _pos[id] = new PointF(ghost.X + r.X - u.X, ghost.Y + r.Y - u.Y);
            _anim.Start();
        }

        bool changed = Sig(result) != Sig(_settings);
        _dragKey = null;
        _dragging = false;
        _tentative = null;
        _hide = _show = null;
        _lastSig = "";

        if (changed)
            LayoutChanged?.Invoke(new LayoutEdit(OverlayPainter.OrderOf(result), result.ImageSide, hide, show));
        else
            Rebuild(_settings);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragKey == null && _hoverKey != null)
        {
            _hoverKey = null;
            Invalidate();
        }
    }

    // ---- painting ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));
        LayoutChips();

        // rounded "stash" area with grid cells
        var area = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Themes.RoundRect(area, 12f))
        {
            using (var br = new SolidBrush(BackColor))
                g.FillPath(br, path);
            g.SetClip(path);
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Grid))
            {
                const int cell = 44;
                for (int x = 10; x < Width; x += cell) g.DrawLine(pen, x, 0, x, Height);
                for (int y = 10; y < Height; y += cell) g.DrawLine(pen, 0, y, Width, y);
            }
            g.ResetClip();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(Grid, 1f);
            g.DrawPath(border, path);
        }

        PaintOverlay(g);
        PaintTray(g);

        // unobtrusive hint at the bottom
        using (var hintFont = UiFont.Text(8f))
            TextRenderer.DrawText(g, L.T("Drag & Drop"), hintFont, new Rectangle(0, Height - HintH, Width, HintH - 4), HintColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (_dragging && _dragKey != null) PaintGhost(g);
    }

    void PaintOverlay(Graphics g)
    {
        if (_frame == null) return;
        int zoneH = _tray.Top - 6;
        int w = _frame.Full.Width, h = _frame.Full.Height;
        _scale = Math.Max(0.05f, Math.Min(1f, Math.Min((Width - 24f) / w, (zoneH - 20f) / h)));
        int dw = (int)(w * _scale), dh = (int)(h * _scale);
        _dest = new Rectangle((Width - dw) / 2, Math.Max(10, (zoneH - dh) / 2), dw, dh);

        // compose the card: empty card + every part at its (animated) position
        using var frame = new Bitmap(_frame.Bg);
        using (var fg = Graphics.FromImage(frame))
        {
            foreach (var (id, r) in Slices())
            {
                if (_dragging && _dragKey != null && id.StartsWith(_dragKey + "#")) continue;   // its place stays empty
                var p = _pos.TryGetValue(id, out var v) ? v : r.Location;
                fg.DrawImage(_frame.Full, new RectangleF(p.X, p.Y, r.Width, r.Height), r, GraphicsUnit.Pixel);
            }
            // the free place of the dragged part
            if (_dragging && _dragKey != null && _hide == null && UnionOf(_dragKey) is Rectangle u)
            {
                fg.SmoothingMode = SmoothingMode.AntiAlias;
                using var dash = new Pen(Color.FromArgb(150, Accent), 1.5f) { DashStyle = DashStyle.Dash };
                using var slot = Themes.RoundRect(new RectangleF(u.X + 1, u.Y + 1, u.Width - 2, u.Height - 2), 6);
                fg.DrawPath(dash, slot);
            }
        }

        using (var clip = Themes.RoundRect(_dest, _frame.Radius * _scale))
        using (var attr = new ImageAttributes())
        {
            attr.SetColorMatrix(new ColorMatrix { Matrix33 = _frame.Opacity / 100f });
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(frame, _dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attr);
            g.ResetClip();
        }

        // hover: soft highlight and a grip on the left
        if (!_dragging && _hoverKey != null && !_hoverChip && UnionOf(_hoverKey) is Rectangle hu)
        {
            var r = ToControl(hu);
            r.Inflate(3, 1);
            using (var path = Themes.RoundRect(r, 6))
            {
                using (var fill = new SolidBrush(Color.FromArgb(30, Accent)))
                    g.FillPath(fill, path);
                using var pen = new Pen(Color.FromArgb(150, Accent), 1f);
                g.DrawPath(pen, path);
            }
            using var dot = new SolidBrush(Accent);
            float gx = r.Left - 8, gy = r.Top + r.Height / 2f - 5;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 2; col++)
                    g.FillEllipse(dot, gx + col * 4, gy + row * 4, 2.4f, 2.4f);
        }
    }

    void PaintTray(Graphics g)
    {
        bool hot = _dragging && _hide != null;   // a part is about to be hidden
        using (var path = Themes.RoundRect(new RectangleF(_tray.X + 0.5f, _tray.Y + 0.5f, _tray.Width - 1, _tray.Height - 1), 10))
        {
            using (var fill = new SolidBrush(hot ? Color.FromArgb(40, Accent) : Color.FromArgb(90, Color.Black)))
                g.FillPath(fill, path);
            using var pen = new Pen(hot ? Accent : Grid, hot ? 1.5f : 1f) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        if (_chips.Count == 0)
        {
            using var f = UiFont.Text(8.5f);
            TextRenderer.DrawText(g, hot ? L.T("Release to hide") : L.T("Drag parts here to hide them"), f, _tray,
                hot ? Accent : HintColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        foreach (var (key, r) in _chips)
        {
            bool dragged = _dragging && _fromTray && key == _dragKey;
            bool hover = !_dragging && _hoverChip && key == _hoverKey;
            DrawChip(g, r, OverlayPainter.PartName(key), hover ? Accent : Grid, dragged ? 0.35f : 1f);
        }
    }

    void DrawChip(Graphics g, RectangleF r, string text, Color border, float alpha)
    {
        int a = (int)(255 * alpha);
        using (var path = Themes.RoundRect(r, r.Height / 2f))
        {
            using (var fill = new SolidBrush(Color.FromArgb(a, ChipBack)))
                g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(a, border), 1.2f);
            g.DrawPath(pen, path);
        }
        using var f = UiFont.Text(8.5f);
        using var br = new SolidBrush(Color.FromArgb(a, TextColor));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.DrawString(text, f, br, r, sf);
    }

    // the dragged part under the mouse: its real picture, or a chip when it is not in the box
    void PaintGhost(Graphics g)
    {
        var at = new PointF(_mouse.X - _grab.Width, _mouse.Y - _grab.Height);
        if (_hide == null && _frame != null && UnionOf(_dragKey!) is Rectangle u)
        {
            var dst = new RectangleF(at.X, at.Y, u.Width * _scale, u.Height * _scale);
            using (var shadow = Themes.RoundRect(new RectangleF(dst.X + 2, dst.Y + 4, dst.Width, dst.Height), 8))
            using (var sb = new SolidBrush(Color.FromArgb(90, Color.Black)))
                g.FillPath(sb, shadow);
            using (var attr = new ImageAttributes())
            {
                attr.SetColorMatrix(new ColorMatrix { Matrix33 = 0.92f });
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_frame.Full, Rectangle.Round(dst), u.X, u.Y, u.Width, u.Height, GraphicsUnit.Pixel, attr);
            }
            using var outline = Themes.RoundRect(dst, 8);
            using var pen = new Pen(Accent, 1.5f);
            g.DrawPath(pen, outline);
            return;
        }

        using var f = UiFont.Text(8.5f);
        float w = TextRenderer.MeasureText(OverlayPainter.PartName(_dragKey!), f).Width + 22;
        DrawChip(g, new RectangleF(at.X, at.Y, w, ChipH), OverlayPainter.PartName(_dragKey!), Accent, 1f);
    }
}
