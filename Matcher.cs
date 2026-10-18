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

static class Matcher
{
    // Lookup structures for one loaded price list (rebuilt automatically when the list changes)
    sealed class ItemIndex
    {
        public readonly List<Item> Items;
        public readonly Dictionary<string, Item> ByName = new();          // exact normalized full name
        public readonly Dictionary<int, List<Item>> ByLength = new();     // normalized name length -> items

        public ItemIndex(List<Item> items)
        {
            Items = items;
            foreach (var item in items)
            {
                if (item.NormName.Length == 0) continue;
                ByName.TryAdd(item.NormName, item);
                if (!ByLength.TryGetValue(item.NormName.Length, out var bucket))
                    ByLength[item.NormName.Length] = bucket = new List<Item>();
                bucket.Add(item);
            }
        }
    }

    static ItemIndex? _index;

    static ItemIndex IndexFor(List<Item> items)
    {
        var idx = _index;
        if (idx == null || !ReferenceEquals(idx.Items, items))
        {
            idx = new ItemIndex(items);
            _index = idx;
        }
        return idx;
    }

    // The tooltip shows the FULL item name, while inventory cells are labeled with SHORT names
    // ("Sprats", "M.parts") that often belong to neighbouring cells. So only full names are matched:
    //  0. exact match of a candidate with a full name (instant, nearest candidate first)
    //  1. fuzzy match of all text candidates against full names (candidates nearer to the cursor win ties)
    // Returns the match (or null), its score, and the closest item even when below the threshold.
    // other: set when the result is AMBIGUOUS - a different item scored almost the same and the read
    // text does not tell them apart (item is then null, closest and other are the two candidates).
    // Saying "not sure" is better than silently showing the wrong one.
    public const double SureScore = 0.90;   // at or above: never treated as ambiguous
    public const double MinMargin = 0.04;   // below SureScore the best item must lead by at least this much

    public static (Item? item, double score, Item? closest, Item? other) Find(
        IEnumerable<OcrLineInfo> ocrLines, List<Item> items, AppSettings s, double cursorX, double cursorY,
        Func<Item, bool>? filter = null)
    {
        double threshold = s.MinSimilarityPercent / 100.0;
        var idx = IndexFor(items);
        var lines = ocrLines.ToList();
        var candidates = Candidates(lines, cursorX, cursorY);
        Log.Write("Candidates (nearest to the cursor first): " + string.Join(" | ", candidates.Take(10)));

        // Pass 0 + 1: full names
        var r = Rank(candidates, idx, filter);
        if (r.Best != null && r.BestScore >= threshold)
            return Decide(r, Tokens(lines));

        return (null, r.BestScore, r.Best, null);
    }

    // Tooltip only: the box contains nothing but the item name, so the COMPLETE text of every
    // reading (OCR attempt) is one candidate. No splitting and joining guesswork.
    public static (Item? item, double score, Item? closest, Item? other) FindInTooltip(
        IReadOnlyList<List<OcrLineInfo>> readings, List<Item> items, AppSettings s, Rectangle box)
    {
        double threshold = s.MinSimilarityPercent / 100.0;
        var idx = IndexFor(items);
        var filter = TooltipWidthFilter(box, readings);
        var candidates = new List<string>();
        foreach (var reading in readings)
        {
            string n = TarkovApi.Norm(TooltipText(reading));
            if (n.Length >= 3 && !candidates.Contains(n)) candidates.Add(n);
        }
        Log.Write("Tooltip candidates: " + string.Join(" | ", candidates));

        var r = Rank(candidates, idx, filter);
        if (r.Best != null && r.BestScore >= threshold)
            return Decide(r, Tokens(readings.SelectMany(x => x)));
        return (null, r.BestScore, r.Best, null);
    }

    // The text of a tooltip in reading order: rows from top to bottom, words from left to right
    public static string TooltipText(List<OcrLineInfo> lines) =>
        string.Join(" ", Rows(lines).Select(row => string.Join(" ", row.OrderBy(l => l.Left).Select(l => l.Text))));

