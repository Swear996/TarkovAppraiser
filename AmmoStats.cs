using System.Text;
using System.Text.Json;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Ammo values (penetration, damage, armor damage) from tarkov.dev.
//  Loaded once in the background and saved, so they are there right away on the next start.
// ------------------------------------------------------------
static class AmmoStats
{
    public sealed record Stats(int Damage, int Penetration, int ArmorDamage, int Pellets);

    static volatile Dictionary<string, Stats> _map = new();

    // raised (on the calling thread) when new values are available, so the preview can redraw
    public static event Action? Loaded;

    public static Stats? Get(string id) =>
        id.Length > 0 && _map.TryGetValue(id, out var s) ? s : null;

    static string CacheFile(string mode) => Path.Combine(SettingsStore.Dir, $"ammo-{mode}.json");

    public static async Task LoadAsync(AppSettings s)
    {
        string mode = s.GameMode == "pve" ? "pve" : "regular";
        try
        {
            string file = CacheFile(mode);
            if (File.Exists(file))
            {
                var cached = Parse(await File.ReadAllTextAsync(file));
                Merge(cached);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Ammo values (saved): " + ex.Message);
        }

        try
        {
            string args = mode == "pve" ? "(gameMode: pve)" : "";
            string query = "{ ammo" + args + " { item { id } damage penetrationPower armorDamage projectileCount } }";
            using var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
            using var resp = await TarkovApi.Http.PostAsync("https://api.tarkov.dev/graphql", content);
            string raw = await resp.Content.ReadAsStringAsync();
            var map = Parse(raw);
            if (map.Count == 0) throw new Exception("no ammo in the answer: " + (raw.Length > 200 ? raw[..200] : raw));
            Merge(map);
            Directory.CreateDirectory(SettingsStore.Dir);
            await File.WriteAllTextAsync(CacheFile(mode), raw);
            Log.Write($"Ammo values: {map.Count} loaded");
        }
        catch (Exception ex)
        {
            Log.Write("Ammo values could not be loaded: " + ex.Message, true);
        }
    }

    // values from the price list itself (json.tarkov.dev sends them with the ammo items)
    public static void Merge(Dictionary<string, Stats> values)
    {
        if (values.Count == 0) return;
        var map = new Dictionary<string, Stats>(_map);
        foreach (var (id, st) in values) map[id] = st;
        _map = map;
        Loaded?.Invoke();
    }

    // ammo values inside an item of the price list ("properties"), null when it is no ammo
    public static Stats? FromItem(JsonElement item)
    {
        if (!item.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        if (!p.TryGetProperty("penetrationPower", out _) || !p.TryGetProperty("damage", out _)) return null;
        return new Stats(Int(p, "damage"), Int(p, "penetrationPower"), Int(p, "armorDamage"), Math.Max(1, Int(p, "projectileCount")));
    }

    static Dictionary<string, Stats> Parse(string raw)
    {
        var map = new Dictionary<string, Stats>();
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("ammo", out var list) || list.ValueKind != JsonValueKind.Array)
            return map;
        foreach (var a in list.EnumerateArray())
        {
            if (!a.TryGetProperty("item", out var item) || !item.TryGetProperty("id", out var idEl) ||
                idEl.ValueKind != JsonValueKind.String) continue;
            map[idEl.GetString()!] = new Stats(Int(a, "damage"), Int(a, "penetrationPower"),
                                               Int(a, "armorDamage"), Math.Max(1, Int(a, "projectileCount")));
        }
        return map;
    }

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt32(out int i) ? i : (int)Math.Round(v.GetDouble()))
            : 0;
}
