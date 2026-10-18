using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  The last items whose price was shown (hotkey or automatic mode).
//  Shown in Settings > History, in the tray menu and in the in-game list. Kept in history.txt,
//  only the item id is stored: the prices always come from the current price list.
//  Also counts the loot of the current raid (raid.txt) until it is reset.
// ------------------------------------------------------------
sealed record RecentScan(string Id, DateTime Time, int? Count);

static class History
{
    public const int Max = 20;
    static readonly List<RecentScan> _list = new();
    static readonly Dictionary<string, int?> _raid = new();   // items of this raid: id -> counted stack

    public static event Action? Changed;
    public static event Action<Item, int?>? ShowRequested;    // a line was clicked: show its price box
    public static IReadOnlyList<RecentScan> Items => _list;

    static string FilePath => Path.Combine(SettingsStore.Dir, "history.txt");
    static string RaidPath => Path.Combine(SettingsStore.Dir, "raid.txt");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                _list.Clear();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('\t');
                    if (p.Length < 2 || p[0].Length == 0 || !long.TryParse(p[1], out long ticks)) continue;
                    int? count = p.Length > 2 && int.TryParse(p[2], out int n) && n > 0 ? n : null;
                    _list.Add(new RecentScan(p[0], new DateTime(ticks, DateTimeKind.Local), count));
                    if (_list.Count >= Max) break;
                }
            }
            if (File.Exists(RaidPath))
            {
                _raid.Clear();
                foreach (var line in File.ReadAllLines(RaidPath))
                {
                    var p = line.Split('\t');
                    if (p[0].Length == 0) continue;
                    _raid[p[0]] = p.Length > 1 && int.TryParse(p[1], out int n) && n > 0 ? n : null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read the history: " + ex.Message);
        }
    }

    public static void Add(Item item, int? count)
    {
        if (string.IsNullOrEmpty(item.Id) || ReferenceEquals(item, MainForm.Demo)) return;
        Stats.OnCheck(item, ValueOf(item, count), firstInRaid: !_raid.ContainsKey(item.Id));
        _list.RemoveAll(r => r.Id == item.Id);   // the same item again: move it to the top
        _list.Insert(0, new RecentScan(item.Id, DateTime.Now, count));
        if (_list.Count > Max) _list.RemoveRange(Max, _list.Count - Max);
        _raid[item.Id] = count;                   // checked twice = counted once
        Save();
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        _list.RemoveAll(r => r.Id == id);
        _raid.Remove(id);
        Save();
        Changed?.Invoke();
    }

    // prices changed: redraw the lists
    public static void Refresh() => Changed?.Invoke();

    public static void Clear()
    {
        _list.Clear();
        Save();
        Changed?.Invoke();
    }

    // ---- raid total ----
    public static int RaidCount => _raid.Count;

    public static long RaidTotal =>
        _raid.Sum(kv => MainForm.ById(kv.Key) is Item i ? ValueOf(i, kv.Value) : 0);

    public static void ResetRaid()
    {
        if (_raid.Count > 0) Stats.OnRaidEnd(RaidTotal);
        _raid.Clear();
        Save();
        Changed?.Invoke();
        Log.Write("Raid total reset");
    }

    public static void RequestShow(Item item, int? count) => ShowRequested?.Invoke(item, count);

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            File.WriteAllLines(FilePath, _list.Select(r => $"{r.Id}\t{r.Time.Ticks}\t{r.Count?.ToString() ?? ""}"));
            File.WriteAllLines(RaidPath, _raid.Select(kv => $"{kv.Key}\t{kv.Value?.ToString() ?? ""}"));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save the history: " + ex.Message);
        }
    }

    // value shown in the list: the counted stack, else one cell
    public static long ValueOf(Item i, int? count) =>
        count is int n && i.StackMax > 1 ? Tiers.Value(i) * Math.Min(n, i.StackMax) : Tiers.CellValue(i);
}

// ------------------------------------------------------------
//  Statistics over all raids (Settings > Statistics), kept in stats.json.
//  Values are those at the time of checking (prices change later).
// ------------------------------------------------------------
sealed record StatFind(string Id, long Value, DateTime Time);