    static List<List<OcrLineInfo>> Rows(List<OcrLineInfo> lines)
    {
        var rows = new List<List<OcrLineInfo>>();
        foreach (var l in lines.OrderBy(l => l.Y))
        {
            var row = rows.Count > 0 ? rows[^1] : null;
            if (row != null && Math.Abs(l.Y - row[0].Y) < Math.Max(1, row[0].Bottom - row[0].Top) * 0.5)
                row.Add(l);
            else
                rows.Add(new List<OcrLineInfo> { l });
        }
        return rows;
    }

    // ---- Tooltip width check ----
    // The game sizes a one-line tooltip exactly to its text, so the box width tells how long the name is
    // (measured on 34 tooltips: at most 5 % off). A reading that lost words ("battery" of "D Size battery")
    // can then no longer match a shorter name ("AA Battery"). Widths are relative to the box height,
    // so this works at every resolution.
    const string NarrowChars = "iljI.,'()-!t1|:;f r";
    const string WideChars = "mwMW@";

    static double NameUnits(string name)
    {
        double u = 0;
        foreach (char c in name)
            u += NarrowChars.IndexOf(c) >= 0 ? 0.55 : WideChars.IndexOf(c) >= 0 ? 1.35 : 1.0;
        return u;
    }

    public static double ExpectedTooltipWidth(string name, int boxHeight) =>
        boxHeight * (0.904 + 0.22 * NameUnits(name));

    // Only for one-line tooltips (wrapped names have a fixed maximum width); null = no check
    public static Func<Item, bool>? TooltipWidthFilter(Rectangle box, IReadOnlyList<List<OcrLineInfo>> readings)
    {
        bool singleLine = box.Width < box.Height * 5 && readings.All(r => Rows(r).Count <= 1);
        if (!singleLine || box.Height < 8) return null;
        return item =>
        {
            double ratio = box.Width / ExpectedTooltipWidth(item.Name, box.Height);
            return ratio >= 0.87 && ratio <= 1.15;
        };
    }

    readonly record struct Ranking(Item? Best, double BestScore, Item? Second, double SecondScore);

    // Best item and the best item with a DIFFERENT name, over all candidates
    static Ranking Rank(List<string> candidates, ItemIndex idx, Func<Item, bool>? filter = null)
    {
        // the OCR often reads "m" as "rn" (and "mm" as "nrn"): also try the repaired text
        candidates = candidates.SelectMany(Variants).Distinct().ToList();

        // exact names
        foreach (var line in candidates)
            if (idx.ByName.TryGetValue(line, out var exact))
                return new Ranking(exact, 1.0, null, 0);

        // fuzzy (only names of a similar length can reach a useful score)
        Item? best = null;
        double bestScore = 0;
        Item? second = null;
        double secondScore = 0;
        foreach (var line in candidates)
        {
            int len = line.Length;
            int lo = (int)Math.Floor(len * 0.6);
            int hi = (int)Math.Ceiling(len / 0.6);
            for (int n = lo; n <= hi; n++)
            {
                if (!idx.ByLength.TryGetValue(n, out var bucket)) continue;
                foreach (var item in bucket)
                {
                    if (filter != null && !filter(item)) continue;   // name does not fit the tooltip width
                    double sc = Score(line, item.NormName, secondScore);
                    if (sc > bestScore)   // strict: on equal scores the earlier (nearer) candidate stays
                    {
                        if (best != null && best.Name != item.Name)
                        {
                            second = best;
                            secondScore = bestScore;
                        }
                        bestScore = sc;
                        best = item;
                    }
                    else if (sc > secondScore && best != null && best.Name != item.Name)
                    {
                        secondScore = sc;
                        second = item;
                    }
                }
            }
        }
        return new Ranking(best, bestScore, second, secondScore);
    }

    // Accepts the best item unless a different item is almost as good. Then the words in which the two
    // names differ decide ("30-round" vs. "40-round", "6L10" vs. "6P2"); only if they don't, it is ambiguous.
    static (Item? item, double score, Item? closest, Item? other) Decide(Ranking r, List<string> ocrTokens)
    {
        var best = r.Best!;
        if (r.BestScore >= SureScore || r.Second == null || r.BestScore - r.SecondScore >= MinMargin)
            return (best, r.BestScore, best, null);

        int tie = TieBreak(best, r.Second, ocrTokens);
        if (tie > 0) return (best, r.BestScore, best, null);
        if (tie < 0) return (r.Second, r.SecondScore, r.Second, null);

        Log.Write($"Ambiguous: {best.Name} ({r.BestScore:P0}) vs. {r.Second.Name} ({r.SecondScore:P0})");
        return (null, r.BestScore, best, r.Second);
    }

