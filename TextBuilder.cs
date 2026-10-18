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

static class Tiers
{
    // The "main" price of an item: lowest flea offer, else 24h average, else best trader
    public static long Value(Item i) =>
        i.LastLow > 0 ? i.LastLow :
        i.Avg24h > 0 ? i.Avg24h :
        i.Traders.Count > 0 ? i.Traders[0].Price : 0;

    // value of what fills one cell: a full stack for ammo and other stackable items
    public static long CellValue(Item i) => Value(i) * Math.Max(1, i.StackMax);

    public static int TierOf(long total, int slots, AppSettings s)
    {
        long v = s.TierBasis == "Total" ? total : total / Math.Max(1, slots);
        int tier = 0;
        for (int k = 0; k < s.TierThresholds.Length; k++)
            if (v >= s.TierThresholds[k]) tier = k + 1;
        return tier;
    }

    // ammo by penetration (about the armor class it gets through): <20 junk, 20+ common, 30+ good, 40+ rare, 50+ legendary
    public static int OfAmmo(int penetration) =>
        penetration >= 50 ? 4 : penetration >= 40 ? 3 : penetration >= 30 ? 2 : penetration >= 20 ? 1 : 0;

    public static Color ColorOf(int tier, AppSettings s) =>
        Themes.FromArgb(s.TierColors[Math.Clamp(tier, 0, s.TierColors.Length - 1)]);
}

static class TextBuilder
{
    public static string Money(long v) => v.ToString("N0", CultureInfo.InvariantCulture) + " ₽";

    const int MaxNameLength = 30;

    // Long item names: use the short name when it is shorter, otherwise cut off with "..."
    public static string DisplayName(Item i)
    {
        string name = i.Name;
        if (name.Length <= MaxNameLength) return name;

        string sn = i.Short?.Trim() ?? "";
        if (sn.Length > 0 && sn.Length < name.Length)
            name = sn;
        if (name.Length <= MaxNameLength) return name;

        return name.Substring(0, MaxNameLength - 1).TrimEnd() + "\u2026";
    }

