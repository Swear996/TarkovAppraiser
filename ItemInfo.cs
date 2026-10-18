using System.Text;
using System.Text.Json;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Extra item facts from tarkov.dev (besides prices):
//  - armor class + durability (armor, helmets, rigs, plates)
//  - keys: number of uses and the maps they are for
//  - barters and crafts that use an item (to tell when trading it in is worth more than selling)
//  Loaded once in the background and saved, so they are there right away on the next start.
// ------------------------------------------------------------
static class ItemInfo
{
    public sealed record Armor(int Class, int Durability);

    // Kind: "Barter" / "Craft"; Where: "Therapist 2" / "Medstation 2"
    public sealed record Use(string Kind, string Where, string RewardId, int RewardCount, List<(string Id, int Count)> Required);

    static volatile Dictionary<string, Armor> _armor = new();
    static volatile Dictionary<string, int> _keyUses = new();
    static volatile Dictionary<string, List<string>> _keyMaps = new();
    static volatile Dictionary<string, List<Use>> _barters = new();
    static volatile Dictionary<string, List<Use>> _crafts = new();

    // raised when new facts are available (the windows redraw)
    public static event Action? Loaded;

    public static Armor? ArmorOf(string id) => id.Length > 0 && _armor.TryGetValue(id, out var a) ? a : null;
    public static int KeyUses(string id) => id.Length > 0 && _keyUses.TryGetValue(id, out var n) ? n : 0;
    public static IReadOnlyList<string> KeyMaps(string id) =>
        id.Length > 0 && _keyMaps.TryGetValue(id, out var m) ? m : Array.Empty<string>();
    public static IReadOnlyList<Use> UsesOf(string id)
    {
        if (id.Length == 0) return Array.Empty<Use>();
        var b = _barters.TryGetValue(id, out var x) ? x : null;
        var c = _crafts.TryGetValue(id, out var y) ? y : null;
        if (b == null) return c ?? (IReadOnlyList<Use>)Array.Empty<Use>();
        return c == null ? b : b.Concat(c).ToList();
    }

    // ---- from the price list (json.tarkov.dev sends the item properties along) ----

    public static Armor? ArmorFromItem(JsonElement item)
    {
        if (!item.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        int cls = Int(p, "class");
        return cls > 0 ? new Armor(cls, Int(p, "durability")) : null;
    }

    // only for keys (medical items have "uses" too)
    public static int KeyUsesFromItem(JsonElement item, bool mustBeKey = true) =>
        (!mustBeKey || IsKey(item)) &&
        item.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object &&
        p.TryGetProperty("uses", out _) ? Int(p, "uses") : 0;

    static bool IsKey(JsonElement item) =>
        item.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array &&
        t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "keys");

    public static void MergeFromPriceList(Dictionary<string, Armor> armor, Dictionary<string, int> keyUses)
    {
        if (armor.Count > 0) _armor = Merged(_armor, armor);
        if (keyUses.Count > 0) _keyUses = Merged(_keyUses, keyUses);
        if (armor.Count + keyUses.Count > 0) Loaded?.Invoke();
    }

    static Dictionary<string, T> Merged<T>(Dictionary<string, T> old, Dictionary<string, T> add)
    {
        var map = new Dictionary<string, T>(old);
        foreach (var (k, v) in add) map[k] = v;
        return map;
    }

    // ---- from the tarkov.dev API: maps (keys), barters, crafts; armor and keys as a reserve ----

    static string CacheFile(string name, AppSettings s) =>
        Path.Combine(SettingsStore.Dir, $"{name}-{(s.GameMode == "pve" ? "pve" : "regular")}-{s.ApiLanguage}.json");

