using System.Text.Json;

namespace TarkovPriceOverlay.Tests;

// One picture from the game and the number that must be read from it (see digits.json)
sealed record NumberCase(string File, string Kind, int[] Cursor, int Max, string? Expect, string? Note = null);

// Number recognition tests: stack counts, armor durability and key uses, read from saved pictures
// with the same code as in the game. Catches a change that makes "20" come out as "25" again.
static class NumberTests
{
    public static (int Passed, int Failed) Run()
    {
        string dir = AppContext.BaseDirectory;
        string path = Path.Combine(dir, "digits.json");
        if (!File.Exists(path)) return (0, 0);
        var cases = JsonSerializer.Deserialize<List<NumberCase>>(File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
                                                    AllowTrailingCommas = true }) ?? new List<NumberCase>();
        Console.WriteLine($"Number recognition: {cases.Count} pictures");
        int passed = 0, failed = 0;
        foreach (var c in cases)
        {
            string got;
            try
            {
                using var bmp = new Bitmap(Path.Combine(dir, "images", c.File));
                var cursor = new Point(c.Cursor[0], c.Cursor[1]);
                got = c.Kind.ToLowerInvariant() switch
                {
                    "stack" => StackCount.ReadAsync(bmp, cursor, 1f, null, c.Max, "test").GetAwaiter().GetResult()?.ToString() ?? "(nothing)",
                    "durability" => Fraction(StackCount.ReadDurabilityAsync(bmp, cursor, 1f, null, c.Max).GetAwaiter().GetResult()),
                    "keyuses" => Fraction(StackCount.ReadKeyUsesAsync(bmp, cursor, 1f, null, c.Max).GetAwaiter().GetResult()),
                    _ => "unknown kind " + c.Kind
                };
            }
            catch (Exception ex) { got = "error: " + ex.Message; }

            bool ok = got == (c.Expect ?? "(nothing)");
            if (ok) passed++; else failed++;
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(ok ? "PASS " : "FAIL ");
            Console.ResetColor();
            Console.WriteLine($"{c.File} ({c.Kind}) {c.Expect ?? "(nothing)",-10} -> {got}" + (ok || c.Note == null ? "" : "   [" + c.Note + "]"));
        }
        Console.WriteLine();
        return (passed, failed);
    }

    static string Fraction((int Cur, int Max)? v) => v is { } f ? $"{f.Cur}/{f.Max}" : "(nothing)";
}