    // Content of the overlay (drawn by OverlayPainter.cs)
    // stackCount: how many are in the cell (read or typed in); null = unknown -> full stack
    public static OverlayModel BuildModel(Item i, AppSettings s, int? stackCount = null, (int Cur, int Max)? durability = null)
    {
        var text = Themes.FromArgb(s.TextColorArgb);
        if (stackCount == null && ReferenceEquals(i, MainForm.Demo)) stackCount = 3;   // example item: show the stack line
        int? count = stackCount is int sc && sc >= 1 && i.StackMax > 1 ? Math.Min(sc, i.StackMax) : null;
        long cellValue = count is int cnt ? Tiers.Value(i) * cnt : Tiers.CellValue(i);
        int priceTier = Tiers.TierOf(cellValue, i.Slots, s);
        // ammo: the tier says how good the round is (penetration); the price keeps the color of what it sells for
        var ammo = AmmoStats.Get(i.Id);
        bool ammoTier = s.AmmoTierByPen && ammo != null;
        int tier = ammoTier ? Tiers.OfAmmo(ammo!.Penetration) : priceTier;
        var m = new OverlayModel
        {
            Name = DisplayName(i),
            Slots = i.Slots,
            UseTier = s.UseTiers,
            Tier = s.UseTiers ? Tiers.ColorOf(tier, s) : text,
            PriceColor = s.UseTiers ? Tiers.ColorOf(priceTier, s) : text,
            TierName = s.UseTiers && s.TierShowLabel ? L.T(AppSettings.TierNames[tier]) : null,
            ColorName = s.UseTiers && s.TierColorName,
            Frame = s.UseTiers && s.TierFrame && tier > 0
        };

        // not worth the space: dimmed, always labeled "Junk"
        if (s.DimJunk && tier == 0 && (ammoTier || cellValue > 0))
        {
            m.Junk = true;
            m.TierName = L.T(AppSettings.TierNames[0]);
        }

        // ammo: penetration, damage (x pellets for buckshot), armor damage
        if (s.ShowAmmoStats && AmmoStats.Get(i.Id) is { } a)
            m.Infos.Add(new InfoLine("ammo", L.T("Ammo"),
                L.T("Pen") + " " + a.Penetration + "  \u00B7  " +
                L.T("Dmg") + " " + a.Damage + (a.Pellets > 1 ? "\u00D7" + a.Pellets : "") + "  \u00B7  " +
                L.T("Armor") + " " + a.ArmorDamage + " %"));

        // armor, helmets, rigs, plates: protection class + durability. The real durability of this piece (e.g. 179/190)
        // is read from the game; otherwise the value of a new item with its default plates is shown, marked "new".
        if (s.ShowArmor && ItemInfo.ArmorOf(i.Id) is { } ar)
            m.Infos.Add(new InfoLine("armor", L.T("Armor"),
                string.Format(L.T("Class {0}"), ar.Class) +
                (durability is { } dur
                    ? "  \u00B7  " + string.Format(L.T("{0} durability"), $"{dur.Cur}/{dur.Max}")   // read from the game: this piece
                    : ar.Durability > 0 ? "  \u00B7  " + string.Format(L.T("{0} durability (new)"), ar.Durability) : "")));

        // keys: where they are used + how often
        if (s.ShowKeyInfo)
        {
            var maps = ItemInfo.KeyMaps(i.Id);
            int keyUses = ItemInfo.KeyUses(i.Id);
            if (maps.Count > 0 || keyUses > 0)
            {
                string keyText = maps.Count > 0 ? string.Join(", ", maps) : L.T("unknown map");
                // uses left of this key, read from the game (same as the armor durability); otherwise the uses of a new key
                if (durability is { } left && ItemInfo.ArmorOf(i.Id) == null)
                    keyText += "  \u00B7  " + string.Format(L.T("{0} uses"), $"{left.Cur}/{left.Max}");
                else if (keyUses > 0) keyText += "  \u00B7  " + string.Format(L.T("{0} uses (new)"), keyUses);
                m.Infos.Add(new InfoLine("key", L.T("Key"), keyText));
            }
        }


        bool perSlot = s.ShowPerSlot && i.Slots > 1;
        string? Slot(long v) => perSlot ? Money(v / i.Slots) + L.T("/slot") : null;

        long fleaNet = Flea.Net(i, s);
        if (s.ShowFleaLow)
        {
            if (i.NoFlea) m.Notes.Add(new PriceNote("flea", L.T("Flea: banned")));
            else if (i.LastLow > 0) m.Rows.Add(new PriceRow("flea", L.T("Flea"), i.LastLow, Slot(i.LastLow), null));
            else m.Notes.Add(new PriceNote("flea", L.T("Flea: no offers")));
        }
        if (s.ShowFleaNet && fleaNet > 0)
            m.Rows.Add(new PriceRow("net", L.T("After fee"), fleaNet, "\u2212" + Money(i.LastLow - fleaNet) + " " + L.T("fee"), null));
        if (s.ShowFleaAvg && i.Avg24h > 0)
            m.Rows.Add(new PriceRow("avg", L.T("Avg 24h"), i.Avg24h, Slot(i.Avg24h), null));

        var traders = s.ShowAllTraders ? i.Traders : s.ShowBestTrader ? i.Traders.Take(1).ToList() : new List<TraderPrice>();
        if ((s.ShowAllTraders || s.ShowBestTrader) && i.Traders.Count == 0)
            m.Notes.Add(new PriceNote("trader", L.T("Trader: not sellable")));
        foreach (var t in traders)
            m.Rows.Add(new PriceRow("trader", t.Vendor, t.Price, Slot(t.Price),
                s.ShowTraderImages && TraderImages.Has(t.Vendor) ? t.Vendor : null));

        long unit = Tiers.Value(i);
        if (count is int n && unit > 0)
            m.Rows.Add(new PriceRow("stack", L.T("Stack") + $" ({n.ToString("N0", CultureInfo.InvariantCulture)})", unit * n, null, null));

        // barter / craft that pays more than selling: shown in the recommendation bar (or as a line when the bar is off)
        var use = s.ShowUses ? BestUse(i, s) : null;
        if (use is { } u0 && !s.ShowBestOption)
            m.Infos.Add(new InfoLine("use", L.T(u0.Kind), u0.Text + "  \u2248 " + Money(u0.Share), Color.FromArgb(232, 190, 90)));

        // example item of the live preview: every fact line with sample values
        if (ReferenceEquals(i, MainForm.Demo))
        {
            m.Name = L.T("Example item");
            m.Infos.Clear();   // only the sample lines below (no real data of the item behind the picture)
            if (s.ShowAmmoStats)
                m.Infos.Add(new InfoLine("ammo", L.T("Ammo"), L.T("Pen") + " 42  \u00B7  " + L.T("Dmg") + " 50  \u00B7  " + L.T("Armor") + " 48 %"));
            if (s.ShowArmor)
                m.Infos.Add(new InfoLine("armor", L.T("Armor"), string.Format(L.T("Class {0}"), 4) + "  \u00B7  " + string.Format(L.T("{0} durability"), "65/80")));
            if (s.ShowKeyInfo)
                m.Infos.Add(new InfoLine("key", L.T("Key"), "Customs, Factory  \u00B7  " + string.Format(L.T("{0} uses"), 40)));
            if (s.ShowUses)
                m.Infos.Add(new InfoLine("use", L.T("Barter"), "Therapist 2 \u2192 Salewa  \u2248 " + Money(52000), Color.FromArgb(232, 190, 90)));
        }

        if (s.ShowBestOption && use is { } u)
        {
            // trading it in brings more than selling: "Craft: Hideout 2 → VOG-17 ×8   +16,028 ₽"
            long sell = Math.Max(fleaNet, i.Traders.Count > 0 ? i.Traders[0].Price : 0);
            m.BestTitle = u.Kind;
            m.BestText = L.T(u.Kind) + ": " + u.Text;
            m.BestDetail = sell > 0 ? "+" + Money(u.Share - sell) : "\u2248 " + Money(u.Share);
        }
        else if (s.ShowBestOption && fleaNet > 0 && i.Traders.Count > 0)
        {
            var trader = i.Traders[0];
            bool flea = fleaNet > trader.Price;
            m.BestTitle = flea ? "Flea market" : trader.Vendor;
            m.BestTrader = !flea && s.ShowTraderImages && TraderImages.Has(trader.Vendor) ? trader.Vendor : null;
            m.BestDetail = $"+{Money(Math.Abs(fleaNet - trader.Price))} vs. {(flea ? trader.Vendor : "flea")}";
        }

        if (s.OverlayCompact) Compact(m, i, s, fleaNet);

        // prices are old (tarkov.dev unreachable for a while): say so, the numbers may be off
        if (!ReferenceEquals(i, MainForm.Demo) && PriceState.Updated is DateTime upd &&
            (PriceState.FromCache || DateTime.Now - upd > TimeSpan.FromMinutes(Math.Max(60, s.PriceRefreshMinutes * 3))))
            m.Infos.Add(new InfoLine("stale", L.T("Prices"), PriceState.Ago(upd) + (PriceState.FromCache ? "  \u00B7  " + L.T("saved") : ""),
                Color.FromArgb(224, 165, 38)));
        return m;
    }