sealed class StatsData
{
    public DateTime Since { get; set; } = DateTime.Now;
    public int Checks { get; set; }                 // every price box shown
    public long CheckedValue { get; set; }          // value of the checked items (each item once per raid)
    public int Raids { get; set; }                  // raids finished with "Reset"
    public long RaidsValue { get; set; }
    public long BestRaid { get; set; }
    public List<StatFind> Top { get; set; } = new();               // most valuable finds
    public Dictionary<string, int> Counts { get; set; } = new();   // how often each item was checked
}

static class Stats
{
    public const int TopCount = 5;
    static StatsData? _data;
    static string FilePath => Path.Combine(SettingsStore.Dir, "stats.json");

    public static StatsData Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(FilePath)) _data = JsonSerializer.Deserialize<StatsData>(File.ReadAllText(FilePath));
            }
            catch (Exception ex) { Log.Write("Could not read the statistics: " + ex.Message); }
            return _data ??= new StatsData();
        }
    }

    public static void OnCheck(Item item, long value, bool firstInRaid)
    {
        var d = Data;
        d.Checks++;
        d.Counts[item.Id] = d.Counts.GetValueOrDefault(item.Id) + 1;
        if (firstInRaid) d.CheckedValue += value;
        if (value > 0)
        {
            var old = d.Top.FirstOrDefault(t => t.Id == item.Id);
            if (old == null || value > old.Value)
            {
                d.Top.RemoveAll(t => t.Id == item.Id);
                d.Top.Add(new StatFind(item.Id, value, DateTime.Now));
                d.Top = d.Top.OrderByDescending(t => t.Value).Take(TopCount).ToList();
            }
        }
        Save();
    }

    public static void OnRaidEnd(long total)
    {
        var d = Data;
        d.Raids++;
        d.RaidsValue += total;
        d.BestRaid = Math.Max(d.BestRaid, total);
        Save();
    }

    public static void Reset()
    {
        _data = new StatsData();
        Save();
        History.Refresh();
    }

    public static List<(string Id, int Count)> MostChecked() =>
        Data.Counts.OrderByDescending(kv => kv.Value).Take(TopCount).Select(kv => (kv.Key, kv.Value)).ToList();

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data));
        }
        catch (Exception ex) { Log.Write("Could not save the statistics: " + ex.Message); }
    }
}

// ------------------------------------------------------------
//  Links of an item: tarkov.dev page and wiki page (asked from api.tarkov.dev once per item,
//  with a search as fallback when the server does not answer)
// ------------------------------------------------------------
static class ItemLinks
{
    static readonly Dictionary<string, (string? Dev, string? Wiki)> Cache = new();

    public static async Task<(string Dev, string Wiki)> GetAsync(Item i)
    {
        if (!Cache.TryGetValue(i.Id, out var c))
        {
            try
            {
                string query = "{ item(id: \"" + i.Id + "\") { link wikiLink } }";
                using var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
                using var resp = await TarkovApi.Http.PostAsync("https://api.tarkov.dev/graphql", content);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.Object)
                {
                    string? Str(string n) => it.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    c = (Str("link"), Str("wikiLink"));
                    Cache[i.Id] = c;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Item links: " + ex.Message);
            }
        }
        string name = Uri.EscapeDataString(i.Name);
        return (c.Dev ?? "https://tarkov.dev/?search=" + name,
                c.Wiki ?? "https://escapefromtarkov.fandom.com/wiki/Special:Search?query=" + name);
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Could not open " + url + ": " + ex.Message, true); }
    }
}

