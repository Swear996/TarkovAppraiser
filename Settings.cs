using System.Text.Json;
using System.Text.RegularExpressions;

namespace TarkovPriceOverlay;

public class AppSettings
{
    static int Argb(uint v) => unchecked((int)v);

    public static readonly string[] TierNames = { "Junk", "Common", "Good", "Rare", "Legendary" };

    public static long[] DefaultThresholds() => new long[] { 10_000, 25_000, 50_000, 100_000 };

    public static int[] DefaultTierColors() => new[]
    {
        Argb(0xFF9AA0A6), // Junk       - grey
        Argb(0xFF5CC46B), // Common     - green
        Argb(0xFF4DA3FF), // Good       - blue
        Argb(0xFFB66DFF), // Rare       - purple
        Argb(0xFFFFB300), // Legendary  - gold
    };

    // ---- Hotkey (modifiers: 1 = Alt, 2 = Ctrl, 4 = Shift) ----
    public uint HotkeyMods { get; set; } = 0;
    public uint HotkeyVk { get; set; } = 0x77; // F8
    public uint PauseHotkeyMods { get; set; } = 2;     // Ctrl
    public uint PauseHotkeyVk { get; set; } = 0x77;    // F8 -> Ctrl+F8 pauses the automatic mode (0 = no hotkey)
    public uint StackHotkeyMods { get; set; } = 4;     // Shift
    public uint StackHotkeyVk { get; set; } = 0x77;    // F8 -> Shift+F8: type the stack count (0 = no hotkey)
    public uint HistoryHotkeyMods { get; set; } = 1;   // Alt
    public uint HistoryHotkeyVk { get; set; } = 0x77;  // F8 -> Alt+F8: show / hide the in-game history list (0 = no hotkey)

    // ---- In-game history list ----
    public bool HistoryWinVisible { get; set; } = false;
    public int HistoryWinX { get; set; } = -1;          // -1 = not placed yet (top right)
    public int HistoryWinY { get; set; } = -1;
    public int HistoryWinW { get; set; } = 330;
    public int HistoryWinH { get; set; } = 300;
    public int HistoryWinOpacity { get; set; } = 85;     // own opacity, independent of the price box
    public bool HistoryWinOnlyInGame { get; set; } = true; // hidden while another program is in front

    // ---- Language of the program (en | de) ----
    public string UiLanguage { get; set; } = DefaultUiLanguage();
    static string DefaultUiLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    // ---- Language & data ----
    public string OcrLanguage { get; set; } = "en-US";
    public string ApiLanguage { get; set; } = "en";
    public string GameMode { get; set; } = "regular"; // regular | pve
    public int PriceRefreshMinutes { get; set; } = 10;

    // ---- Detection ----
    public int MinSimilarityPercent { get; set; } = 72;
    public int CaptureWidth { get; set; } = 800;
    public int CaptureHeight { get; set; } = 400;
    public int CaptureOffsetX { get; set; } = -350;
    public int CaptureOffsetY { get; set; } = -100;
    public int ResolutionHeight { get; set; } = 0;      // 0 = detect automatically, otherwise the game's height in px

    // ---- Auto hover (no hotkey needed) ----
    public bool AutoHover { get; set; } = false;
    public int AutoHoverDelayMs { get; set; } = 300;
    public bool AutoHoverOnlyInGame { get; set; } = true;

    // ---- Overlay ----
    public string Position { get; set; } = "Cursor"; // Cursor | TopLeft | TopRight | BottomLeft | BottomRight
    public int OverlaySeconds { get; set; } = 5;
    public int OverlayOpacityPercent { get; set; } = 92;
    public int OverlayScalePercent { get; set; } = 100;   // size of the whole price box (50 - 200 %)
    public int OffsetX { get; set; } = 24;
    public int OffsetY { get; set; } = 24;
    public int BackColorArgb { get; set; } = unchecked((int)0xFF15171C);
    public int TextColorArgb { get; set; } = unchecked((int)0xFFFFFFFF);