    public static async Task LoadAsync(AppSettings s)
    {
        string args = "(lang: " + s.ApiLanguage + (s.GameMode == "pve" ? ", gameMode: pve" : "") + ")";

        // one request per part: a big combined request was refused by tarkov.dev ("GraphQL server unavailable"),
        // and then the key maps were missing ("unknown map")
        string maps = "{ maps" + args + " { name locks { key { id } } } }";
        string barters = "{ barters" + args + " { trader { name } level requiredItems { item { id } count } rewardItems { item { id } count } } }";
        string crafts = "{ crafts" + args + " { station { name } level requiredItems { item { id } count } rewardItems { item { id } count } } }";
        // saved answer of older versions (maps, barters and crafts in one): used until the new parts are there
        try
        {
            string old = CacheFile("item-uses", s);
            if (File.Exists(old))
            {
                using var cached = JsonDocument.Parse(await File.ReadAllTextAsync(old));
                if (cached.RootElement.TryGetProperty("data", out var d))
                {
                    ParseMaps(d);
                    if (ParseUses(d, "barters", "Barter", "trader") is { } b) _barters = b;
                    if (ParseUses(d, "crafts", "Craft", "station") is { } c) _crafts = c;
                    Loaded?.Invoke();
                }
            }
        }
        catch (Exception ex) { Log.Write("Item info (old saved): " + ex.Message); }

        var parts = new List<Task>
        {
            LoadFromJsonAsync(s),   // static files: work even when the GraphQL API is busy
            LoadPartAsync("item-maps", maps, s, ParseMaps),
            LoadPartAsync("item-barters", barters, s, d => ParseUses(d, "barters", "Barter", "trader") is { } u && (_barters = u) != null),
            LoadPartAsync("item-crafts", crafts, s, d => ParseUses(d, "crafts", "Craft", "station") is { } u && (_crafts = u) != null),
        };

        string gear = "{ items(types: [armor, helmet, rig, armorPlate, keys]" +
                      (s.GameMode == "pve" ? ", gameMode: pve" : "") + ") { id properties { " +
                      "... on ItemPropertiesArmor { class durability } " +
                      "... on ItemPropertiesHelmet { class durability } " +
                      "... on ItemPropertiesChestRig { class durability } " +
                      "... on ItemPropertiesArmorAttachment { class durability } " +
                      "... on ItemPropertiesKey { uses } } } }";
        parts.Add(LoadPartAsync("item-gear", gear, s, ParseGear));
        await Task.WhenAll(parts);   // side by side: a part that is retried does not hold up the others
    }

