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

static class TarkovApi
{
    internal static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovAppraiser/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    public static bool UsedCache { get; private set; }
    public static DateTime CacheTime { get; private set; }

    static string CachePath(AppSettings s) =>
        Path.Combine(SettingsStore.Dir, $"items-{s.ApiLanguage}-{s.GameMode}.json");

    // 3 attempts; if tarkov.dev does not answer, the last saved price list is used
    public static async Task<List<Item>> LoadItemsAsync(AppSettings s)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            // Source 1: json.tarkov.dev (static JSON files)
            try
            {
                var items = await FetchJsonAsync(s);
                SaveCache(s, items);
                UsedCache = false;
                Log.Write($"Prices loaded from json.tarkov.dev ({items.Count} items).");
                return items;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Write($"json.tarkov.dev attempt {attempt}/3 failed: {ex.Message}", true);
            }

            // Source 2: api.tarkov.dev (GraphQL) as fallback
            try
            {
                var items = await FetchAsync(s, attempt > 1 && s.ApiLanguage == "en");
                SaveCache(s, items);
                UsedCache = false;
                Log.Write($"Prices loaded from api.tarkov.dev ({items.Count} items).");
                return items;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Write($"api.tarkov.dev attempt {attempt}/3 failed: {ex.Message}", true);
            }