// ------------------------------------------------------------
//  List of the last items: picture, name, when, value in the tier color.
//  Click = show the price box again, right click = links / copy / remove, mouse wheel = scroll.
// ------------------------------------------------------------
sealed class HistoryList : Control
{
    public Color TextColor { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;
    public Color Hover { get; set; } = Color.FromArgb(40, 40, 46);
    public Color Tile { get; set; } = Color.FromArgb(30, 30, 36);
    public Color Line { get; set; } = Color.FromArgb(50, 50, 56);
    public bool AutoHeight { get; set; } = true;   // true = as high as all lines (settings page), false = fixed, scrolls

    float _scale = 1f;
    int _rowH = 54;
    Font _name = UiFont.Semi(10f), _small = UiFont.Text(8.5f), _value = UiFont.Strong(10f);
    readonly HashSet<string> _loading = new();
    List<(RecentScan Scan, Item Item)> _rows = new();
    int _hover = -1, _offset;
    ContextMenuStrip? _menu;

    public HistoryList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Reload();
    }

    public int Count => _rows.Count;
    int S(float v) => (int)Math.Round(v * _scale);

    // line height and text size (the in-game list follows the size of the price box)
    public void SetScale(float scale)
    {
        scale = Math.Clamp(scale, 0.6f, 2f);
        if (Math.Abs(scale - _scale) < 0.01f) return;
        _scale = scale;
        _rowH = S(54);
        _name.Dispose(); _small.Dispose(); _value.Dispose();
        _name = UiFont.Semi(10f * scale);
        _small = UiFont.Text(8.5f * scale);
        _value = UiFont.Strong(10f * scale);
        Reload();
    }

    public void Reload()
    {
        _rows = History.Items.Select(r => (r, MainForm.ById(r.Id)))
                             .Where(x => x.Item2 != null).Select(x => (x.r, x.Item2!)).ToList();
        if (AutoHeight) Height = Math.Max(1, _rows.Count) * _rowH;
        ClampOffset();
        Invalidate();
    }

    int MaxOffset => Math.Max(0, _rows.Count * _rowH - Height);
    void ClampOffset() => _offset = Math.Clamp(_offset, 0, MaxOffset);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampOffset();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (MaxOffset == 0) return;
        _offset -= Math.Sign(e.Delta) * _rowH;
        ClampOffset();
        UpdateHover(e.Location);
        Invalidate();
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    int RowAt(Point p)
    {
        int i = (p.Y + _offset) / _rowH;
        return p.Y >= 0 && i >= 0 && i < _rows.Count ? i : -1;
    }

    void UpdateHover(Point p)
    {
        int i = RowAt(p);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = RowAt(e.Location);
        if (i < 0) return;
        var (scan, item) = _rows[i];
        if (e.Button == MouseButtons.Left) History.RequestShow(item, scan.Count);
        else if (e.Button == MouseButtons.Right) ShowMenu(item, scan.Count, e.Location);
    }

