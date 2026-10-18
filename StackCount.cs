using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Reads the stack count ("18") that the game draws at the bottom right of an item cell.
//  1. Find the hovered cell: its right and bottom edge are followed by the dark gap between the cells
//     (a column / row that is dark almost everywhere, right after a lighter one).
//  2. Cut out the bottom right corner of that cell, where the number is.
//  3. Stretch the contrast around the cell's background (the digits are only 5-6 px high at 768 px,
//     light grey with a dark outline), enlarge it smoothly and let the Windows OCR read it.
//  Without a cell edge the older way is used: near-white spots of digit size in the area of the cursor.
//  A number is only used when it is plausible (1 .. stack size); otherwise null (the price box then
//  shows the full stack, or the user types the number with the stack hotkey).
// ------------------------------------------------------------
static class StackCount
{
    const float CellAt768 = 38f;   // size of one inventory cell on a 768 px high screen (measured in game)

    // tag: only for the log ("before tooltip" = screenshot taken before the game drew its tooltip)
    public static async Task<int?> ReadAsync(Bitmap shot, Point cursor, float fs, Rectangle? tooltip, int max, string tag = "")
    {
        float cell = CellAt768 * fs;
        var area = Rectangle.Round(new RectangleF(cursor.X - 0.35f * cell, cursor.Y - 0.35f * cell, 1.75f * cell, 1.75f * cell));
        area.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        if (area.Width < 12 || area.Height < 12) return null;
        string name = tag.Length > 0 ? " (" + tag + ")" : "";
        bool debug = SettingsStore.Current.DebugLog;
        if (debug) SaveRaw(shot, area, cursor, tooltip, tag);

        var seen = new List<string>();
        int variant = 0;

        // ---- 1. + 2. corner of the hovered cell ----
        var lum = Luminance(shot, area, tooltip, out var sat);
        var zones = CellCorners(lum, area, cursor, cell);
        Rectangle? numberZone = null;
        foreach (var zone in zones)
        {
            int zbg = Background(lum, area.Width, zone);
            // known look: the game draws the same number in the same way, so a picture seen before
            // (built in, or learned) is recognized exactly; otherwise digit by digit
            var crop = NumberCrop(lum, sat, area.Width, zone, zbg, cell);
            if (crop == null) continue;   // nothing that looks like a number in this corner: try the next edge
            if (numberZone == null)
            {
                numberZone = zone;
                _last = (crop, DateTime.UtcNow);
            }
            if (Known(crop, max) is int known)
            {
                _last = (crop, DateTime.UtcNow);
                Log.Write($"Stack count{name}: known picture {crop.W}x{crop.H} -> {known}");
                return known;
            }
            if (ByDigits(crop, max) is { } dr)
            {
                var (digits, sure, scores) = dr;
                _last = (crop, DateTime.UtcNow);
                Log.Write($"Stack count{name}: digits [{scores}] -> {digits}");
                if (sure) Remember(digits, crop, "read by its digits");   // very clear: the whole picture is kept too
                return digits;
            }
        }
        if (numberZone is Rectangle nz)
        {
            var zone = nz;
            int bg = Background(lum, area.Width, zone);
            // only the number itself: the box around the light pixels of the corner (2 px margin)
            var z = Tight(lum, sat, area.Width, zone, bg + 25) ?? zone;
            int scale = Math.Clamp((int)Math.Round(10 / fs), 4, 12);
            const int pad = 30;
            foreach (var (lo, hi) in new[] { (bg + 3, bg + 30), (bg - 2, bg + 50), (bg + 10, bg + 70) })
            {
                variant++;
                // the Windows OCR is bad at very short text: the number is put in 3 times ("25   25   25")
                using var bmp = Stretch(lum, sat, area.Width, z, lo, hi, scale, pad, copies: 3);
                if (debug) SaveDebug(bmp, (tag.Length > 0 ? "clean-" : "") + $"c{variant}");
                var lines = await Ocr.ReadLinesAsync(bmp, splitGaps: false);
                string text = string.Join(" ", lines.Select(x => x.Text));
                seen.Add(text);
                // majority of the copies; one clear reading is enough when the others are empty
                var votes = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Select(Digits).Where(t => t != null && int.TryParse(t, out int v) && v >= 1 && v <= max)
                                .GroupBy(t => int.Parse(t!)).OrderByDescending(gr => gr.Count()).ToList();
                if (votes.Count == 0) continue;
                int best = votes[0].Key;
                if (votes[0].Count() < 2 && votes.Count > 1) continue;
                Log.Write($"Stack count{name}: cell corner {z.Width}x{z.Height} [{string.Join(" / ", seen)}] -> {best}");
                return best;
            }
        }
        else seen.Add(zones.Count == 0 ? "no cell edge" : "no number in the corner");

        // ---- fallback: near-white spots of digit size around the cursor ----
        int scale2 = Math.Clamp((int)Math.Round(6 / fs), 3, 8);
        const int pad2 = 16;
        foreach (var (minBright, maxTint) in new[] { (165, 40), (140, 55), (115, 70) })
        {
            variant++;
            using var bmp = WhiteText(shot, area, scale2, pad2, tooltip, cell, minBright, maxTint);
            if (bmp == null) { seen.Add("-"); continue; }
            if (debug) SaveDebug(bmp, (tag.Length > 0 ? "clean-" : "") + $"v{variant}");

            var lines = await Ocr.ReadLinesAsync(bmp, splitGaps: true);
            seen.Add(string.Join(" | ", lines.Select(x => x.Text)));
            int? best = null;
            double bestDist = double.MaxValue;
            foreach (var l in lines)
            {
                if (Digits(l.Text) is not string t || !int.TryParse(t, out int n) || n < 1 || n > max) continue;
                double x = area.X + (l.X - pad2) / scale2, y = area.Y + (l.Y - pad2) / scale2;
                if (x < cursor.X - 0.25 * cell || y < cursor.Y - 0.25 * cell) continue;   // belongs to a cell above / left
                double d = Math.Sqrt((x - cursor.X) * (x - cursor.X) + (y - cursor.Y) * (y - cursor.Y));
                if (d < bestDist) { bestDist = d; best = n; }
            }
            if (best != null)
            {
                Log.Write($"Stack count{name}: [{string.Join(" / ", seen)}] -> {best}");
                return best;
            }
        }
        Log.Write($"Stack count{name}: [{string.Join(" / ", seen)}] -> not sure");
        return null;
    }