            if (attempt < 3) await Task.Delay(4000);
        }

        var cached = LoadCache(s);
        if (cached != null)
        {
            UsedCache = true;
            return cached;
        }
        throw last ?? new Exception("Unknown error");
    }

    static void SaveCache(AppSettings s, List<Item> items)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            File.WriteAllText(CachePath(s), JsonSerializer.Serialize(items));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save cache: " + ex.Message, true);
        }
    }

    // The price list saved by the last successful download (null when there is none)
    public static (List<Item> Items, DateTime Time)? LoadSaved(AppSettings s)
    {
        var items = LoadCache(s);
        return items == null ? null : (items, CacheTime);
    }

    static List<Item>? LoadCache(AppSettings s)
    {
        try
        {
            string path = CachePath(s);
            if (!File.Exists(path)) return null;
            var items = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(path));
            if (items == null || items.Count == 0) return null;
            CacheTime = File.GetLastWriteTime(path);
            return items;
        }
        catch (Exception ex)
        {
            Log.Write("Could not read cache: " + ex.Message, true);
            return null;
        }
    }

    // Trader IDs (the item file only contains IDs, no names)
    internal static readonly Dictionary<string, string> TraderNames = new()
    {
        ["54cb50c76803fa8b248b4571"] = "Prapor",
        ["54cb57776803fa99248b456e"] = "Therapist",
        ["58330581ace78e27b8b10cee"] = "Skier",
        ["5935c25fb3acc3127c3d8cd9"] = "Peacekeeper",
        ["5a7c2eca46aef81a7ca2145d"] = "Mechanic",
        ["5ac3b934156ae10c4430e83c"] = "Ragman",
        ["5c0647fdd443bc2504c2d371"] = "Jaeger",
        ["579dc571d53a0658a154fbec"] = "Fence",
        ["638f541a29ffd1183d187f57"] = "Lightkeeper",
        ["6617beeaa9cfa777ca915b7c"] = "Ref"
    };

    internal static string TraderName(string id) =>
        TraderNames.TryGetValue(id, out var n) ? n : "Unknown trader";

    // Downloads the trader portraits once (cached in %AppData%). Failure is harmless:
    // the overlay then simply shows the generated avatars.
    public static async Task LoadTraderPortraitsAsync()
    {
        try
        {
            Directory.CreateDirectory(TraderImages.Dir);
            var missing = TraderNames
                .Where(kv => !File.Exists(Path.Combine(TraderImages.Dir, kv.Key + ".png")))
                .ToList();
            if (missing.Count == 0) return;

            // 1. Get the image links from the API (a failure here is not fatal)
            var links = new Dictionary<string, List<string>>();
            try
            {
                var body = JsonSerializer.Serialize(new { query = "{ traders { id imageLink image4xLink } }" });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await Http.PostAsync("https://api.tarkov.dev/graphql", content);
                string raw = await resp.Content.ReadAsStringAsync();
                resp.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(raw);
                foreach (var t in doc.RootElement.GetProperty("data").GetProperty("traders").EnumerateArray())
                {
                    string id = t.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var list = new List<string>();
                    foreach (var prop in new[] { "imageLink", "image4xLink" })
                        if (t.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrEmpty(u.GetString()))
                            list.Add(u.GetString()!);
                    links[id] = list;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Trader portrait query failed: " + ex.Message, true);
            }

            // 2. Download (API link first, then the fallback URL)
            int ok = 0;
            foreach (var (id, name) in missing)
            {
                var urls = links.TryGetValue(id, out var l) ? new List<string>(l) : new List<string>();
                urls.Add($"https://assets.tarkov.dev/{id}.webp");

                foreach (var url in urls)
                {
                    try
                    {
                        var raw = await Http.GetByteArrayAsync(url);
                        var png = await TraderImages.ToPngAsync(raw);
                        if (png == null) { Log.Write($"Trader image {name}: could not decode {url}", true); continue; }
                        File.WriteAllBytes(Path.Combine(TraderImages.Dir, id + ".png"), png);
                        TraderImages.AddFromPng(name, png);
                        ok++;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Trader image {name} ({url}): {ex.Message}", true);
                    }
                }
            }
            Log.Write($"Trader portraits: {ok}/{missing.Count} downloaded", true);
        }
        catch (Exception ex)
        {
            Log.Write("Could not load trader portraits: " + ex.Message, true);
        }
    }

    // json.tarkov.dev: /{mode}/items (prices) + /{mode}/items_{language} (names)
    static async Task<List<Item>> FetchJsonAsync(AppSettings s)
    {
        string baseUrl = $"https://json.tarkov.dev/{s.GameMode}/";

        // --- Names from the language file ---
        var names = new Dictionary<string, string>();
        using (var nameResp = await Http.GetAsync(baseUrl + "items_" + s.ApiLanguage, HttpCompletionOption.ResponseHeadersRead))
        {
            nameResp.EnsureSuccessStatusCode();
            using var nameStream = await nameResp.Content.ReadAsStreamAsync();
            using var nameDoc = await JsonDocument.ParseAsync(nameStream);

            if (!nameDoc.RootElement.TryGetProperty("data", out var nameData) ||
                nameData.ValueKind != JsonValueKind.Object)
                throw new Exception("Language file contains no data");

            foreach (var entry in nameData.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                if (entry.Name.EndsWith(" Name") || entry.Name.EndsWith(" ShortName"))
                    names[entry.Name] = entry.Value.GetString() ?? "";
            }
        }

        // --- Items with prices ---
        using var resp = await Http.GetAsync(baseUrl + "items", HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("items", out var itemsEl) ||
            itemsEl.ValueKind != JsonValueKind.Object)
            throw new Exception("Item file contains no data");

        var list = new List<Item>();
        var ammo = new Dictionary<string, AmmoStats.Stats>();
        var armor = new Dictionary<string, ItemInfo.Armor>();
        var keys = new Dictionary<string, int>();

        foreach (var prop in itemsEl.EnumerateObject())
        {
            string id = prop.Name;
            var item = prop.Value;
            if (AmmoStats.FromItem(item) is { } st) ammo[id] = st;
            if (ItemInfo.ArmorFromItem(item) is { } ar) armor[id] = ar;
            int keyUses = ItemInfo.KeyUsesFromItem(item);
            if (keyUses > 0) keys[id] = keyUses;

            if (!names.TryGetValue(id + " Name", out var name) || string.IsNullOrWhiteSpace(name))
                continue;
            names.TryGetValue(id + " ShortName", out var shortName);
            shortName ??= "";

            var traders = new List<TraderPrice>();
            if (item.TryGetProperty("sellToTrader", out var sellTo) && sellTo.ValueKind == JsonValueKind.Array)
            {
                foreach (var offer in sellTo.EnumerateArray())
                {
                    long rub = GetLong(offer, "priceRUB");
                    if (rub <= 0) continue;

                    string traderId = offer.TryGetProperty("trader", out var tid) && tid.ValueKind == JsonValueKind.String
                        ? tid.GetString() ?? ""
                        : "";
                    traders.Add(new TraderPrice(TraderName(traderId), rub));
                }
            }
            traders.Sort((a, b) => b.Price.CompareTo(a.Price));

            list.Add(new Item(
                name, shortName, Norm(name), Norm(shortName),
                Math.Max(1, GetInt(item, "width", 1) * GetInt(item, "height", 1)),
                GetLong(item, "avg24hPrice"), GetLong(item, "lastLowPrice"),
                traders, id,
                GetLong(item, "basePrice"), ImageUrl(item), HasType(item, "noFlea"),
                Math.Max(1, GetInt(item, "stackMaxSize", 1))));
        }

        if (list.Count == 0) throw new Exception("No items read");
        AmmoStats.Merge(ammo);
        ItemInfo.MergeFromPriceList(armor, keys);
        Log.Write($"Ammo values in the price list: {ammo.Count}");
        return list;
    }

    static async Task<List<Item>> FetchAsync(AppSettings s, bool omitLang)
    {
        var args = new List<string>();
        if (!omitLang) args.Add("lang: " + s.ApiLanguage);
        if (s.GameMode == "pve") args.Add("gameMode: pve");
        string argText = args.Count > 0 ? "(" + string.Join(", ", args) + ")" : "";

        string query =
            "{ items" + argText + " { id name shortName width height avg24hPrice lastLowPrice basePrice types " +
            "image512pxLink gridImageLink baseImageLink sellFor { priceRUB vendor { name normalizedName } } } }";

        var body = JsonSerializer.Serialize(new { query });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync("https://api.tarkov.dev/graphql", content);
        string raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}: {Truncate(raw)}");

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("items", out var itemsEl) ||
            itemsEl.ValueKind != JsonValueKind.Array)
            throw new Exception("API response contains no data: " + Truncate(raw));

        var list = new List<Item>();

        foreach (var it in itemsEl.EnumerateArray())
        {
            string name = it.GetProperty("name").GetString() ?? "";
            string shortName = it.GetProperty("shortName").GetString() ?? "";
            string itemId = it.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? "" : "";
            int w = GetInt(it, "width", 1);
            int h = GetInt(it, "height", 1);

            var traders = new List<TraderPrice>();
            if (it.TryGetProperty("sellFor", out var sf) && sf.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in sf.EnumerateArray())
                {
                    var vendor = entry.GetProperty("vendor");
                    string norm = vendor.GetProperty("normalizedName").GetString() ?? "";
                    if (norm == "flea-market") continue;

                    long p = GetLong(entry, "priceRUB");
                    if (p > 0)
                        traders.Add(new TraderPrice(vendor.GetProperty("name").GetString() ?? "?", p));
                }
            }
            traders.Sort((a, b) => b.Price.CompareTo(a.Price));

            list.Add(new Item(
                name, shortName, Norm(name), Norm(shortName),
                Math.Max(1, w * h),
                GetLong(it, "avg24hPrice"), GetLong(it, "lastLowPrice"),
                traders, itemId,
                GetLong(it, "basePrice"), ImageUrl(it), HasType(it, "noFlea")));
        }
        return list;
    }

    static string Truncate(string s) => s.Length > 300 ? s[..300] : s;

    static long GetLong(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt64(out long l) ? l : (long)v.GetDouble())
            : 0;

    // Picture addresses of an item, best first, separated by "|" (large picture first; PNG last,
    // readable without a WebP decoder).
    // Weapons: the large picture of the weapon item shows only the bare receiver + grip. The weapon's
    // default preset (the assembled standard weapon) has its own large picture, so that one comes first,
    // then the inventory picture (assembled, but small).
    static string? ImageUrl(JsonElement e)
    {
        var urls = new List<string>();
        bool gun = HasType(e, "gun");
        if (gun && e.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object &&
            props.TryGetProperty("defaultPreset", out var dp) && dp.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(dp.GetString()))
            urls.Add($"https://assets.tarkov.dev/{dp.GetString()}-512.webp");

        var order = gun
            ? new[] { "gridImageLink", "image512pxLink", "baseImageLink" }
            : new[] { "image512pxLink", "gridImageLink", "baseImageLink" };
        foreach (var prop in order)
            if (e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(v.GetString()))
                urls.Add(v.GetString()!);
        return urls.Count > 0 ? string.Join("|", urls) : null;
    }

    static bool HasType(JsonElement e, string type) =>
        e.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array &&
        t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == type);

    static int GetInt(JsonElement e, string prop, int fallback) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    public static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            if (c == '\u00F8') { sb.Append('0'); continue; }   // Windows OCR often reads a zero as "ø" (e.g. "3Ø-round")
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