    // +1 = the read text supports a, -1 = it supports b, 0 = undecided
    static int TieBreak(Item a, Item b, List<string> ocr)
    {
        if (ocr.Count == 0) return 0;
        var ta = NameTokens(a.Name);
        var tb = NameTokens(b.Name);
        var onlyA = ta.Except(tb).ToList();
        var onlyB = tb.Except(ta).ToList();
        int result;

        if (onlyA.Count == 0 && onlyB.Count == 0)
        {
            result = 0;
        }
        else if (onlyA.Count == 0 || onlyB.Count == 0)
        {
            // one name has extra words ("... (Black)"): were they read?
            var extra = onlyA.Count > 0 ? onlyA : onlyB;
            double support = extra.Average(t => ocr.Max(o => Similarity(t, o)));
            int forExtra = support >= 0.8 ? 1 : support <= 0.4 ? -1 : 0;
            result = onlyA.Count > 0 ? forExtra : -forExtra;
        }
        else
        {
            // edit distance of the differing words to the closest read words; at least one edit better wins
            double da = onlyA.Sum(t => ocr.Min(o => Distance(t, o, double.MaxValue)));
            double db = onlyB.Sum(t => ocr.Min(o => Distance(t, o, double.MaxValue)));
            result = da <= db - 1 ? 1 : db <= da - 1 ? -1 : 0;
        }

        Log.Write($"Tie-break {a.Name} vs. {b.Name}: " +
                  (result > 0 ? "first" : result < 0 ? "second" : "undecided") +
                  $" (differing words: {string.Join(",", onlyA)} / {string.Join(",", onlyB)})");
        return result;
    }

    static HashSet<string> NameTokens(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TarkovApi.Norm).Where(t => t.Length > 0).ToHashSet();

    // Normalized words of the read text, plus each pair of neighbouring words joined
    // (OCR sometimes splits a word: "Screa nuts")
    static List<string> Tokens(IEnumerable<OcrLineInfo> lines)
    {
        var result = new HashSet<string>();
        foreach (var l in lines)
        {
            var words = l.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(TarkovApi.Norm).Where(w => w.Length > 0).ToList();
            for (int i = 0; i < words.Count; i++)
            {
                result.Add(words[i]);
                if (i + 1 < words.Count) result.Add(words[i] + words[i + 1]);
            }
        }
        return result.ToList();
    }