    static async Task LoadPartAsync(string name, string query, AppSettings s, Func<JsonElement, bool> parse)
    {
        string file = CacheFile(name, s);
        try
        {
            if (File.Exists(file))
            {
                using var cached = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                if (cached.RootElement.TryGetProperty("data", out var d) && parse(d)) Loaded?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Item info '{name}' (saved): " + ex.Message);
        }

        // tarkov.dev is sometimes busy: tried again after 30 s, 2 min and 5 min
        int[] waitSeconds = { 0, 30, 120, 300 };
        for (int attempt = 0; attempt < waitSeconds.Length; attempt++)
        {
            if (waitSeconds[attempt] > 0) await Task.Delay(TimeSpan.FromSeconds(waitSeconds[attempt]));
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
                using var resp = await TarkovApi.Http.PostAsync("https://api.tarkov.dev/graphql", content);
                string raw = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !parse(data))
                    throw new Exception(raw.Length > 300 ? raw[..300] : raw);
                Directory.CreateDirectory(SettingsStore.Dir);
                await File.WriteAllTextAsync(file, raw);
                Loaded?.Invoke();
                Log.Write($"Item info '{name}' loaded");
                return;
            }
            catch (Exception ex)
            {
                Log.Write($"Item info '{name}' could not be loaded (try {attempt + 1}): " + ex.Message, attempt == waitSeconds.Length - 1);
            }
        }
    }

    // ---- json.tarkov.dev (static files, the same source as the prices) ----
    // The GraphQL API answered "GraphQL server unavailable" for hours; the static files kept working.

    static string KeyMapsFile(AppSettings s) => CacheFile("key-maps", s);

    static async Task LoadFromJsonAsync(AppSettings s)
    {
        string baseUrl = $"https://json.tarkov.dev/{s.GameMode}/";

        // saved key maps of the last start, right away
        try
        {
            if (File.Exists(KeyMapsFile(s)) &&
                JsonSerializer.Deserialize<Dictionary<string, List<string>>>(await File.ReadAllTextAsync(KeyMapsFile(s))) is { Count: > 0 } saved
                && _keyMaps.Count == 0)
            {
                _keyMaps = saved;
                Loaded?.Invoke();
            }
        }
        catch (Exception ex) { Log.Write("Key maps (saved): " + ex.Message); }

        try
        {
            var names = await TranslationsAsync(baseUrl + "maps_" + s.ApiLanguage);
            using var doc = await GetJsonAsync(baseUrl + "maps");
            var keyMaps = new Dictionary<string, List<string>>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var maps = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("maps", out var mp) ? mp : data;
                IEnumerable<JsonElement> list = maps.ValueKind switch
                {
                    JsonValueKind.Object => maps.EnumerateObject().Select(p => p.Value),
                    JsonValueKind.Array => maps.EnumerateArray(),
                    _ => Array.Empty<JsonElement>()
                };
                foreach (var m in list)
                {
                    if (m.ValueKind != JsonValueKind.Object) continue;
                    string raw = Str(m, "name");
                    string map = names.TryGetValue(raw, out var tr) ? tr : raw.EndsWith(" Name") ? Str(m, "normalizedName") : raw;
                    if (map.Length == 0 || !m.TryGetProperty("locks", out var locks) || locks.ValueKind != JsonValueKind.Array) continue;
                    foreach (var l in locks.EnumerateArray())
                    {
                        string id = l.ValueKind == JsonValueKind.Object && l.TryGetProperty("key", out var k) ? Ref(k) : "";
                        if (id.Length == 0) continue;
                        if (!keyMaps.TryGetValue(id, out var ml)) keyMaps[id] = ml = new List<string>();
                        if (!ml.Contains(map)) ml.Add(map);
                    }
                }
            }
            if (keyMaps.Count == 0) throw new Exception("no locks in the map file");
            _keyMaps = keyMaps;
            Loaded?.Invoke();
            Directory.CreateDirectory(SettingsStore.Dir);
            await File.WriteAllTextAsync(KeyMapsFile(s), JsonSerializer.Serialize(keyMaps));
            Log.Write($"Key maps (json.tarkov.dev): {keyMaps.Count} keys");
        }
        catch (Exception ex) { Log.Write("Key maps (json.tarkov.dev): " + ex.Message, true); }

        // barters and crafts, only when the API did not deliver them
        try
        {
            using var doc = await GetJsonAsync(baseUrl + "barters");
            var uses = new Dictionary<string, List<Use>>();
            foreach (var b in DataList(doc.RootElement, "barters"))
                AddUse(uses, "Barter", (TarkovApi.TraderName(Ref(Prop(b, "trader"))) + " " + Int(b, "minTraderLevel")).Trim(),
                       Prop(b, "offeredItem"), b);
            if (uses.Count > 0 && _barters.Count == 0) { _barters = uses; Loaded?.Invoke(); }
        }
        catch (Exception ex) { Log.Write("Barters (json.tarkov.dev): " + ex.Message); }

        try
        {
            var stations = await StationNamesAsync(baseUrl, s.ApiLanguage);
            using var doc = await GetJsonAsync(baseUrl + "crafts");
            var uses = new Dictionary<string, List<Use>>();
            foreach (var c in DataList(doc.RootElement, "crafts"))
            {
                string st = Ref(Prop(c, "station"));
                string name = stations.TryGetValue(st, out var n) ? n : L.T("Hideout");
                AddUse(uses, "Craft", (name + " " + Int(c, "level")).Trim(), Prop(c, "productItem"), c);
            }
            if (uses.Count > 0 && _crafts.Count == 0) { _crafts = uses; Loaded?.Invoke(); }
        }
        catch (Exception ex) { Log.Write("Crafts (json.tarkov.dev): " + ex.Message); }
    }

    // station id -> name ("Workbench", "Medstation"): the hideout file names each station by a translation key
    // ("hideout_area_10_name"), the language file has the text; without it the station's normalized name is used
    static async Task<Dictionary<string, string>> StationNamesAsync(string baseUrl, string lang)
    {
        var names = new Dictionary<string, string>();
        try
        {
            var texts = await TranslationsAsync(baseUrl + "hideout_" + lang);
            using var doc = await GetJsonAsync(baseUrl + "hideout");
            foreach (var st in DataList(doc.RootElement, "hideoutStations"))
            {
                string id = Str(st, "id");
                if (id.Length == 0) continue;
                string key = Str(st, "name");
                string name = texts.TryGetValue(key, out var t) && t.Length > 0 ? t : Pretty(Str(st, "normalizedName"));
                if (name.Length > 0) names[id] = name;
            }
        }
        catch (Exception ex) { Log.Write("Hideout stations (json.tarkov.dev): " + ex.Message); }
        return names;
    }

    // "intelligence-center" -> "Intelligence Center"
    static string Pretty(string normalized) =>
        string.Join(" ", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    static void AddUse(Dictionary<string, List<Use>> uses, string kind, string where, JsonElement reward, JsonElement entry)
    {
        string rewardId = reward.ValueKind == JsonValueKind.Object ? Ref(Prop(reward, "item")) : "";
        if (rewardId.Length == 0 || !entry.TryGetProperty("requiredItems", out var reqs) || reqs.ValueKind != JsonValueKind.Array) return;
        var req = new List<(string Id, int Count)>();
        foreach (var r in reqs.EnumerateArray())
        {
            string id = r.ValueKind == JsonValueKind.Object ? Ref(Prop(r, "item")) : "";
            if (id.Length > 0) req.Add((id, Math.Max(1, Int(r, "count"))));
        }
        if (req.Count == 0) return;
        var use = new Use(kind, where, rewardId, Math.Max(1, Int(reward, "count")), req);
        foreach (var (id, _) in req)
        {
            if (!uses.TryGetValue(id, out var l)) uses[id] = l = new List<Use>();
            l.Add(use);
        }
    }

    static IEnumerable<JsonElement> DataList(JsonElement root, string name)
    {
        if (!root.TryGetProperty("data", out var data)) return Array.Empty<JsonElement>();
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var inner)) data = inner;
        return data.ValueKind switch
        {
            JsonValueKind.Array => data.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList(),
            JsonValueKind.Object => data.EnumerateObject().Select(p => p.Value).Where(e => e.ValueKind == JsonValueKind.Object).ToList(),
            _ => Array.Empty<JsonElement>()
        };
    }

    static JsonElement Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    // an id given as "5448ba..." or as { "id": "5448ba..." }
    static string Ref(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" :
        e.ValueKind == JsonValueKind.Object ? Str(e, "id") : "";

    static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var resp = await TarkovApi.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    static async Task<Dictionary<string, string>> TranslationsAsync(string url)
    {
        var map = new Dictionary<string, string>();
        try
        {
            using var doc = await GetJsonAsync(url);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                foreach (var p in data.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) { Log.Write("Translations " + url + ": " + ex.Message); }
        return map;
    }

    static bool ParseMaps(JsonElement data)
    {
        if (!data.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array) return false;
        var keyMaps = new Dictionary<string, List<string>>();
        foreach (var m in maps.EnumerateArray())
        {
            string map = Str(m, "name");
            if (map.Length == 0 || !m.TryGetProperty("locks", out var locks) || locks.ValueKind != JsonValueKind.Array) continue;
            foreach (var l in locks.EnumerateArray())
            {
                if (!l.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Object) continue;
                string id = Str(key, "id");
                if (id.Length == 0) continue;
                if (!keyMaps.TryGetValue(id, out var list)) keyMaps[id] = list = new List<string>();
                if (!list.Contains(map)) list.Add(map);
            }
        }
        if (keyMaps.Count == 0) return false;
        _keyMaps = keyMaps;
        Log.Write($"Key maps: {keyMaps.Count} keys on {maps.GetArrayLength()} maps");
        return true;
    }

    // barters (source: trader) or crafts (source: station) that need an item
    static Dictionary<string, List<Use>>? ParseUses(JsonElement data, string prop, string kind, string sourceProp)
    {
        if (!data.TryGetProperty(prop, out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var uses = new Dictionary<string, List<Use>>();
        foreach (var b in list.EnumerateArray())
        {
            string where = b.TryGetProperty(sourceProp, out var src) && src.ValueKind == JsonValueKind.Object ? Str(src, "name") : "";
            where = (where + " " + Int(b, "level")).Trim();
            var req = Items(b, "requiredItems");
            var reward = Items(b, "rewardItems");
            if (req.Count == 0 || reward.Count == 0) continue;
            var use = new Use(kind, where, reward[0].Id, Math.Max(1, reward[0].Count), req);
            foreach (var (id, _) in req)
            {
                if (!uses.TryGetValue(id, out var l)) uses[id] = l = new List<Use>();
                l.Add(use);
            }
        }
        return uses.Count > 0 ? uses : null;
    }

    static bool ParseGear(JsonElement data)
    {
        if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
        var armor = new Dictionary<string, Armor>();
        var keys = new Dictionary<string, int>();
        foreach (var it in items.EnumerateArray())
        {
            string id = Str(it, "id");
            if (id.Length == 0) continue;
            if (ArmorFromItem(it) is { } a) armor[id] = a;
            int usesLeft = KeyUsesFromItem(it, mustBeKey: false);   // the query asks for keys only
            if (usesLeft > 0) keys[id] = usesLeft;
        }
        if (armor.Count > 0) _armor = Merged(_armor, armor);
        if (keys.Count > 0) _keyUses = Merged(_keyUses, keys);
        return armor.Count + keys.Count > 0;
    }

    static List<(string Id, int Count)> Items(JsonElement e, string prop)
    {
        var list = new List<(string, int)>();
        if (!e.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var x in arr.EnumerateArray())
        {
            if (!x.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;
            string id = Str(item, "id");
            if (id.Length > 0) list.Add((id, Math.Max(1, Int(x, "count"))));
        }
        return list;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt32(out int i) ? i : (int)Math.Round(v.GetDouble()))
            : 0;
}
