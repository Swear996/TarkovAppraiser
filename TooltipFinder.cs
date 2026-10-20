using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Finds the game's item tooltip in a screenshot.
//  EFT draws it as a PURE black box (RGB 0,0,0) with a thin grey frame and light text.
//  The game background never gets that dark (the darkest areas are about 15-20), so a wide
//  run of black pixels with a non-black row above it marks the top edge of a tooltip.
//  Only the tooltip is then given to the OCR: the inventory labels around it can no longer
//  be mixed into the item name.
// ------------------------------------------------------------
static class TooltipFinder
{
    const int Black = 12;   // max. channel value that still counts as tooltip background
    const int MinW = 20;    // minimum tooltip size in screen pixels
    const int MinH = 9;

    // All tooltip boxes in the picture (inner area without the frame), in picture pixels
    public static List<Rectangle> Find(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var boxes = new List<Rectangle>();
        if (w < MinW + 4 || h < MinH + 4) return boxes;

        var bright = Brightness(bmp);
        bool B(int x, int y) => bright[y * w + x] <= Black;

        // brightest of the 3 pixels next to (x, y) in direction dx: the tooltip frame is light grey
        int Frame(int x, int y, int dx)
        {
            int m = 0;
            for (int k = 1; k <= 3; k++)
            {
                int xx = x + dx * k;
                if (xx >= 0 && xx < w) m = Math.Max(m, bright[y * w + xx]);
            }
            return m;
        }

        for (int y = 1; y < h - 1; y++)
        {
            int x = 0;
            while (x < w)
            {
                if (!B(x, y)) { x++; continue; }
                int x0 = x;
                while (x < w && B(x, y)) x++;
                int x1 = x;   // exclusive
                int runW = x1 - x0;
                if (runW < MinW) continue;
                // at least one frame pixel must be visible on both sides. Next to the screen edge the game
                // pushes the tooltip flush against it, so its frame can be the very last pixel column.
                if (x0 < 1 || x1 > w - 1) continue;

                // top edge: the row above is (mostly) not black
                int above = 0;
                for (int i = x0; i < x1; i++) if (B(i, y - 1)) above++;
                if (above > runW * 0.2) continue;

                // follow the left / right padding (no text there) downwards
                int yb = y;
                while (yb + 1 < h && B(x0 + 1, yb + 1) && B(x1 - 2, yb + 1)) yb++;
                int boxH = yb - y + 1;
                if (boxH < MinH || yb + 1 >= h) continue;   // the bottom frame row must be visible

                // the bottom padding row is black as well
                int bottom = 0;
                for (int i = x0; i < x1; i++) if (B(i, yb)) bottom++;
                if (bottom < runW * 0.9) continue;

                // light frame on the left and right (black gaps between UI panels have none)
                int mid = (y + yb) / 2;
                if (Frame(x0, mid, -1) < 40 || Frame(x1 - 1, mid, 1) < 40) continue;

                // there must be some text inside (a plain black area is not a tooltip)
                int ink = 0;
                for (int yy = y; yy <= yb; yy++)
                    for (int i = x0; i < x1; i++)
                        if (!B(i, yy)) ink++;
                if (ink < runW * boxH * 0.01) continue;

                boxes.Add(Rectangle.FromLTRB(x0, y, x1, yb + 1));
            }
        }
        return boxes;
    }