    // Builds the normalized text candidates, nearest to the cursor first:
    //  - every OCR line on its own
    //  - lines joined with the left-aligned lines directly below them
    //    (long tooltip names wrap onto several lines, e.g. weapons)
    static List<string> Candidates(IEnumerable<OcrLineInfo> ocrLines, double cx, double cy)
    {
        var sorted = ocrLines.OrderBy(l => l.Top).ToList();
        var found = new List<(string Norm, double Dist)>();

        static double Dist(OcrLineInfo l, double px, double py) =>
            Math.Pow(l.X - px, 2) + Math.Pow(l.Y - py, 2);

        for (int i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            double dist = Dist(a, cx, cy);
            found.Add((TarkovApi.Norm(a.Text), dist));

            // Follow the wrapped tooltip downwards: the next line is the topmost line that sits directly
            // below the previous one and is left-aligned with the first. The search covers ALL lines
            // (an inventory full of small labels puts many unrelated lines in between when sorted by top).
            string joined = a.Text;
            var prev = a;
            for (int taken = 0; taken < 3; taken++)
            {
                double h = Math.Max(1, prev.Bottom - prev.Top);
                OcrLineInfo? next = null;
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var b = sorted[j];
                    if (b.Top < prev.Top + h * 0.5) continue;        // same row as the previous line
                    if (b.Top - prev.Bottom > h * 0.8) break;        // too far below (all further lines are even lower)
                    if (Math.Abs(b.Left - a.Left) > h * 2) continue; // not left-aligned with the first line
                    next = b;
                    break;
                }
                if (next == null) break;

                joined += " " + next.Text;
                found.Add((TarkovApi.Norm(joined), Math.Min(dist, Dist(next, cx, cy))));
                prev = next;
            }
        }

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (norm, _) in found.OrderBy(f => f.Dist))
            if (norm.Length >= 3 && seen.Add(norm))
                result.Add(norm);
        return result;
    }

    // Similarity plus a containment rule: the tooltip text may lack (or add) a prefix/suffix
    // compared to the database name. Only for longer texts of similar length, otherwise short
    // inventory labels ("Tushonka") would match long names ("Tushonka beef stew").
    // At least 75 % of the longer text must be present: the beginning of a name ("PP-19-01 Vityaz") is shared
    // by many items (magazines, gas tube ...) and must not count as a match for one of them.
    // minUseful: scores at or below this value are not needed by the caller, so the distance
    // calculation may stop early (the result is then 0 or the containment score).
    static double Score(string line, string name, double minUseful)
    {
        int min = Math.Min(line.Length, name.Length);
        int max = Math.Max(line.Length, name.Length);
        double ratio = (double)min / max;

        // The score drops steeply with the missing part: a wrapped tooltip's 2nd line alone
        // ("(Black Division)") must never beat the complete name ("... Plate Carrier (Black Division)").
        double contained = 0;
        if (min >= 10 && ratio >= 0.75 &&
            (name.Contains(line, StringComparison.Ordinal) || line.Contains(name, StringComparison.Ordinal)))
            contained = 0.55 + 0.45 * ratio * ratio;

        double sc = Similarity(line, name, Math.Max(minUseful, contained));
        return Math.Max(sc, contained);
    }

    // 1 = identical, 0 = nothing in common. Returns 0 when the result would not exceed minUseful.
    static double Similarity(string a, string b, double minUseful = 0)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        int maxLen = Math.Max(a.Length, b.Length);
        // Quick pre-filter: lengths too different -> no match
        if (Math.Abs(a.Length - b.Length) > maxLen * 0.4) return 0;

        double maxDist = (1.0 - Math.Clamp(minUseful, 0, 1)) * maxLen;
        double d = Distance(a, b, maxDist);
        if (d > maxDist) return 0;
        return 1.0 - d / maxLen;
    }

    static IEnumerable<string> Variants(string line)
    {
        yield return line;
        if (line.Contains("nrn")) yield return line.Replace("nrn", "mm");
        if (line.Contains("rn")) yield return line.Replace("rn", "m");
    }

    // Typical OCR confusions are cheaper than other substitutions (input is normalized: lower case, no spaces)
    static double SubCost(char x, char y)
    {
        if (x > y) (x, y) = (y, x);
        return (x, y) switch
        {
            ('0', 'o') or ('1', 'l') or ('1', 'i') or ('i', 'l') or
            ('5', 's') or ('8', 'b') or ('2', 'z') or ('9', 'g') => 0.35,
            ('m', 'n') => 0.5,
            _ => 1.0
        };
    }

    // Reused row buffers (one set per thread, matching runs on a worker thread)
    [ThreadStatic] static double[]? _rowA, _rowB;

    // Weighted Levenshtein distance; stops as soon as the result must exceed maxDist (returns maxDist + 1 then)
    static double Distance(string a, string b, double maxDist)
    {
        int m = b.Length + 1;
        if (_rowA == null || _rowB == null || _rowA.Length < m)
        {
            _rowA = new double[m * 2];
            _rowB = new double[m * 2];
        }
        var prev = _rowA;
        var cur = _rowB;

        for (int j = 0; j < m; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            char ca = a[i - 1];
            cur[0] = i;
            double rowMin = i;
            for (int j = 1; j < m; j++)
            {
                double cost = ca == b[j - 1] ? 0 : SubCost(ca, b[j - 1]);
                double v = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                cur[j] = v;
                if (v < rowMin) rowMin = v;
            }
            if (rowMin > maxDist) return maxDist + 1;   // every path is already too expensive
            (prev, cur) = (cur, prev);
        }
        return prev[m - 1];
    }
}