    // ------------------------------------------------------------
    //  Durability of armor ("179/190" at the bottom right of the item): the real value of the piece
    //  in the stash, which the price data cannot know (repairs lower the maximum, plates change it).
    //  The text is longer than a stack count, so the Windows OCR reads it fairly well; a reading is
    //  only used when it is plausible (current <= max <= max of a new item).
    // ------------------------------------------------------------
    public static Task<(int Cur, int Max)?> ReadDurabilityAsync(Bitmap shot, Point cursor, float fs, Rectangle? tooltip, int newMax) =>
        // armor can be up to 3 x 4 cells: look up to about 3.5 cells right / down of the mouse;
        // also 1.6 cells to the left, so the text is not cut off when the mouse is near the right edge of the item
        ReadFractionAsync(shot, cursor, fs, tooltip, newMax, "Durability", red: false,
                          left: 1.6f, width: 5.25f, height: 4f, reach: 3.6f, zoneWidth: 1.9f, exactMax: false);

    // uses left of a key ("37/40" in red at the bottom right of the 1 x 1 cell); the maximum is fixed for each key
    public static Task<(int Cur, int Max)?> ReadKeyUsesAsync(Bitmap shot, Point cursor, float fs, Rectangle? tooltip, int maxUses) =>
        ReadFractionAsync(shot, cursor, fs, tooltip, maxUses, "Key uses", red: true,
                          left: 1.05f, width: 2.4f, height: 1.75f, reach: 1.25f, zoneWidth: 0.95f, exactMax: true);

