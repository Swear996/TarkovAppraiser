using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Item pictures for the overlay (from tarkov.dev).
//  Downloaded on demand when an item is recognized, then cached in
//  %AppData%\TarkovPriceOverlay\items (192 px PNG) and in memory.
//  If no picture is available, the overlay is simply shown without one.
// ------------------------------------------------------------
static class ItemImages
{
    const int Px = 192;

    static readonly object Lock = new();
    static readonly Dictionary<string, Bitmap> Mem = new();
    static readonly Dictionary<string, Task<Image?>> Pending = new();
    static readonly HashSet<string> Failed = new();

    static string Dir => Path.Combine(SettingsStore.Dir, "items");

    // Picture only when it is already in memory (no waiting)
    public static Image? TryGet(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (Lock) return Mem.TryGetValue(id, out var b) ? b : null;
    }

    // Never throws; returns null when no picture is available.
    // knownUrl: the picture addresses from the price list, best first, separated by "|" (tried first)
    public static Task<Image?> GetAsync(string id, string? knownUrl = null)
    {
        if (string.IsNullOrEmpty(id)) return Task.FromResult<Image?>(null);
        lock (Lock)
        {
            if (Mem.TryGetValue(id, out var b)) return Task.FromResult<Image?>(b);
            if (Failed.Contains(id)) return Task.FromResult<Image?>(null);
            if (!Pending.TryGetValue(id, out var t))
            {
                t = Task.Run(() => LoadAsync(id, knownUrl));
                Pending[id] = t;
            }
            return t;
        }
    }

    static async Task<Image?> LoadAsync(string id, string? knownUrl)
    {
        try
        {
            var known = (knownUrl ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            // weapons (assembled picture first): own cache file, the old cached bare-receiver picture is not reused
            bool assembled = known.Length > 0 && !known[0].Contains($"{id}-512", StringComparison.OrdinalIgnoreCase);
            string file = Path.Combine(Dir, id + (assembled ? "-full" : "") + ".png");
            if (File.Exists(file))
            {
                try
                {
                    var cached = FromPng(File.ReadAllBytes(file));
                    lock (Lock) Mem[id] = cached;
                    return cached;
                }
                catch (Exception ex)
                {
                    Log.Write("Broken item image cache " + id + ": " + ex.Message, true);
                }
            }

            // 1. the address from the price list, 2. the known address patterns
            var urls = new List<string>(known);
            urls.Add($"https://assets.tarkov.dev/{id}-512.webp");
            urls.Add($"https://assets.tarkov.dev/{id}-grid-image.webp");
            urls.Add($"https://assets.tarkov.dev/{id}-icon.webp");
            urls.Add($"https://assets.tarkov.dev/{id}-base-image.png");   // PNG: readable even without a WebP decoder

            // 3. only if none of them works: ask the tarkov.dev API (slower, reserve)
            if (await TryUrlsAsync(id, file, urls) is Image direct) return direct;
            var queried = new List<string>();
            try { queried.AddRange(await QueryLinksAsync(id)); }
            catch (Exception ex) { Log.Write($"Item image query {id}: {ex.Message}"); }
            if (await TryUrlsAsync(id, file, queried.Except(urls).ToList()) is Image fromApi) return fromApi;

            lock (Lock) Failed.Add(id);
            Log.Write("No item image available for " + id, true);
            return null;
        }
        catch (Exception ex)
        {
            Log.Write("Item image error: " + ex.Message, true);
            return null;
        }
        finally
        {
            lock (Lock) Pending.Remove(id);
        }
    }

    // Downloads the first address that works, stores it in the cache; null when none works
    static async Task<Image?> TryUrlsAsync(string id, string file, List<string> urls)
    {
        foreach (var url in urls.Distinct())
        {
            try
            {
                var raw = await TarkovApi.Http.GetByteArrayAsync(url);
                var png = await TraderImages.ToPngAsync(raw);
                if (png == null) continue;

                var bmp = Fit(png);
                Directory.CreateDirectory(Dir);
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    File.WriteAllBytes(file, ms.ToArray());
                }
                lock (Lock) Mem[id] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Write($"Item image {id} ({url}): {ex.Message}");
            }
        }
        return null;
    }

    static async Task<List<string>> QueryLinksAsync(string id)
    {
        var list = new List<string>();
        if (!Regex.IsMatch(id, "^[0-9a-f]{24}$")) return list;

        var body = JsonSerializer.Serialize(new
        {
            query = "{ item(id: \"" + id + "\") { image512pxLink gridImageLink iconLink baseImageLink } }"
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        using var resp = await TarkovApi.Http.PostAsync("https://api.tarkov.dev/graphql", content, cts.Token);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("item", out var item) ||
            item.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var prop in new[] { "image512pxLink", "gridImageLink", "iconLink", "baseImageLink" })
            if (item.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(u.GetString()))
                list.Add(u.GetString()!);
        return list;
    }

    // Square 192 px bitmap, picture centered (keeps the aspect ratio)
    static Bitmap Fit(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var src = new Bitmap(ms);
        var bmp = new Bitmap(Px, Px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        float k = Math.Min((float)Px / src.Width, (float)Px / src.Height);
        float w = src.Width * k, h = src.Height * k;
        g.DrawImage(src, (Px - w) / 2f, (Px - h) / 2f, w, h);
        return bmp;
    }

    static Bitmap FromPng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);   // independent of the stream
    }
}