    public bool ShowFleaLow { get; set; } = true;
    public bool ShowFleaAvg { get; set; } = true;
    public bool ShowFleaNet { get; set; } = true;        // flea price after the market fee
    public bool ShowBestOption { get; set; } = true;     // "Best: flea market / trader"
    public bool IntelCenter { get; set; } = false;       // Intelligence Center level 3: 30 % lower flea fee
    public bool ShowBestTrader { get; set; } = true;
    public bool ShowAllTraders { get; set; } = false;
    public bool ShowPerSlot { get; set; } = true;
    public bool ShowTraderImages { get; set; } = true;
    public bool ShowItemImage { get; set; } = true;
    public bool ShowAmmoStats { get; set; } = true;   // ammo: penetration, damage, armor damage
    public bool ShowArmor { get; set; } = true;       // armor, helmets, rigs: class + durability
    public bool ShowKeyInfo { get; set; } = true;     // keys: maps + uses
    public bool ShowUses { get; set; } = true;        // hint when a barter / craft is worth more than selling
    public bool OverlayCompact { get; set; } = false;   // only name + best sell price (+ counted stack)
    public bool OverlayFade { get; set; } = true;
    public bool ShowInScreenshots { get; set; } = true;   // price box / in-game list appear in screenshots and recordings
    public bool DimJunk { get; set; } = true;         // items below the first tier are dimmed and marked "Junk"
    public bool ReadStackCount { get; set; } = true;  // read the number in the cell (e.g. 18 rounds), experimental

    // Layout (drag & drop in the live preview): order of the parts and side of the picture
    public string[] OverlayOrder { get; set; } = (string[])OverlayPainter.DefaultOrder.Clone();
    public string ImageSide { get; set; } = "Left";   // Left | Right | Top

    // ---- Loot tiers ----
    public bool UseTiers { get; set; } = true;
    public string TierBasis { get; set; } = "PerSlot"; // PerSlot | Total
    public bool TierColorName { get; set; } = true;
    public bool TierShowLabel { get; set; } = true;
    public bool AmmoTierByPen { get; set; } = true;
    public bool TierFrame { get; set; } = true;        // price box framed in the tier color (from the first tier up)   // ammo: tier name + name color by penetration, prices keep their price color
    public long[] TierThresholds { get; set; } = DefaultThresholds(); // start of tier 1..4
    public int[] TierColors { get; set; } = DefaultTierColors();      // tier 0..4

    // ---- Menu theme ----
    public string ThemePreset { get; set; } = "Fluent";
    public int ThemeBackArgb { get; set; } = unchecked((int)0xFF0D0F13);
    public int ThemePanelArgb { get; set; } = unchecked((int)0xFF111318);
    public int ThemeTextArgb { get; set; } = unchecked((int)0xFFE8EAF0);
    public int ThemeAccentArgb { get; set; } = unchecked((int)0xFF5B8CFF);

    // ---- System ----
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;   // false = the settings window opens at startup
    public bool DebugLog { get; set; } = false;
    public bool CheckForUpdates { get; set; } = true;
    public bool WelcomeDone { get; set; } = false;      // the welcome window was shown (first start)

    // copy for "what if" layouts in the live preview (arrays are replaced, never changed in place)
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public static string HotkeyText(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 4) != 0) parts.Add("Shift");
        parts.Add(((Keys)vk).ToString());
        return string.Join("+", parts);
    }
}

public static class SettingsStore
{
    // %AppData%\TarkovAppraiser (settings, price cache, pictures, log).
    // The folder of the old versions ("LootLens", before that "TarkovPriceOverlay") is taken over on the first start.
    public static readonly string Dir = PrepareDir();