    // The box closest to the point (the cursor), or null when none is within maxDist pixels
    public static Rectangle? Nearest(List<Rectangle> boxes, Point p, int maxDist)
    {
        Rectangle? best = null;
        double bestD = double.MaxValue;
        foreach (var r in boxes)
        {
            if (r.Height > r.Width) continue;   // a tooltip is always wider than tall (a tall box is a panel edge, not a tooltip)
            double dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right));
            double dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestD)
            {
                bestD = d;
                best = r;
            }
        }
        return bestD <= maxDist ? best : null;
    }

    // Cuts the tooltip out and enlarges it.
    // invert = true: dark text on white with extra contrast (what Windows OCR usually reads best)
    // invert = false: the original light-on-black look (reads some words better, e.g. "7.62x39")
    // binary = true: pure black / white without grey edges (only with invert); keeps tiny letter shapes
    public static Bitmap PrepareForOcr(Bitmap shot, Rectangle box, float scale, bool invert = true, bool binary = false)
    {
        const int pad = 16;   // margin around the text (in the tooltip's background color)
        box.Inflate(-1, -1);  // skip the anti-aliased frame edge
        box.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        int w = Math.Max(1, (int)Math.Round(box.Width * scale)), h = Math.Max(1, (int)Math.Round(box.Height * scale));

        var result = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(invert ? Color.White : Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var ia = Attributes(shot, box, invert, binary);
        g.DrawImage(shot, new Rectangle(pad, pad, w, h),
            box.X, box.Y, box.Width, box.Height, GraphicsUnit.Pixel, ia);
        return result;
    }

    // How much larger the game draws its tooltips than on a 768 px high screen (where the recognition
    // was tuned): there a tooltip is 15 px + 11 px per text line high. The number of lines is the one
    // that fits the screen size best, the factor then comes from the measured box itself
    // (so it also follows the in-game UI scale).
    public static float SizeFactor(Rectangle box, int screenHeight)
    {
        float screen = Math.Clamp(screenHeight / 768f, 0.8f, 3f);
        float best = screen, bestDiff = float.MaxValue;
        for (int lines = 1; lines <= 5; lines++)
        {
            float f = box.Height / (15f + 11f * lines);
            float diff = Math.Abs(f - screen);
            if (diff < bestDiff) { bestDiff = diff; best = f; }
        }
        return Math.Clamp(best, 0.7f, 4f);
    }

    // Short names ("Awl"): Windows OCR ignores text areas that are too small - even when enlarged.
    // So the text is cut out tightly and placed several times in a row with a normal word gap
    // ("Awl Awl Awl"), which forms one long, clearly detectable text line. The caller keeps the first copy.
    // Returns null when the box contains no text.
    public static Bitmap? PrepareTiled(Bitmap shot, Rectangle box, float scale, int copies, bool invert, int maxDim)
    {
        box.Inflate(-1, -1);
        box.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        if (box.Width < 3 || box.Height < 3) return null;

        // tight bounding box of the text (pixels clearly brighter than the black background)
        var bright = Brightness(shot);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
                if (bright[y * shot.Width + x] > 50)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return null;

        var ink = Rectangle.FromLTRB(minX - 1, minY - 1, maxX + 2, maxY + 2);
        ink.Intersect(box);
        int gap = Math.Max(2, (int)(ink.Height * 0.7));   // about one word space
        int rowW = copies * ink.Width + (copies - 1) * gap;

        int pad = Math.Max(16, (int)(ink.Height * scale));
        scale = Math.Max(1f, Math.Min(scale, (float)((maxDim - pad * 2) / Math.Max(1, rowW))));
        pad = Math.Max(16, (int)(ink.Height * scale));
        int Px(int v) => (int)Math.Round(v * scale);

        var result = new Bitmap(Px(rowW) + pad * 2, Px(ink.Height) + pad * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(invert ? Color.White : Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var ia = Attributes(shot, ink, invert, false);
        for (int i = 0; i < copies; i++)
            g.DrawImage(shot,
                new Rectangle(pad + Px(i * (ink.Width + gap)), pad, Px(ink.Width), Px(ink.Height)),
                ink.X, ink.Y, ink.Width, ink.Height, GraphicsUnit.Pixel, ia);
        return result;
    }

    // Grayscale + contrast stretch: out = 1 - k * luma (inverted) or k * luma (plain).
    // The tooltip text is fairly dim (brightest pixels only about 120-145 of 255), so k turns the
    // brightest text into (almost) pure black / white instead of grey. binary: hard threshold on top.
    static ImageAttributes Attributes(Bitmap shot, Rectangle box, bool invert, bool binary)
    {
        float k = 230f / Math.Max(40, TextLevel(shot, box));
        float sign = invert ? -1f : 1f;
        float offset = invert ? 1f : 0f;
        var cm = new ColorMatrix(new[]
        {
            new[] { sign * 0.299f * k, sign * 0.299f * k, sign * 0.299f * k, 0f, 0f },
            new[] { sign * 0.587f * k, sign * 0.587f * k, sign * 0.587f * k, 0f, 0f },
            new[] { sign * 0.114f * k, sign * 0.114f * k, sign * 0.114f * k, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { offset, offset, offset, 0f, 1f },
        });
        var ia = new ImageAttributes();
        ia.SetColorMatrix(cm);
        if (binary && invert) ia.SetThreshold(0.57f);
        ia.SetWrapMode(WrapMode.TileFlipXY);   // no dark seams at the edges when scaling
        return ia;
    }

    // Brightness of the text: the 99th percentile of the box (the brightest 1 % are the text strokes)
    static int TextLevel(Bitmap bmp, Rectangle box)
    {
        var data = bmp.LockBits(box, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var hist = new int[256];
            var row = new byte[box.Width * 4];
            for (int y = 0; y < box.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < box.Width; x++)
                {
                    int i = x * 4;   // B, G, R, A
                    hist[(row[i + 2] * 299 + row[i + 1] * 587 + row[i] * 114) / 1000]++;
                }
            }
            int skip = box.Width * box.Height / 100, seen = 0;
            for (int v = 255; v > 0; v--)
            {
                seen += hist[v];
                if (seen > skip) return v;
            }
            return 255;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // Brightest color channel of every pixel (0 = black)
    static byte[] Brightness(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            var bright = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                int o = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = x * 4;   // B, G, R, A
                    bright[o + x] = Math.Max(row[i], Math.Max(row[i + 1], row[i + 2]));
                }
            }
            return bright;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
