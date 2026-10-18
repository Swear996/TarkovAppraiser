using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Real trader portraits (from tarkov.dev) for the overlay.
//  Downloaded once and cached in %AppData%\TarkovPriceOverlay\traders.
//  If a portrait is not available (yet), the line is simply shown without an image.
// ------------------------------------------------------------
static class TraderImages
{
    static readonly object Lock = new();
    static readonly Dictionary<string, Bitmap> Portraits = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Bitmap> Rendered = new();

    public static string Dir => Path.Combine(SettingsStore.Dir, "traders");

    public static bool Has(string trader)
    {
        lock (Lock) return Portraits.ContainsKey(trader);
    }

    // Round portrait of the trader (cached per name + size); null when no real portrait is available
    public static Image? Get(string trader, int size)
    {
        string key = trader + "|" + size;
        lock (Lock)
        {
            if (Rendered.TryGetValue(key, out var hit)) return hit;
            if (!Portraits.TryGetValue(trader, out var portrait)) return null;
            var bmp = Render(portrait, size);
            Rendered[key] = bmp;
            return bmp;
        }
    }

    static Bitmap Render(Bitmap portrait, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float rw = Math.Max(1.5f, size / 11f);                       // clearly visible ring
        var circle = new RectangleF(rw / 2f, rw / 2f, size - rw, size - rw);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(circle);

            // dark backdrop so transparent parts of a portrait never look washed out
            using (var back = new SolidBrush(Color.FromArgb(255, 28, 28, 32)))
                g.FillPath(back, path);

            var state = g.Save();
            g.SetClip(path);
            float k = Math.Max((float)size / portrait.Width, (float)size / portrait.Height);
            float w = portrait.Width * k, h = portrait.Height * k;
            g.DrawImage(portrait, (size - w) / 2f, 0f, w, h);   // cover fit, top aligned (faces)
            g.Restore(state);
        }

        // bright ring + thin dark outline: readable on every background
        using (var ring = new Pen(Color.FromArgb(235, 255, 255, 255), rw))
            g.DrawEllipse(ring, circle);
        using (var edge = new Pen(Color.FromArgb(150, 0, 0, 0), 1f))
            g.DrawEllipse(edge, 0.5f, 0.5f, size - 1f, size - 1f);

        return bmp;
    }

    // ---- Portraits (cache + download) ----

    public static void LoadCached()
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var kv in TarkovApi.TraderNames)
            {
                string file = Path.Combine(Dir, kv.Key + ".png");
                if (!File.Exists(file)) continue;
                AddFromPng(kv.Value, File.ReadAllBytes(file));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read trader images: " + ex.Message, true);
        }
    }

    public static void AddFromPng(string trader, byte[] png)
    {
        using var ms = new MemoryStream(png);
        using var tmp = new Bitmap(ms);
        var copy = new Bitmap(tmp);   // independent of the stream

        lock (Lock)
        {
            Portraits[trader] = copy;
            Rendered.Clear();   // re-render with the new portrait (old bitmaps may still be in use, GC cleans them up)
        }
    }

    // Converts any image (WebP, PNG, JPG ...) to PNG bytes.
    // Uses the image decoders built into Windows (no extra library and no license needed):
    // WebP is part of Windows 10 1809+ / Windows 11 ("WebP Image Extensions").
    public static async Task<byte[]?> ToPngAsync(byte[] data)
    {
        // 1. Windows image decoder (WebP, PNG, JPG, ...)
        try
        {
            using var input = new InMemoryRandomAccessStream();
            await input.WriteAsync(data.AsBuffer());
            input.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(input);
            using var sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(sb);
            await encoder.FlushAsync();

            output.Seek(0);
            var bytes = new byte[output.Size];
            await output.ReadAsync(bytes.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Write("Windows image decoder failed: " + ex.Message +
                      " (for WebP pictures, install \"WebP Image Extensions\" from the Microsoft Store)");
        }

        // 2. GDI+ (PNG / JPG / GIF)
        try
        {
            using var ms = new MemoryStream(data);
            using var bmp = new Bitmap(ms);
            using var outMs = new MemoryStream();
            bmp.Save(outMs, ImageFormat.Png);
            return outMs.ToArray();
        }
        catch (Exception ex)
        {
            Log.Write("Could not decode trader image: " + ex.Message);
            return null;
        }
    }
}