    static string PrepareDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "TarkovAppraiser");
        foreach (var name in new[] { "LootLens", "TarkovPriceOverlay" })
        {
            string old = Path.Combine(appData, name);
            try
            {
                if (!Directory.Exists(dir) && Directory.Exists(old))
                    Directory.Move(old, dir);
            }
            catch
            {
                // old folder in use: keep using it, nothing is lost
                if (!Directory.Exists(dir) && Directory.Exists(old)) return old;
            }
        }
        return dir;
    }

    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public static readonly string LogPath = Path.Combine(Dir, "debug.log");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = Parse(File.ReadAllText(FilePath));
                if (s != null) Current = Validate(s);
                L.Language = Current.UiLanguage;
            }
        }
        catch (Exception ex)
        {
            Current = new AppSettings();
            Log.Write("Could not load settings: " + ex.Message, true);
        }
        L.Language = Current.UiLanguage;
    }

    public static void Save(AppSettings s)
    {
        Current = Validate(s);
        L.Language = Current.UiLanguage;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Indented));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save settings: " + ex.Message, true);
        }
    }

    // Export / import (settings window, General page)
    public static void Export(AppSettings s, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(s, Indented));

    // Throws when the file is not a settings file
    public static AppSettings Import(string path)
    {
        var s = Parse(File.ReadAllText(path))
                ?? throw new InvalidDataException("The file contains no settings.");
        return Validate(s);
    }

    // Reads a settings file. Older versions had "FontSize" (11 = normal) instead of the size in %.
    static AppSettings? Parse(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json);
        if (s == null) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty(nameof(AppSettings.OverlayScalePercent), out _) &&
            root.TryGetProperty("FontSize", out var fs) && fs.TryGetInt32(out int size) && size > 0)
            s.OverlayScalePercent = (int)Math.Round(size * 100 / 11.0 / 5) * 5;   // rounded to 5 %
        if (!root.TryGetProperty(nameof(AppSettings.WelcomeDone), out _))
            s.WelcomeDone = true;   // settings of an older version: not a first start
        return s;
    }

    // Clamps every value into its allowed range (also used for imported settings files)
    static AppSettings Validate(AppSettings s)
    {
        const int opaque = unchecked((int)0xFF000000);

        if (s.HotkeyVk == 0) s.HotkeyVk = 0x77;
        if (!Regex.IsMatch(s.ApiLanguage ?? "", "^[a-z]{2}$")) s.ApiLanguage = "en";
        if (string.IsNullOrWhiteSpace(s.OcrLanguage)) s.OcrLanguage = "en-US";
        if (s.GameMode != "pve") s.GameMode = "regular";

        string[] positions = { "Cursor", "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        if (!positions.Contains(s.Position)) s.Position = "Cursor";

        s.PriceRefreshMinutes = Math.Clamp(s.PriceRefreshMinutes, 1, 120);
        s.MinSimilarityPercent = Math.Clamp(s.MinSimilarityPercent, 50, 95);
        s.CaptureWidth = Math.Clamp(s.CaptureWidth, 200, 1920);
        s.CaptureHeight = Math.Clamp(s.CaptureHeight, 100, 1080);
        s.CaptureOffsetX = Math.Clamp(s.CaptureOffsetX, -1500, 1500);
        s.CaptureOffsetY = Math.Clamp(s.CaptureOffsetY, -1500, 1500);
        if (s.ResolutionHeight is not (0 or 720 or 768 or 900 or 1080 or 1200 or 1440 or 1600 or 2160)) s.ResolutionHeight = 0;
        s.AutoHoverDelayMs = Math.Clamp(s.AutoHoverDelayMs, 200, 3000);
        s.OverlaySeconds = Math.Clamp(s.OverlaySeconds, 1, 30);
        s.OverlayOpacityPercent = Math.Clamp(s.OverlayOpacityPercent, 20, 100);
        s.OverlayScalePercent = Math.Clamp(s.OverlayScalePercent, 50, 200);
        s.HistoryWinW = Math.Clamp(s.HistoryWinW, 240, 1600);
        s.HistoryWinH = Math.Clamp(s.HistoryWinH, 120, 1600);
        s.HistoryWinOpacity = Math.Clamp(s.HistoryWinOpacity, 20, 100);
        s.OverlayOrder = OverlayPainter.OrderOf(s).ToArray();
        if (s.ImageSide is not ("Right" or "Top")) s.ImageSide = "Left";
        if (s.UiLanguage != "de") s.UiLanguage = "en";
        s.OffsetX = Math.Clamp(s.OffsetX, -500, 500);
        s.OffsetY = Math.Clamp(s.OffsetY, -500, 500);

        // Loot tiers
        if (s.TierBasis != "Total") s.TierBasis = "PerSlot";
        if (s.TierThresholds == null || s.TierThresholds.Length != 4)
            s.TierThresholds = AppSettings.DefaultThresholds();
        for (int i = 0; i < s.TierThresholds.Length; i++)
            s.TierThresholds[i] = Math.Clamp(s.TierThresholds[i], 0, 1_000_000_000);
        Array.Sort(s.TierThresholds);

        if (s.TierColors == null || s.TierColors.Length != 5)
            s.TierColors = AppSettings.DefaultTierColors();
        for (int i = 0; i < s.TierColors.Length; i++)
            s.TierColors[i] |= opaque;

        // Menu theme
        s.ThemePreset ??= "";
        s.ThemeBackArgb |= opaque;
        s.ThemePanelArgb |= opaque;
        s.ThemeTextArgb |= opaque;
        s.ThemeAccentArgb |= opaque;
        return s;
    }
}

public static class Log
{
    static readonly object Lock = new();

    // force = true always writes (e.g. crashes), otherwise only when the debug log is enabled
    public static void Write(string message, bool force = false)
    {
        if (!force && !SettingsStore.Current.DebugLog) return;
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(SettingsStore.Dir);
                File.AppendAllText(SettingsStore.LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never become an error itself
        }
    }
}

public static class Autostart
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "TarkovAppraiser";
    static readonly string[] LegacyValueNames = { "EFTPriceTooltip", "TarkovPriceOverlay", "LootLens" };   // old names, removed

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, true);
            if (key == null) return;

            foreach (var legacy in LegacyValueNames)
                key.DeleteValue(legacy, false);

            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Autostart: " + ex.Message, true);
        }
    }
}
