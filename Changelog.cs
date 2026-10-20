namespace TarkovPriceOverlay;

public sealed record ChangelogEntry(string Version, string Date, string[] Items);

// ------------------------------------------------------------
//  Changelog shown in Settings > About and used as the GitHub release notes.
//  For EVERY update: add a NEW entry at the TOP, update Current and <Version> in the .csproj.
//  Versions count up with one digit after the point: 1.0, 1.1 ... 1.9, then 2.0.
//  Keep it short and written for players: what is new or better, not how it works inside.
// ------------------------------------------------------------
public static class Changelog
{
    public const string Current = "1.9";

    public static readonly ChangelogEntry[] Entries =
    {
        new("1.9", "2026-10-19", new[]
        {
            "Better item recognition at low resolutions: similar names like M855 / M856, MP-155 / MP-153 or 25- / 20-round magazines are told apart more often.",
            "Items are also recognized when every reading misses a different word of the name (e.g. \"6B13 assault armor (EMR)\").",
            "Stack count: a single round (no number in the corner) now counts as 1 instead of the full stack.",
            "Stack count: numbers with a 0 (e.g. 60) are read again - the game draws the 0 with a slash.",
            "Stack count: numbers that are read clearly are remembered, so at other screen sizes (e.g. 1080p) the program learns the digits by itself and also reads numbers like 80.",
            "A tall panel edge next to the mouse is no longer taken for a tooltip.",
            "Durability (e.g. 112/150) is read again on the item under the mouse - its light grey highlight was taken for text.",
            "The settings, welcome and question windows now follow the Windows scaling (Settings > Display > Scale) - no more tiny windows on large or high-resolution screens.",
            "Messages that are just a bit too long for one line get a wider window instead of a lonely last word.",
            "Settings: in a big or maximized window the pages sit centered with a comfortable width, and the name next to the logo is no longer cut off. The page icons are a bit bigger and clearer."
        }),
        new("1.8", "2026-10-18", new[]
        {
            "New error window: after an unexpected error you can send a report with one click or copy the details. If the program had to close, it offers the report at the next start.",
            "Tarkov Appraiser is now open source under the MIT license.",
            "The message \"already running\" now matches the look of the program.",
            "Welcome window: choose English or German at the top right; \"Let's go\" now opens the settings.",
            "The settings window no longer stays on top of other programs like the Explorer or the browser.",
            "The GitHub button in the settings now shows the GitHub logo."
        }),
        new("1.7", "2026-10-18", new[]
        {
            "Now runs on .NET 10 (support for .NET 8 ends in November 2026). Please install the .NET 10 Desktop Runtime if Windows asks for it: dotnet.microsoft.com/download/dotnet/10.0",
            "Fixed an error message when the program was closed while the settings window was open.",
            "After an update the settings window opens again like at a normal start (unless \"Start minimized\" is on)."
        }),
        new("1.6", "2026-10-18", new[]
        {
            "New update popup at startup: \"New Update Available\" with \"Download Now\" or \"Skip\" - Download Now installs it and restarts, your settings stay.",
            "New website button next to the GitHub button in the settings."
        }),
        new("1.5", "2026-10-18", new[]
        {
            "Updates are shown in the settings window too: \"Update to v…\" under the menu and an Updates card on the About page.",
            "New button \"Check for updates\" on the About page.",
            "Versions are shown as \"1.5\" instead of \"v1.5\"."
        }),
        new("1.4", "2026-10-18", new[]
        {
            "New Statistics page: value checked over all raids, best raid, most valuable finds and most checked items.",
            "Valuable loot stands out: the price box is framed in its tier color (can be switched off under Loot Tiers).",
            "Crafts show the real station (e.g. Workbench 2, Medstation 1) instead of \"Hideout\"."
        }),
        new("1.3", "2026-10-17", new[]
        {
            "The recommendation at the bottom of the price box suggests a barter or craft when it brings more than selling (e.g. \"Craft: Workbench 2 → VOG-17 ×8  +16,028 ₽\")."
        }),
        new("1.2", "2026-10-17", new[]
        {
            "Price box: labels end with a colon (Flea:, Therapist:, Craft: ...)."
        }),
        new("1.1", "2026-10-17", new[]
        {
            "New logo.",
            "\"Flea: banned\" and similar notes line up with the price rows."
        }),
        new("1.0", "2026-10-17", new[]
        {
            "Prices at a glance: hover an item and press F8 - or switch on the automatic mode and just rest the mouse on it.",
            "Flea market (lowest, 24h average, after fee), best trader and where you get more - plus barter and craft tips.",
            "Loot tiers color prices from junk to legendary; ammo is ranked by how good it is (penetration).",
            "Ammo values, armor class, keys with their map - stack counts, armor durability and key uses are read right from the game.",
            "Arrange the price box in the live preview, compact mode, themes and colors.",
            "History of checked items and a raid total, also as a movable list in game (Alt+F8).",
            "FAQ, PvP and PvE prices, many game languages, English and German interface, updates with one click."
        })
    };
}