    void ShowMenu(Item item, int? count, Point at)
    {
        _menu?.Dispose();
        var m = new ContextMenuStrip { ImageScalingSize = new Size(20, 20) };
        m.Items.Add(L.T("Show prices"), null, (_, _) => History.RequestShow(item, count));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(L.T("Open on tarkov.dev"), null, async (_, _) => ItemLinks.Open((await ItemLinks.GetAsync(item)).Dev));
        m.Items.Add(L.T("Open in the wiki"), null, async (_, _) => ItemLinks.Open((await ItemLinks.GetAsync(item)).Wiki));
        m.Items.Add(L.T("Copy name"), null, (_, _) => { try { Clipboard.SetText(item.Name); } catch { } });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(L.T("Remove from list"), null, (_, _) => History.Remove(item.Id));
        var s = SettingsStore.Current;
        Themes.StyleMenu(m, Themes.FromArgb(s.ThemePanelArgb), Themes.FromArgb(s.ThemeTextArgb), Themes.FromArgb(s.ThemeAccentArgb));
        m.Opened += (_, _) => Themes.RoundCorners(m.Handle, Themes.Blend(Themes.FromArgb(s.ThemePanelArgb), Themes.FromArgb(s.ThemeTextArgb), 0.16));
        _menu = m;
        m.Show(this, at);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        UiPaint.Smooth(g);
        const TextFormatFlags F = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(g, L.T("No items yet - hover an item in Tarkov and press the hotkey."), _small,
                new Rectangle(S(16), 0, Width - S(32), Math.Min(Height, _rowH)), Muted, BackColor,
                F | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var s = SettingsStore.Current;
        int first = _offset / _rowH;
        for (int i = first; i < _rows.Count; i++)
        {
            int y = i * _rowH - _offset;
            if (y > Height) break;
            var (scan, item) = _rows[i];
            var back = BackColor;
            if (i == _hover)
            {
                back = Hover;
                using var path = Themes.RoundRect(new RectangleF(6, y + 3, Width - 12, _rowH - 6), 8);
                using var b = new SolidBrush(Hover);
                g.FillPath(b, path);
            }
            else if (i > first)
            {
                using var pen = new Pen(Line);
                g.DrawLine(pen, S(16), y, Width - S(16), y);
            }

            // picture
            int tileSize = S(36);
            var tile = new Rectangle(S(16), y + (_rowH - tileSize) / 2, tileSize, tileSize);
            using (var path = Themes.RoundRect(tile, 6))
            using (var b = new SolidBrush(Tile))
                g.FillPath(b, path);
            var img = ItemImages.TryGet(item.Id);
            if (img != null) g.DrawImage(img, new Rectangle(tile.X + 2, tile.Y + 2, tile.Width - 4, tile.Height - 4));
            else if (_loading.Add(item.Id)) _ = LoadPicture(item);

            // value on the right, in the tier color
            int right = Width - S(18) - (MaxOffset > 0 ? 6 : 0);
            long v = History.ValueOf(item, scan.Count);
            string value = v > 0 ? TextBuilder.Money(v) : "-";
            var tierColor = s.UseTiers && v > 0 ? Tiers.ColorOf(Tiers.TierOf(v, item.Slots, s), s) : TextColor;
            var vs = TextRenderer.MeasureText(g, value, _value, Size.Empty, F);
            TextRenderer.DrawText(g, value, _value, new Point(right - vs.Width, y + S(10)), tierColor, back, F);
            string where = BestPlace(item, s);
            var ws = TextRenderer.MeasureText(g, where, _small, Size.Empty, F);
            TextRenderer.DrawText(g, where, _small, new Point(right - ws.Width, y + S(31)), Muted, back, F);

            // name + when
            int x = tile.Right + S(12);
            int textW = right - x - Math.Max(vs.Width, ws.Width) - S(12);
            string sub = PriceState.Ago(scan.Time) + (scan.Count is int n && item.StackMax > 1 ? $"  ·  {L.T("Stack")} {n}" : "")
                         + (item.Slots > 1 ? $"  ·  {item.Slots} {L.T("slots")}" : "");
            TextRenderer.DrawText(g, item.Name, _name, new Rectangle(x, y + S(9), textW, S(20)), TextColor, back, F | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, sub, _small, new Rectangle(x, y + S(31), textW, S(18)), Muted, back, F | TextFormatFlags.EndEllipsis);
        }

        // thin scroll position bar when not everything fits
        if (MaxOffset > 0)
        {
            float total = _rows.Count * _rowH;
            float h = Math.Max(24, Height * Height / total);
            float top = (Height - h) * _offset / MaxOffset;
            using var path = Themes.RoundRect(new RectangleF(Width - 6, top + 2, 3, h - 4), 1.5f);
            using var b = new SolidBrush(Themes.Blend(BackColor, TextColor, 0.3));
            g.FillPath(b, path);
        }
    }

    // where it sells best right now: "Flea 41,000 ₽" or "Therapist 30,000 ₽"
    static string BestPlace(Item i, AppSettings s)
    {
        long flea = Flea.Net(i, s);
        var t = i.Traders.Count > 0 ? i.Traders[0] : null;
        if (flea > 0 && (t == null || flea >= t.Price)) return L.T("Flea") + " " + TextBuilder.Money(flea);
        if (t != null) return t.Vendor + " " + TextBuilder.Money(t.Price);
        return L.T("Not sellable");
    }

    async Task LoadPicture(Item item)
    {
        try { await ItemImages.GetAsync(item.Id, item.ImageUrl); } catch { }
        if (!IsDisposed) Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _name.Dispose(); _small.Dispose(); _value.Dispose(); _menu?.Dispose(); }
        base.Dispose(disposing);
    }
}