    // Compact mode: only the best way to sell (and a counted stack); everything else is left out
    static void Compact(OverlayModel m, Item i, AppSettings s, long fleaNet)
    {
        var stack = m.Rows.Where(r => r.Key == "stack").ToList();
        m.Rows.Clear();
        m.Notes.Clear();
        m.Infos.Clear();
        m.BestTitle = m.BestTrader = m.BestDetail = m.BestText = null;

        var trader = i.Traders.Count > 0 ? i.Traders[0] : null;
        if (fleaNet > 0 && (trader == null || fleaNet >= trader.Price))
            m.Rows.Add(new PriceRow("flea", L.T("Flea"), fleaNet, null, null));
        else if (trader != null)
            m.Rows.Add(new PriceRow("trader", trader.Vendor, trader.Price, null,
                s.ShowTraderImages && TraderImages.Has(trader.Vendor) ? trader.Vendor : null));
        else
            m.Notes.Add(new PriceNote("flea", L.T("Not sellable")));
        m.Rows.AddRange(stack);
    }

    // The barter / craft where this item is worth clearly more than selling it.
    // The item gets its share of the reward by value: reward value x (its value / value of everything required).
    // Text: "Hideout 2 → VOG-17 ×8"; Share: what one piece of this item is worth in it
    static (string Kind, string Text, long Share)? BestUse(Item i, AppSettings s)
    {
        var uses = ItemInfo.UsesOf(i.Id);
        if (uses.Count == 0) return null;
        long own = Tiers.Value(i);
        long sell = Math.Max(Flea.Net(i, s), i.Traders.Count > 0 ? i.Traders[0].Price : 0);
        if (sell <= 0) sell = own;
        if (own <= 0) return null;

        ItemInfo.Use? best = null;
        long bestShare = 0;
        foreach (var u in uses)
        {
            var reward = MainForm.ById(u.RewardId);
            if (reward == null) continue;
            long rewardValue = Tiers.Value(reward) * u.RewardCount;
            long required = 0;
            int mine = 0;
            bool unknown = false;
            foreach (var (id, count) in u.Required)
            {
                var r = id == i.Id ? i : MainForm.ById(id);
                if (r == null || Tiers.Value(r) <= 0) { unknown = true; break; }
                required += Tiers.Value(r) * count;
                if (id == i.Id) mine += count;
            }
            if (unknown || required <= 0 || mine == 0 || rewardValue <= 0) continue;
            long share = (long)((double)rewardValue * own / required);   // per piece of this item
            if (share > bestShare) { bestShare = share; best = u; }
        }
        if (best == null || bestShare < sell * 1.15) return null;   // not clearly better
        string name = MainForm.ById(best.RewardId)?.Short is { Length: > 0 } sn ? sn : MainForm.ById(best.RewardId)?.Name ?? "?";
        return (best.Kind, $"{best.Where} \u2192 {name}" + (best.RewardCount > 1 ? $" \u00D7{best.RewardCount}" : ""), bestShare);
    }
}
