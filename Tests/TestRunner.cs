using System.Text.Json;

namespace TarkovPriceOverlay.Tests;

// One test case = one hovered tooltip:
//  Expect   = item name that must be recognized, or null = must NOT recognize anything (no wrong guess)
//  Box      = tooltip size in pixels [width, height] (as logged: "Tooltip 211x37 found")
//  Readings = the OCR readings of that tooltip (as logged: "Tooltip OCR (...): <text>")
sealed record TestCase(string? Expect, int[] Box, string[] Readings, string? Note = null);

static class TestRunner
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // the user's own settings (minimum similarity, learned stack numbers), but never write into the debug log
        SettingsStore.Load();
        var s = SettingsStore.Current;
        s.DebugLog = false;

        // 1. number recognition (needs no price list)
        var (numPassed, numFailed) = NumberTests.Run();

        string casesPath = Path.Combine(AppContext.BaseDirectory, "cases.json");
        string itemsPath = args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                           ?? Path.Combine(SettingsStore.Dir, "items-en-regular.json");

        if (!File.Exists(itemsPath))
        {
            Console.WriteLine("Price list not found: " + itemsPath);
            Console.WriteLine("Start the app once (it saves the list there) or pass the path of an items-*.json file.");
            return numFailed == 0 ? 2 : 1;
        }

        // 2. item names: OCR readings against the price list
        var items = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(itemsPath)) ?? new List<Item>();
        var cases = JsonSerializer.Deserialize<List<TestCase>>(File.ReadAllText(casesPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
                                                    AllowTrailingCommas = true })
                    ?? new List<TestCase>();
        var names = items.Select(i => i.Name).ToHashSet();

        Console.WriteLine($"{items.Count} items ({Path.GetFileName(itemsPath)}), {cases.Count} cases, " +
                          $"minimum similarity {s.MinSimilarityPercent} %");
        Console.WriteLine();

        int passed = 0, failed = 0;
        foreach (var c in cases)
        {
            var box = new Rectangle(0, 0, c.Box[0], c.Box[1]);
            var readings = c.Readings
                .Select(r => new List<OcrLineInfo> { new(r, 0, 0, 0, 0, 0, 1) })
                .ToList();

            var (item, score, closest, other) = Matcher.FindInTooltip(readings, items, s, box);
            bool ok = c.Expect == null ? item == null : item?.Name == c.Expect;

            string expect = c.Expect ?? "(nothing)";
            string got = item != null ? $"{item.Name} ({score:P0})"
                       : other != null ? $"not sure: {closest?.Name} / {other.Name}"
                       : $"nothing (closest: {closest?.Name} {score:P0})";

            if (ok)
            {
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("PASS ");
            }
            else
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAIL ");
            }
            Console.ResetColor();
            Console.WriteLine($"{expect,-58} -> {got}");
            if (!ok)
                Console.WriteLine($"     readings: {string.Join(" | ", c.Readings)}");
            if (c.Expect != null && !names.Contains(c.Expect))
                Console.WriteLine($"     WARNING: \"{c.Expect}\" is not in the price list (typo in cases.json?)");
        }

        Console.WriteLine();
        Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Item names: {passed} passed, {failed} failed");
        Console.ForegroundColor = numFailed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Numbers:    {numPassed} passed, {numFailed} failed");
        Console.ResetColor();
        return failed + numFailed == 0 ? 0 : 1;
    }
}
