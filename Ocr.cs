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

static class Ocr
{
    static OcrEngine? _engine;
    static string _language = "";

    // Re-creates the engine when needed (e.g. after a language change in the settings)
    public static bool EnsureEngine()
    {
        string tag = SettingsStore.Current.OcrLanguage;
        if (_engine == null || _language != tag)
        {
            OcrEngine? e = null;
            try { e = OcrEngine.TryCreateFromLanguage(new Language(tag)); }
            catch (Exception ex) { Log.Write("Invalid OCR language: " + ex.Message, true); }

            e ??= OcrEngine.TryCreateFromUserProfileLanguages();
            _engine = e;
            _language = tag;
        }
        return _engine != null;
    }

    // splitGaps: split lines at wide gaps (whole capture area with inventory labels). Not for the tooltip
    // cut-out: there a gap only means a word was not recognized ("9x19"), and the line must stay whole.
    public static async Task<List<OcrLineInfo>> ReadLinesAsync(Bitmap bmp, bool splitGaps = true)
    {
        if (!EnsureEngine() || _engine == null) return new List<OcrLineInfo>();

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(softwareBitmap);

        // Windows OCR often joins text that sits on the same row into ONE line - e.g. the small hover
        // tooltip ("Duct tape") together with the item labels of the inventory grid next to it.
        // So every OCR line is split wherever there is a wide horizontal gap between two words.
        var list = new List<OcrLineInfo>();
        foreach (var line in result.Lines)
        {
            var words = line.Words;
            if (words.Count == 0) continue;

            int start = 0;
            for (int i = 1; i <= words.Count; i++)
            {
                bool split = i == words.Count;
                if (!split && splitGaps)
                {
                    var prev = words[i - 1].BoundingRect;
                    var cur = words[i].BoundingRect;
                    double h = Math.Max(prev.Height, cur.Height);
                    split = cur.Left - prev.Right > h * 1.0;   // a normal space is only about 0.3 x the text height
                }
                if (!split) continue;

                var seg = words.Skip(start).Take(i - start).ToList();
                start = i;

                double left = seg.Min(w => w.BoundingRect.Left);
                double right = seg.Max(w => w.BoundingRect.Right);
                double top = seg.Min(w => w.BoundingRect.Top);
                double bottom = seg.Max(w => w.BoundingRect.Bottom);
                list.Add(new OcrLineInfo(string.Join(" ", seg.Select(w => w.Text)),
                    (left + right) / 2, (top + bottom) / 2, left, top, right, bottom));
            }
        }
        return list;
    }
}
