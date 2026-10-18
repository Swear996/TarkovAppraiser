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
    public const string Current = "1.6";

    public static readonly ChangelogEntry[] Entries =
    {
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
