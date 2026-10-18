using System.Drawing.Drawing2D;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Small vector icons drawn with GDI+ (no image files needed).
//  Everything is designed on a 24 x 24 grid and scaled to the target rectangle,
//  so the icons stay sharp at any size and follow the theme colors.
// ------------------------------------------------------------
static class Icons
{
    static PointF P(float x, float y) => new(x, y);

    public static void Draw(Graphics g, string name, RectangleF r, Color color, float stroke = 1.9f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);

        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (name)
        {
            case "gear":
            {
                g.DrawEllipse(pen, 8.8f, 8.8f, 6.4f, 6.4f);
                g.DrawEllipse(pen, 5.5f, 5.5f, 13f, 13f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float cs = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    g.DrawLine(pen, 12 + cs * 6.5f, 12 + sn * 6.5f, 12 + cs * 9.4f, 12 + sn * 9.4f);
                }
                break;
            }

            case "globe":
            {
                g.DrawEllipse(pen, 3f, 3f, 18f, 18f);
                g.DrawEllipse(pen, 8f, 3f, 8f, 18f);
                g.DrawLine(pen, 3f, 12f, 21f, 12f);
                g.DrawLine(pen, 4.2f, 7.5f, 19.8f, 7.5f);
                g.DrawLine(pen, 4.2f, 16.5f, 19.8f, 16.5f);
                break;
            }

            case "scan":
            {
                g.DrawLines(pen, new[] { P(3, 8), P(3, 3), P(8, 3) });
                g.DrawLines(pen, new[] { P(16, 3), P(21, 3), P(21, 8) });
                g.DrawLines(pen, new[] { P(21, 16), P(21, 21), P(16, 21) });
                g.DrawLines(pen, new[] { P(8, 21), P(3, 21), P(3, 16) });
                g.FillEllipse(brush, 9.5f, 9.5f, 5f, 5f);
                break;
            }

            case "overlay":
            {
                using (var path = Themes.RoundRect(new RectangleF(3f, 4.5f, 18f, 15f), 3f))
                    g.DrawPath(pen, path);
                g.DrawLine(pen, 3f, 9.5f, 21f, 9.5f);
                g.DrawLine(pen, 7f, 13.2f, 17f, 13.2f);
                g.DrawLine(pen, 7f, 16.2f, 13f, 16.2f);
                break;
            }

            case "gem":
            {
                g.DrawPolygon(pen, new[] { P(6.5f, 4f), P(17.5f, 4f), P(22f, 9.5f), P(12f, 21f), P(2f, 9.5f) });
                g.DrawLine(pen, 2f, 9.5f, 22f, 9.5f);
                g.DrawLine(pen, 9f, 9.5f, 12f, 21f);
                g.DrawLine(pen, 15f, 9.5f, 12f, 21f);
                break;
            }

            case "drop":
            {
                const float deg = -35.7f;
                float rad = deg * (float)Math.PI / 180f;
                using var path = new GraphicsPath();
                path.AddLine(12f, 3f, 12f + 7f * (float)Math.Cos(rad), 15f + 7f * (float)Math.Sin(rad));
                path.AddArc(5f, 8f, 14f, 14f, deg, 251.4f);
                path.CloseFigure();
                g.DrawPath(pen, path);
                break;
            }

            case "log":
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = 6f + i * 6f;
                    g.FillEllipse(brush, 3f, y - 1.7f, 3.4f, 3.4f);
                    g.DrawLine(pen, 9.5f, y, 21f, y);
                }
                break;
            }

            case "code":
            {
                g.DrawLines(pen, new[] { P(8f, 7f), P(3f, 12f), P(8f, 17f) });
                g.DrawLines(pen, new[] { P(16f, 7f), P(21f, 12f), P(16f, 17f) });
                g.DrawLine(pen, 13.5f, 5f, 10.5f, 19f);
                break;
            }

            case "reload":
            {
                const float start = -40f, sweep = 280f;
                g.DrawArc(pen, 4f, 4f, 16f, 16f, start, sweep);
                double end = (start + sweep) * Math.PI / 180.0;
                float tx = (float)-Math.Sin(end), ty = (float)Math.Cos(end);   // direction of travel (clockwise)
                var tip = P(12f + 8f * (float)Math.Cos(end) + tx * 1.2f, 12f + 8f * (float)Math.Sin(end) + ty * 1.2f);
                Arrow(g, pen, tip, tx, ty, 4.2f);
                break;
            }

            case "history":
            {
                g.DrawArc(pen, 3.5f, 3.5f, 17f, 17f, 200f, 300f);
                var tip = P(12f + 8.5f * (float)Math.Cos(200 * Math.PI / 180.0), 12f + 8.5f * (float)Math.Sin(200 * Math.PI / 180.0));
                Arrow(g, pen, tip, (float)Math.Sin(200 * Math.PI / 180.0), (float)-Math.Cos(200 * Math.PI / 180.0), 3.8f);
                g.DrawLines(pen, new[] { P(12f, 7.5f), P(12f, 12f), P(15.5f, 14f) });
                break;
            }

            case "chart":
            {
                g.DrawLine(pen, 4f, 20f, 20f, 20f);
                g.DrawLine(pen, 7f, 20f, 7f, 13f);
                g.DrawLine(pen, 12f, 20f, 12f, 6f);
                g.DrawLine(pen, 17f, 20f, 17f, 10f);
                break;
            }

            case "help":
            {
                g.DrawEllipse(pen, 3.5f, 3.5f, 17f, 17f);
                g.DrawArc(pen, 9.2f, 7f, 5.6f, 5.6f, 180f, 250f);
                g.DrawLine(pen, 12f, 12.4f, 12f, 13.6f);
                g.FillEllipse(brush, 11f, 15.8f, 2f, 2f);
                break;
            }

            case "power":
            {
                g.DrawArc(pen, 4.5f, 5.5f, 15f, 15f, -60f, 300f);
                g.DrawLine(pen, 12f, 3f, 12f, 12f);
                break;
            }
        }

        g.Restore(state);
    }

    // Arrow head: two short lines from the tip, angled back against the direction (dx, dy)
    static void Arrow(Graphics g, Pen pen, PointF tip, float dx, float dy, float len)
    {
        foreach (double deg in new[] { 150.0, -150.0 })
        {
            double r = deg * Math.PI / 180.0;
            float ux = (float)(dx * Math.Cos(r) - dy * Math.Sin(r));
            float uy = (float)(dx * Math.Sin(r) + dy * Math.Cos(r));
            g.DrawLine(pen, tip.X, tip.Y, tip.X + ux * len, tip.Y + uy * len);
        }
    }

    // Standalone bitmap (used for the tray menu)
    public static Bitmap Render(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        Draw(g, name, new RectangleF(0, 0, size, size), color, 2f);
        return bmp;
    }
}