    // red: the text is red (keys) - its red channel is read instead of the brightness
    static async Task<(int Cur, int Max)?> ReadFractionAsync(Bitmap shot, Point cursor, float fs, Rectangle? tooltip, int newMax,
        string label, bool red, float left, float width, float height, float reach, float zoneWidth, bool exactMax)
    {
        float cell = CellAt768 * fs;
        var area = Rectangle.Round(new RectangleF(cursor.X - left * cell, cursor.Y - 0.35f * cell, width * cell, height * cell));
        area.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        if (area.Width < 20 || area.Height < 20) return null;
        var lum = Luminance(shot, area, tooltip, out var sat, red);
        var zones = CellCorners(lum, area, cursor, cell, reach: reach, zoneWidth: zoneWidth);
        bool Plausible(int cur, int mx) =>
            mx > 0 && cur <= mx && (newMax <= 0 || (exactMax ? mx == newMax : mx <= newMax * 1.6 + 2 && mx >= newMax * 0.3));
        var seen = new List<string>();
        int scale = Math.Clamp((int)Math.Round(8 / fs), 3, 10);
        bool debugSaved = false;
        // first by the look of each character in every corner (the game font is tiny, text recognition often fails on it)
        foreach (var zone in zones.Take(8))
        {
            int bg = Background(lum, area.Width, zone);
            if (TextLine(lum, sat, area.Width, zone, bg + 40, (int)Math.Round(6 * fs)) is not Rectangle tz || tz.Width < 8) continue;
            if (tz.Left <= Math.Max(0, zone.Left)) continue;   // the text reaches the edge of the corner: cut off ("9/190" instead of "179/190")
            if (SettingsStore.Current.DebugLog && !debugSaved) { SaveRaw(shot, area, cursor, tooltip, label.ToLowerInvariant()); debugSaved = true; }
            if (ByGlyphs(lum, sat, area.Width, tz, bg, fs) is { } g)
            {
                seen.Add($"look {g.Text} ({g.Scores})");
                var gm = System.Text.RegularExpressions.Regex.Match(g.Text, @"^(\d{1,3})/(\d{1,3})$");
                if (gm.Success)
                {
                    int cur = int.Parse(gm.Groups[1].Value), mx = int.Parse(gm.Groups[2].Value);
                    if (Plausible(cur, mx))
                    {
                        Log.Write($"{label}: [{string.Join(" / ", seen)}] -> {cur}/{mx}");
                        return (cur, mx);
                    }
                }
            }
        }
        // then by text recognition
        foreach (var zone in zones.Take(4))
        {
            int bg = Background(lum, area.Width, zone);
            var z = TextLine(lum, sat, area.Width, zone, bg + 40, (int)Math.Round(6 * fs));   // the text is clearly brighter than the picture's edges
            if (z is not Rectangle tz || tz.Width < 8 || tz.Left <= Math.Max(0, zone.Left)) continue;
            foreach (var (lo, hi) in new[] { (bg + 3, bg + 40), (bg - 2, bg + 60) })
            {
                using var bmp = Stretch(lum, sat, area.Width, tz, lo, hi, scale, 30, copies: 2);
                var lines = await Ocr.ReadLinesAsync(bmp, splitGaps: false);
                string text = string.Join(" ", lines.Select(x => x.Text));
                seen.Add(text);
                var votes = new Dictionary<(int, int), int>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Fix(text), @"(\d{1,3})/(\d{1,3})"))
                {
                    int cur = int.Parse(m.Groups[1].Value), mx = int.Parse(m.Groups[2].Value);
                    // as many characters as fit the width of the text (a digit is 4 px + 1 px gap at 768 px):
                    // "IT/tge" read as 17/190 is one digit short of "179/190" and is not used
                    string all = m.Groups[1].Value + "/" + m.Groups[2].Value;
                    double expected = (all.Sum(c => c == '1' ? 3 : 4) + all.Length - 1) * fs;   // '1' is 3 px wide, others 4, 1 px gaps
                    if (Math.Abs(expected - (tz.Width - 4)) > 2.5 * fs) continue;
                    bool plausible = Plausible(cur, mx);
                    if (plausible) votes[(cur, mx)] = votes.GetValueOrDefault((cur, mx)) + 1;
                }
                if (votes.Count > 0)
                {
                    var best = votes.OrderByDescending(v => v.Value).First().Key;
                    Log.Write($"{label}: [{string.Join(" / ", seen)}] -> {best.Item1}/{best.Item2}");
                    return best;
                }
            }
        }
        Log.Write($"{label}: [{string.Join(" / ", seen)}] -> not sure{(zones.Count == 0 ? " (no item edge)" : "")}");
        return null;
    }

    // characters of the durability text ("179/190"), seen in game at 768 px: char|width|height|values
    static readonly string[] DurabilityGlyphs =
    {
        "1|3|6|008900687c00007c00007c00007c0063877f",
        "1|3|6|0089005d7100007100007000007000538681",
        "7|4|6|52878686000000520000004f00007c0000002c0000002c00",
        "/|4|6|1b1f0002000300870000430100004d000083000037080000",
        "0|4|6|008787834a00007c4a00630b4a0141034983000300858286",
        "9|4|6|008686796500000e650000160081817f000000164c828181",
        "9|4|6|3c8787175a0000825b0000822d82828c000000817f81822b",
    };
    static List<(char C, Crop Crop)>? _glyphs;

    static List<(char C, Crop Crop)> GlyphList()
    {
        if (_glyphs != null) return _glyphs;
        _glyphs = DigitList().Select(d => ((char)('0' + d.D), d.Crop)).ToList();
        foreach (var line in DurabilityGlyphs)
        {
            var p = line.Split('|');
            _glyphs.Add((p[0][0], new Crop(int.Parse(p[1]), int.Parse(p[2]), Convert.FromHexString(p[3]))));
        }
        return _glyphs;
    }

    // reads a short text line character by character; characters can touch each other ("17" has no gap),
    // so every run of ink is cut into the pieces that look most like known characters
    static (string Text, string Scores)? ByGlyphs(int[] lum, int[] sat, int w, Rectangle tz, int bg, float fs)
    {
        int y1 = -1;
        for (int y = tz.Bottom - 1; y >= tz.Top && y1 < 0; y--)
            for (int x = tz.Left; x < tz.Right; x++)
            {
                int p = y * w + x;
                if (lum[p] >= bg + 40 && sat[p] <= 60) { y1 = y; break; }
            }
        if (y1 < 0) return null;
        int rows = Math.Max(4, (int)Math.Round(6 * fs)), y0 = Math.Max(tz.Top, y1 - rows + 1);
        int cw = tz.Width, ch = y1 - y0 + 1;
        var v = new double[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int p = (y0 + y) * w + tz.Left + x;
                v[y * cw + x] = lum[p] < 0 || sat[p] > 60 ? 0 : Math.Clamp(lum[p] - bg - 12, 0, 255);
            }
        // other screen sizes: bring the text to the 768 px size of the known characters
        if (Math.Abs(fs - 1) > 0.1f)
        {
            int nw = Math.Max(1, (int)Math.Round(cw / fs)), nh = 6;
            var r = new double[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    double sx = Math.Clamp((x + 0.5) * cw / nw - 0.5, 0, cw - 1), sy = Math.Clamp((y + 0.5) * ch / nh - 0.5, 0, ch - 1);
                    int ix = (int)sx, iy = (int)sy, jx = Math.Min(ix + 1, cw - 1), jy = Math.Min(iy + 1, ch - 1);
                    double fx = sx - ix, fy = sy - iy;
                    r[y * nw + x] = (v[iy * cw + ix] * (1 - fx) + v[iy * cw + jx] * fx) * (1 - fy)
                                  + (v[jy * cw + ix] * (1 - fx) + v[jy * cw + jx] * fx) * fy;
                }
            v = r; cw = nw; ch = nh;
        }
        double peak = v.Max();
        if (peak <= 0) return null;
        bool Col(int x) { for (int y = 0; y < ch; y++) if (v[y * cw + x] >= 0.45 * peak) return true; return false; }
        Crop Piece(int a, int b)
        {
            var bytes = new byte[(b - a) * ch];
            for (int y = 0; y < ch; y++)
                for (int x = a; x < b; x++) bytes[y * (b - a) + x - a] = (byte)Math.Round(v[y * cw + x]);
            return new Crop(b - a, ch, bytes);
        }
        var glyphs = GlyphList();
        (char C, double S1, double S2) Best(Crop c)
        {
            var sc = new Dictionary<char, double>();
            foreach (var (ch2, k) in glyphs) sc[ch2] = Math.Max(sc.GetValueOrDefault(ch2), Aligned(c, k));
            var o = sc.OrderByDescending(kv => kv.Value).ToList();
            return (o[0].Key, o[0].Value, o.Count > 1 ? o[1].Value : 0);
        }
        var text = new System.Text.StringBuilder();
        var scores = new List<string>();
        int start = -1;
        for (int x = 0; x <= cw; x++)
        {
            bool ink = x < cw && Col(x);
            if (ink && start < 0) { start = x; continue; }
            if (ink || start < 0) continue;
            int n = x - start;
            // best cut of this run: sum of (score - 0.9) over its pieces, every piece must look like a character
            var dp = new (double T, string S, List<string> L)?[n + 1];
            dp[0] = (0, "", new List<string>());
            for (int i = 1; i <= n; i++)
                for (int pw = 2; pw <= 6 && pw <= i; pw++)
                {
                    if (dp[i - pw] is not { } prev) continue;
                    var (c, s1, s2) = Best(Piece(start + i - pw, start + i));
                    if (s1 < 0.75 || s1 - s2 < 0.04) continue;
                    double t = prev.T + s1 - 0.9;   // weak pieces cost: a wide "4" is not cut into "4" + "1"
                    if (dp[i] is not { } cur || t > cur.T)
                        dp[i] = (t, prev.S + c, new List<string>(prev.L) { $"{c} {s1:0.00}/{s2:0.00}" });
                }
            if (dp[n] is not { } res) return null;   // a character that is not known
            text.Append(res.S);
            scores.AddRange(res.L);
            start = -1;
        }
        return text.Length == 0 ? null : (text.ToString(), string.Join(", ", scores));
    }

    // the lowest line of light text in the corner (the item picture above it is left out)
    static Rectangle? TextLine(int[] lum, int[] sat, int w, Rectangle z, int bright, int textH)
    {
        bool Ink(int x, int y) { int p = y * w + x; return lum[p] >= bright && sat[p] <= 60; }
        int y1 = -1;
        for (int y = z.Bottom - 1; y >= z.Top && y1 < 0; y--)
            for (int x = z.Left; x < z.Right; x++) if (Ink(x, y)) { y1 = y; break; }
        if (y1 < 0) return null;
        int y0 = y1, gap = 0;
        for (int y = y1 - 1; y >= z.Top; y--)
        {
            bool any = false;
            for (int x = z.Left; x < z.Right && !any; x++) any = Ink(x, y);
            if (any) { y0 = y; gap = 0; }
            else if (++gap > 1) break;
        }
        y0 = Math.Max(y0, y1 - textH + 1);   // only the text line itself (the picture above can touch it)
        bool ColInk(int x) { for (int y = y0; y <= y1; y++) if (Ink(x, y)) return true; return false; }
        int x1 = -1;
        for (int x = z.Right - 1; x >= z.Left && x1 < 0; x--) if (ColInk(x)) x1 = x;
        if (x1 < 0) return null;
        // the text is right-aligned: only the group of characters at the right (a wide gap ends it,
        // e.g. before the item picture that reaches down next to the text)
        int x0 = x1, maxGap = Math.Max(3, (int)Math.Round(3 * textH / 6.0)), gapRun = 0;
        for (int x = x1 - 1; x >= z.Left; x--)
        {
            if (ColInk(x)) { x0 = x; gapRun = 0; }
            else if (++gapRun > maxGap) break;
        }
        var r = Rectangle.FromLTRB(x0 - 2, y0 - 2, x1 + 3, y1 + 3);
        r.Intersect(z);
        return r;
    }

    // OCR look-alikes inside "179/190" (seen in game: "179/tge", "IT/tge"): Ø O o D e -> 0, l I | i ! t -> 1, T -> 7,
    // S -> 5, B -> 8, Z -> 2, g -> 9; separators -> "/"
    static string Fix(string t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in t)
            sb.Append(ch switch
            {
                'Ø' or 'ø' or 'O' or 'o' or 'D' or 'Q' => '0',
                'l' or 'I' or '|' or 'i' or '!' => '1',
                'S' or 's' => '5',
                'B' => '8',
                'Z' or 'z' => '2',
                'g' or 'q' => '9',
                't' => '1',
                'T' => '7',
                'e' => '0',   // the slashed zero
                _ => ch
            });
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s*[/\\]\s*", "/");
    }

    // ---- numbers by their look ----
    sealed record Crop(int W, int H, byte[] V);
    static (Crop Crop, DateTime Time)? _last;   // number picture of the last reading (learned when the user types the number)
    static List<(int N, Crop Crop)>? _known;
    static string KnownPath => Path.Combine(SettingsStore.Dir, "stack-numbers.txt");

    // seen in game at 768 px (two looks of 25 and 49: the cells are not exactly on whole pixels).
    // Their digits are also used one by one, so e.g. 52 or 940 can be read without ever having been seen.
    static readonly string[] Seeds =
    {
        "25|9|6|1549483300004849490000000b00000000000000001a00000d0d0a00004216000000001900580000000000000046404041000242415a",
        "25|9|6|3148480000454848380000005a00550000000000005d00550b0b0000005a000000000045342200000000000035524240400055404131",
        "4|4|6|0000004600005755004400552c0100555454546200000055",
        "41|8|5|0100004800005d2400005b0500000024005e0005000000241c565657570000240000000300001228",
        "80|9|6|001b1b000000191b195c00005d001902005655000055001900010d3c3f3f3b0017005a004c00005200172b00005911125900023e102f",
        "20|9|6|011b1b0000001a1b060000005b004900005e000000510048002b080000440300470036070256000000475600074b12121200251e1056",
        "49|9|5|00005820005705055a0048001f005d00002f2c01001f0055050531342f2f37000000002f0000001f0016000053",
        "49|9|5|01003156002e080643001715560041000001032a005600260a060d563030590000000003000000560005000028",
        // from item cells that were not hovered (the digits are drawn the same, only the background differs)
        "80|9|6|00222200000020222063000064002009005d5c00005c002000081443464642001e00610053000059001e320000601819600009451736",
        "20|9|6|08222200000021220d03000062005000006500000058004f00320f00004b0a004e003d0e095d0000004e5d000e52191919002c25175d",
        "41|8|5|0000004e000063280000610b000000280064000b00000028225c5c5d5d000028000000090000182c",
        "60|10|6|0022221f00000d2322004100000000002d00321941000000000010003e19411b1c580000105f001a4000000204001d130019211f1854000064181806",
        "80|9|6|0021210000001e221f62000063001e07005c5b00005c001e00081542454541001f00610052000057001f3300005f16175e0009451735",
        "80|9|6|01212000000020210c40000058004c0000642a000058004d00300c5e434219004d003a0c2100005c004c5b000c5f15163d002923155a",
        "44|10|5|000000250000000002000000082400000000470000003a0c0000005a00000057000c00005f0000002555555554005555551a",
        "25|9|6|32494900004649492d0000005b00560000000000005e00560c0c0000005b000000000039352300000000000029534341410056414225",
        "14|7|6|0033000000000022230000000045001b0000005900001b00005d0000001a000053535301484100000000",
        "4|4|6|0000004400005753004400532c0100535454546000000053",
        "25|9|6|001c1c0000131b1b1a05000050002c00000000000033002c00000000003419000f151557005c000000000000165b1211110019111156",
        "49|9|6|01000040000048493300005d5000450000080059005000460000054b00005000005959575454546300000000050000004f003041415c",
        "25|9|5|1b05055800570505040000005c005700000000002c13003e3e3e2e005c000000000000393b0000000015000045",
        "20|10|5|1f05054100004105211700000000000001004c210000005b0000014a0021003424000000021b00203f00000000005f00001d",
        "51|7|6|1b1a1a05000000000000000043000100000000000015153200000000000000150000001610300000130d",
        "14|8|6|0057000001003000474e000000151800004e000000290f00004e000043000f00004e002642424518365b4c0000000f00",
        "20|10|6|154e4e3e00003c4e4e000000000400000000522100000017000000001321000042140000005c00210058000000002c0000213e48484800005d474b00",
        "20|9|6|4647470000094747000000002b00360019480000002b003400494500193c0000344600455900000000341c004540404027004c40401d",
        "80|9|6|0e1b1b0000001b1a0017000023005f00005a01000023005c0055325b3f4e00005b040d320000002a005b59003243102111004d111158",
        "49|10|6|0002002500001556560000000c1c00003900004500003403000038000045005100030000205e5e4d1a4242424200000000450000000300005c525217",
    };

    static List<(int N, Crop Crop)> KnownList()
    {
        if (_known != null) return _known;
        _known = new();
        var lines = new List<string>(Seeds);
        try { if (File.Exists(KnownPath)) lines.AddRange(File.ReadAllLines(KnownPath)); }
        catch (Exception ex) { Log.Write("Stack numbers: " + ex.Message); }
        foreach (var line in lines)
        {
            var p = line.Split('|');
            if (p.Length != 4 || !int.TryParse(p[0], out int n) || !int.TryParse(p[1], out int w) || !int.TryParse(p[2], out int h)) continue;
            if (p[3].Length != w * h * 2) continue;
            try { _known.Add((n, new Crop(w, h, Convert.FromHexString(p[3])))); } catch { }
        }
        return _known;
    }

    // the number this picture shows, when it looks (almost) exactly like a known one
    static int? Known(Crop c, int max)
    {
        int? best = null;
        double bestScore = 0, other = 0;
        var parts = Split(c);
        foreach (var (n, k) in KnownList())
        {
            if (Math.Abs(k.W - c.W) > 1 || Math.Abs(k.H - c.H) > 1 || n > max) continue;
            double s = Aligned(c, k);
            // every digit must fit as well: "20" looked 0.85 like "25" as a whole, because the bright 2 counts most
            var kp = Split(k);
            if (kp.Count == parts.Count)
                for (int i = 0; i < parts.Count; i++) s = Math.Min(s, Aligned(parts[i], kp[i]));
            if (s > bestScore)
            {
                if (best != n) other = Math.Max(other, bestScore);
                bestScore = s;
                best = n;
            }
            else if (n != best) other = Math.Max(other, s);
        }
        // the same number in another position of the stash still looks about 0.75-0.8 alike, different
        // numbers at most about 0.7 - so a clear lead over every other number is required as well
        return best != null && bestScore >= 0.78 && bestScore - other >= 0.15 ? best : null;
    }

    // the user typed the number of the stack (stack hotkey): remember how the game draws it
    public static void Learn(int n)
    {
        if (_last is not { } last || DateTime.UtcNow - last.Time > TimeSpan.FromSeconds(60)) return;
        _last = null;
        Remember(n, last.Crop, "typed");
    }

    static void Remember(int n, Crop crop, string how)
    {
        var list = KnownList();
        if (list.Any(k => k.N == n && Aligned(k.Crop, crop) >= 0.97)) return;
        list.Add((n, crop));
        _digits = null;   // the digits of the new number are used from now on
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            File.AppendAllText(KnownPath, $"{n}|{crop.W}|{crop.H}|{Convert.ToHexString(crop.V).ToLowerInvariant()}{Environment.NewLine}");
            Log.Write($"Stack count: learned how {n} looks ({crop.W}x{crop.H}, {how})");
        }
        catch (Exception ex) { Log.Write("Stack numbers: " + ex.Message); }
    }

    // ---- single digits ----
    static List<(int D, Crop Crop)>? _digits;

    // the digits are separated by a dark column (all values 0 = not brighter than the background)
    static List<Crop> Split(Crop c)
    {
        var parts = new List<Crop>();
        int start = -1;
        for (int x = 0; x <= c.W; x++)
        {
            bool ink = false;
            if (x < c.W)
                for (int y = 0; y < c.H && !ink; y++) ink = c.V[y * c.W + x] > 0;
            if (ink && start < 0) start = x;
            else if (!ink && start >= 0)
            {
                int w = x - start;
                var v = new byte[w * c.H];
                for (int y = 0; y < c.H; y++) Array.Copy(c.V, y * c.W + start, v, y * w, w);
                parts.Add(new Crop(w, c.H, v));
                start = -1;
            }
        }
        return parts;
    }

    // digit pictures out of all known numbers (built in + learned)
    static List<(int D, Crop Crop)> DigitList()
    {
        if (_digits != null) return _digits;
        _digits = new();
        foreach (var (n, crop) in KnownList())
        {
            string text = n.ToString();
            var parts = Split(crop);
            if (parts.Count != text.Length) continue;
            for (int i = 0; i < parts.Count; i++) _digits.Add((text[i] - '0', parts[i]));
        }
        return _digits;
    }

    // reads the number digit by digit; null when one digit is not clear (then nothing is guessed)
    static (int N, bool Sure, string Scores)? ByDigits(Crop c, int max)
    {
        var parts = Split(c);
        if (parts.Count is < 1 or > 4) return null;
        var digits = DigitList();
        int n = 0;
        double worst = 1;
        var scores = new List<string>();
        foreach (var part in parts)
        {
            var best = new double[10];
            foreach (var (d, k) in digits) best[d] = Math.Max(best[d], Aligned(part, k));
            int d1 = Array.IndexOf(best, best.Max());
            double s1 = best[d1], s2 = best.Where((_, i) => i != d1).Max();
            scores.Add($"{d1} {s1:0.00}/{s2:0.00}");
            // not clear enough: some digits look alike in this tiny font (5/6, 8/9), so the best digit must clearly lead
            if (s1 < 0.8 || s1 - s2 < 0.15) return null;
            n = n * 10 + d1;
            worst = Math.Min(worst, s1);
        }
        if (n < 1 || n > max) return null;
        return (n, worst >= 0.92, string.Join(", ", scores));
    }

    // the same number can lose its faint top row or one column: both pictures are put on a common canvas,
    // aligned at the bottom right (the number is right-aligned) and shifted by up to one pixel; best match counts
    static double Aligned(Crop a, Crop b)
    {
        if (Math.Abs(a.W - b.W) > 1 || Math.Abs(a.H - b.H) > 1) return 0;
        int w = Math.Max(a.W, b.W) + 2, h = Math.Max(a.H, b.H) + 2;
        var ca = new byte[w * h];
        Place(ca, w, a, w - 1 - a.W, h - 1 - a.H);
        double best = 0;
        var cb = new byte[w * h];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Array.Clear(cb);
                Place(cb, w, b, w - 1 - b.W + dx, h - 1 - b.H + dy);
                best = Math.Max(best, Similarity(ca, cb));
            }
        return best;
    }

    static void Place(byte[] canvas, int w, Crop c, int ox, int oy)
    {
        int h = canvas.Length / w;
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
            {
                int px = ox + x, py = oy + y;
                if (px >= 0 && py >= 0 && px < w && py < h) canvas[py * w + px] = c.V[y * c.W + x];
            }
    }

    // normalized cross correlation (1 = same picture, brightness does not matter)
    static double Similarity(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double ma = a.Average(x => (double)x), mb = b.Average(x => (double)x), num = 0, da = 0, db = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double x = a[i] - ma, y = b[i] - mb;
            num += x * y; da += x * x; db += y * y;
        }
        return da <= 0 || db <= 0 ? 0 : num / Math.Sqrt(da * db);
    }

    // the number in the corner: light pixels (brighter than the background) of the lowest text line
    static Crop? NumberCrop(int[] lum, int[] sat, int w, Rectangle z, int bg, float cell)
    {
        bool Ink(int x, int y) { int p = y * w + x; return lum[p] >= 0 && sat[p] <= 60 && lum[p] > bg + 12; }
        var rows = new List<int>();
        for (int y = z.Top; y < z.Bottom; y++)
            for (int x = z.Left; x < z.Right; x++)
                if (Ink(x, y)) { rows.Add(y); break; }
        if (rows.Count == 0) return null;
        int y1 = rows[^1], y0 = y1;
        for (int i = rows.Count - 2; i >= 0 && y0 - rows[i] <= 2; i--) y0 = rows[i];
        int x0 = int.MaxValue, x1 = -1;
        for (int x = z.Left; x < z.Right; x++)
            for (int y = y0; y <= y1; y++)
                if (Ink(x, y)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); break; }
        if (x1 < 0) return null;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        // a number: digit high, right-aligned near the border, at the bottom of the cell
        int near = Math.Max(3, (int)(0.2f * cell));
        if (ch < 3 || ch > 0.3f * cell + 2 || cw < 2 || cw > 0.6f * cell) return null;
        if (x1 < z.Right - 1 - near || y1 < z.Bottom - 1 - near) return null;
        if (x1 > z.Right - 2 || y1 > z.Bottom - 2) return null;   // touches the edge of the corner: cut off, wrong edge
        var v = new byte[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int p = (y0 + y) * w + x0 + x;
                v[y * cw + x] = lum[p] < 0 || sat[p] > 60 ? (byte)0 : (byte)Math.Clamp(lum[p] - bg, 0, 255);
            }
        return new Crop(cw, ch, v);
    }

    // brightness (0..255) and color strength of every pixel of the area; -1 inside the tooltip
    static int[] Luminance(Bitmap shot, Rectangle area, Rectangle? tooltip, out int[] sat, bool red = false)
    {
        int w = area.Width, h = area.Height;
        var lum = new int[w * h];
        sat = new int[w * h];
        var src = shot.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, row.Length);
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    int b = row[i], g = row[i + 1], r = row[i + 2];
                    bool inTooltip = tooltip is Rectangle t && t.Contains(area.X + x, area.Y + y);
                    lum[y * w + x] = inTooltip ? -1 : red ? r : (r + g + b) / 3;
                    sat[y * w + x] = red ? 0 : Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                }
            }
        }
        finally
        {
            shot.UnlockBits(src);
        }
        return lum;
    }

    // bottom right corner of the hovered cell (in area coordinates), or null when no cell edge is found
    static List<Rectangle> CellCorners(int[] lum, Rectangle area, Point cursor, float cell, float reach = 1.25f, float zoneWidth = 0.55f)
    {
        int w = area.Width, h = area.Height;
        int cx = cursor.X - area.X, cy = cursor.Y - area.Y;

        // share of dark pixels in a column / row (tooltip pixels do not count)
        double Col(int x)
        {
            int dark = 0, n = 0;
            for (int y = 0; y < h; y++) { int v = lum[y * w + x]; if (v < 0) continue; n++; if (v < 70) dark++; }
            return n < h / 2 ? 0 : (double)dark / n;
        }
        double Row(int y)
        {
            int dark = 0, n = 0;
            for (int x = 0; x < w; x++) { int v = lum[y * w + x]; if (v < 0) continue; n++; if (v < 70) dark++; }
            return n < w / 2 ? 0 : (double)dark / n;
        }

        // brightness along a short piece of a column / row next to the cursor (inside the hovered cell)
        int span = (int)(0.3f * cell);
        double LocalCol(int x)
        {
            int sum = 0, n = 0;
            for (int y = Math.Max(0, cy - span); y < Math.Min(h, cy + span); y++) { int v = lum[y * w + x]; if (v < 0) continue; sum += v; n++; }
            return n == 0 ? 0 : (double)sum / n;
        }
        double LocalRow(int y)
        {
            int sum = 0, n = 0;
            for (int x = Math.Max(0, cx - span); x < Math.Min(w, cx + span); x++) { int v = lum[y * w + x]; if (v < 0) continue; sum += v; n++; }
            return n == 0 ? 0 : (double)sum / n;
        }

        // Possible right / lower edges of the hovered cell, nearest first. Several ways are used, and when
        // one of them is fooled (item picture, scroll bar, a neighbour) another one still finds the edge.
        var xs = new List<int>();
        var ys = new List<int>();

        // 1. the gap after the cell: dark almost everywhere, clearly darker than the line(s) before it
        //    (the border fades over 1-2 pixels, so the two lines before the gap are looked at)
        int found = 0;
        for (int x = Math.Max(2, cx + 1); x < w && found < 2; x++)
            if (Col(x) >= 0.9 && Math.Min(Col(x - 1), Col(x - 2)) <= Col(x) - 0.3 &&
                Math.Max(LocalCol(x - 1), LocalCol(x - 2)) > LocalCol(x) + 25) { xs.Add(x); found++; }
        found = 0;
        for (int y = Math.Max(2, cy + 1); y < h && found < 2; y++)
            if (Row(y) >= 0.9 && Math.Min(Row(y - 1), Row(y - 2)) <= Row(y) - 0.3 &&
                Math.Max(LocalRow(y - 1), LocalRow(y - 2)) > LocalRow(y) + 25) { ys.Add(y); found++; }

        // 2. the cell's own border line next to the cursor - an even, medium bright line
        //    with a clearly darker line after it (works also when a neighbour is bright, e.g. the scroll bar)
        List<int> RowPart(int y)
        {
            var v = new List<int>();
            for (int x = Math.Max(0, cx - span); x < Math.Min(w, cx + span); x++) if (lum[y * w + x] >= 0) v.Add(lum[y * w + x]);
            return v;
        }
        List<int> ColPart(int x)
        {
            var v = new List<int>();
            for (int y = Math.Max(0, cy - span); y < Math.Min(h, cy + span); y++) if (lum[y * w + x] >= 0) v.Add(lum[y * w + x]);
            return v;
        }
        static double Median(List<int> v) { v.Sort(); return v.Count == 0 ? 0 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2.0; }
        static bool IsLine(List<int> line, List<int> next)
        {
            if (line.Count < 8 || next.Count < 8) return false;
            double m = Median(line);
            if (m < 55 || m > 140) return false;
            int even = line.Count(v => Math.Abs(v - m) <= 8);
            return even >= 0.7 * line.Count && Median(next) <= m - 25;
        }
        for (int x = cx + 1; x < w - 1; x++)
            if (IsLine(ColPart(x), ColPart(x + 1))) { xs.Add(x + 1); break; }
        for (int y = cy + 1; y < h - 1; y++)
            if (IsLine(RowPart(y), RowPart(y + 1))) { ys.Add(y + 1); break; }

        // 2b. the right edge also in a band just above the lower edge (the text row): there the border is
        //     not hidden by the item picture, also for big items where the mouse is far from the corner
        foreach (int gy0 in ys.Select(y => Math.Min(y, h)).Where(y => y > cy && y - cy <= reach * cell).Distinct().OrderBy(y => y).Take(2))
        {
            List<int> Band(int x)
            {
                var v = new List<int>();
                for (int y = Math.Max(0, gy0 - 14); y < Math.Max(0, gy0 - 2); y++) if (lum[y * w + x] >= 0) v.Add(lum[y * w + x]);
                return v;
            }
            for (int x = cx + 1; x < w - 1; x++)
                if (IsLine(Band(x), Band(x + 1))) { xs.Add(x + 1); break; }
        }

        // 3. the gap before the cell (left / upper edge) + one cell
        int size = (int)Math.Round(cell);
        for (int x = Math.Min(w - 2, cx - 1); x >= 0; x--)
            if (Col(x) >= 0.9 && Col(x + 1) <= Col(x) - 0.3 && LocalCol(x + 1) > LocalCol(x) + 25) { xs.Add(x + size + 1); break; }
        for (int y = Math.Min(h - 2, cy - 1); y >= 0; y--)
            if (Row(y) >= 0.9 && Row(y + 1) <= Row(y) - 0.3 && LocalRow(y + 1) > LocalRow(y) + 25) { ys.Add(y + size + 1); break; }

        bool Plausible(int g, int c) => g > c && g - c <= reach * cell;
        var gxs = xs.Select(x => Math.Min(x, w)).Where(x => Plausible(x, cx)).Distinct().OrderBy(x => x).ToList();
        var gys = ys.Select(y => Math.Min(y, h)).Where(y => Plausible(y, cy)).Distinct().OrderBy(y => y).ToList();

        var zones = new List<(int Rank, Rectangle Zone)>();
        for (int i = 0; i < gxs.Count; i++)
            for (int j = 0; j < gys.Count; j++)
            {
                int right = gxs[i] - 1, bottom = gys[j] - 1;   // the cell border line
                var z = Rectangle.FromLTRB((int)(right - zoneWidth * cell), (int)(bottom - 0.42f * cell), right, bottom);
                z.Intersect(new Rectangle(0, 0, w, h));
                if (z.Width >= 6 && z.Height >= 6) zones.Add((i + j, z));
            }
        return zones.OrderBy(z => z.Rank).Select(z => z.Zone).Distinct().Take(6).ToList();
    }

    // most common brightness in the corner = the cell's background
    static int Background(int[] lum, int w, Rectangle z)
    {
        var hist = new int[256];
        for (int y = z.Top; y < z.Bottom; y++)
            for (int x = z.Left; x < z.Right; x++)
                if (lum[y * w + x] >= 0) hist[lum[y * w + x]]++;
        int best = 100;
        for (int i = 1; i < 255; i++)
            if (hist[i] + hist[i - 1] + hist[i + 1] > hist[best] + hist[Math.Max(0, best - 1)] + hist[Math.Min(255, best + 1)]) best = i;
        return best;
    }

    // box around the light (number) pixels of the corner, 2 px margin; null when there are none
    static Rectangle? Tight(int[] lum, int[] sat, int w, Rectangle z, int bright)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = z.Top; y < z.Bottom; y++)
            for (int x = z.Left; x < z.Right; x++)
            {
                int p = y * w + x;
                if (lum[p] < bright || sat[p] > 60) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        if (x1 < 0 || y1 - y0 < 2) return null;
        var r = Rectangle.FromLTRB(x0 - 2, y0 - 2, x1 + 3, y1 + 3);
        r.Intersect(z);
        return r;
    }

    // corner -> dark digits on white: brightness lo..hi becomes white..black, colored pixels stay white.
    // copies: the same picture several times side by side (short text is read better)
    static Bitmap Stretch(int[] lum, int[] sat, int w, Rectangle z, int lo, int hi, int scale, int pad, int copies = 1)
    {
        using var small = new Bitmap(z.Width, z.Height, PixelFormat.Format32bppArgb);
        var dst = small.LockBits(new Rectangle(0, 0, z.Width, z.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var outRow = new byte[z.Width * 4];
            for (int y = 0; y < z.Height; y++)
            {
                for (int x = 0; x < z.Width; x++)
                {
                    int p = (z.Top + y) * w + z.Left + x;
                    int l = lum[p];
                    double t = l < 0 || sat[p] > 60 ? 0 : Math.Clamp((l - lo) / (double)Math.Max(1, hi - lo), 0, 1);
                    byte v = (byte)(255 - (int)(t * 255));
                    int i = x * 4;
                    outRow[i] = outRow[i + 1] = outRow[i + 2] = v;
                    outRow[i + 3] = 255;
                }
                Marshal.Copy(outRow, 0, dst.Scan0 + y * dst.Stride, outRow.Length);
            }
        }
        finally
        {
            small.UnlockBits(dst);
        }
        int cw = z.Width * scale, gap = Math.Max(z.Height * scale, cw / 2);
        var result = new Bitmap(copies * cw + (copies - 1) * gap + pad * 2, z.Height * scale + pad * 2, PixelFormat.Format32bppArgb);
        using var gr = Graphics.FromImage(result);
        gr.Clear(Color.White);
        gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
        gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
        for (int c = 0; c < copies; c++)
            gr.DrawImage(small, new Rectangle(pad + c * (cw + gap), pad, cw, z.Height * scale));
        return result;
    }

    // debug log on: the searched area as it is on the screen (enlarged 4x, mouse = red cross, tooltip = blue frame)
    static void SaveRaw(Bitmap shot, Rectangle area, Point cursor, Rectangle? tooltip, string tag)
    {
        try
        {
            using var big = new Bitmap(area.Width * 4, area.Height * 4);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(shot, new Rectangle(0, 0, big.Width, big.Height), area, GraphicsUnit.Pixel);
                int cx = (cursor.X - area.X) * 4, cy = (cursor.Y - area.Y) * 4;
                using var red = new Pen(Color.Red, 2);
                g.DrawLine(red, cx - 8, cy, cx + 8, cy);
                g.DrawLine(red, cx, cy - 8, cx, cy + 8);
                if (tooltip is Rectangle t)
                {
                    using var blue = new Pen(Color.DeepSkyBlue, 2);
                    g.DrawRectangle(blue, (t.X - area.X) * 4, (t.Y - area.Y) * 4, t.Width * 4, t.Height * 4);
                }
            }
            SaveDebug(big, (tag.Length > 0 ? "clean-" : "") + "raw");
        }
        catch { }
    }

    // debug log on: keeps the pictures given to the OCR (the newest 20) to improve the reading
    static void SaveDebug(Bitmap bmp, string tag)
    {
        try
        {
            string dir = Path.Combine(SettingsStore.Dir, "stack");
            Directory.CreateDirectory(dir);
            bmp.Save(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{tag}.png"), ImageFormat.Png);
            foreach (var old in new DirectoryInfo(dir).GetFiles("*.png").OrderByDescending(f => f.Name).Skip(40))
                old.Delete();
        }
        catch { }
    }

    // "1O" -> "10", "l8" -> "18"; null when it is not a number
    static string? Digits(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s)
        {
            char c = ch switch
            {
                'O' or 'o' or 'D' => '0',
                'l' or 'I' or '|' or 'i' => '1',
                'S' or 's' => '5',
                'B' => '8',
                'Z' or 'z' => '2',
                'g' or 'q' => '9',
                'b' or 'G' => '6',
                'T' => '7',
                '<' or 'A' => '4',
                _ => ch
            };
            if (c == ' ') continue;
            if (!char.IsDigit(c)) return null;
            sb.Append(c);
        }
        return sb.Length is >= 1 and <= 4 ? sb.ToString() : null;
    }

    // near-white pixels -> black on white (inside the tooltip box nothing is kept), then enlarged.
    // Only white spots of digit size are kept: the large white parts of item pictures and the item
    // names are left out, so the OCR sees little more than the number.
    static Bitmap? WhiteText(Bitmap shot, Rectangle area, int scale, int pad, Rectangle? tooltip, float cell, int minBright, int maxTint)
    {
        int w = area.Width, h = area.Height;
        var white = new bool[w * h];
        var src = shot.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, row.Length);
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    int b = row[i], g = row[i + 1], r = row[i + 2];
                    int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
                    bool inTooltip = tooltip is Rectangle t && t.Contains(area.X + x, area.Y + y);
                    white[y * w + x] = !inTooltip && mn >= minBright && mx - mn < maxTint;
                }
            }
        }
        finally
        {
            shot.UnlockBits(src);
        }

        // connected white spots; keep the ones a digit could be
        int minH = Math.Max(3, (int)(0.07f * cell)), maxH = Math.Max(minH + 2, (int)(0.34f * cell)), maxW = (int)(0.3f * cell);
        var keep = new bool[w * h];
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        var comp = new List<int>();
        int kept = 0;
        for (int start = 0; start < white.Length; start++)
        {
            if (!white[start] || seen[start]) continue;
            comp.Clear();
            stack.Push(start);
            seen[start] = true;
            int x0 = w, y0 = h, x1 = 0, y1 = 0;
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                comp.Add(p);
                int px = p % w, py = p / w;
                x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); y0 = Math.Min(y0, py); y1 = Math.Max(y1, py);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (white[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                    }
            }
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            if (ch < minH || ch > maxH || cw > maxW || comp.Count < 3) continue;
            foreach (int p in comp) keep[p] = true;
            kept += comp.Count;
        }
        if (kept < 6) return null;   // no white text of digit size

        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dst = small.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var outRow = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;
                    byte v = keep[y * w + x] ? (byte)0 : (byte)255;
                    outRow[i] = outRow[i + 1] = outRow[i + 2] = v;
                    outRow[i + 3] = 255;
                }
                Marshal.Copy(outRow, 0, dst.Scan0 + y * dst.Stride, outRow.Length);
            }
        }
        finally
        {
            small.UnlockBits(dst);
        }

        var result = new Bitmap(w * scale + pad * 2, h * scale + pad * 2, PixelFormat.Format32bppArgb);
        using var gr = Graphics.FromImage(result);
        gr.Clear(Color.White);
        gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
        gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
        gr.DrawImage(small, new Rectangle(pad, pad, w * scale, h * scale));
        return result;
    }
}
