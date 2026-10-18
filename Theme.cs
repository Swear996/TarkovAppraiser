using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

public sealed record ThemePreset(string Name, int Back, int Panel, int Text, int Accent);

public static class Themes
{
    static int C(uint v) => unchecked((int)v);

    // All presets are dark. Back = sidebar / footer, Panel = content area.
    public static readonly ThemePreset[] Presets =
    {
        new("Fluent",       unchecked((int)0xFF0D0F13), unchecked((int)0xFF111318), unchecked((int)0xFFE8EAF0), unchecked((int)0xFF5B8CFF)),
        new("Midnight",     unchecked((int)0xFF0D1017), unchecked((int)0xFF171B25), unchecked((int)0xFFE8EAF0), unchecked((int)0xFF4F8CFF)),
        new("Graphite",     unchecked((int)0xFF111111), unchecked((int)0xFF1C1C1E), unchecked((int)0xFFECECEC), unchecked((int)0xFF2DD4BF)),
        new("Tarkov Olive", unchecked((int)0xFF11130C), unchecked((int)0xFF1C2015), unchecked((int)0xFFE6E3D1), unchecked((int)0xFFB5BD4C)),
        new("Crimson",      unchecked((int)0xFF140D0E), unchecked((int)0xFF211517), unchecked((int)0xFFF3E8E9), unchecked((int)0xFFE5484D)),
        new("Ocean",        unchecked((int)0xFF09131A), unchecked((int)0xFF10222C), unchecked((int)0xFFE2F1F6), unchecked((int)0xFF2CB8E0)),
        new("Violet",       unchecked((int)0xFF110D18), unchecked((int)0xFF1B1526), unchecked((int)0xFFEEE8F8), unchecked((int)0xFF9D70FF)),
        new("Emerald",      unchecked((int)0xFF0A1410), unchecked((int)0xFF12231B), unchecked((int)0xFFE4F3EC), unchecked((int)0xFF34D399)),
        new("Sunset",     unchecked((int)0xFF160E0A), unchecked((int)0xFF24170F), unchecked((int)0xFFF6EBE0), unchecked((int)0xFFFF8A3D)),
        new("Rose",       unchecked((int)0xFF160D12), unchecked((int)0xFF24151D), unchecked((int)0xFFF7E9F0), unchecked((int)0xFFFF5C9E)),
        new("Amber",      unchecked((int)0xFF14110A), unchecked((int)0xFF211C0F), unchecked((int)0xFFF4EEDC), unchecked((int)0xFFFFC233)),
        new("Arctic",     unchecked((int)0xFF0B1118), unchecked((int)0xFF141D28), unchecked((int)0xFFE6EEF7), unchecked((int)0xFF7DD3FC)),
        new("Mint",       unchecked((int)0xFF0A1412), unchecked((int)0xFF11201C), unchecked((int)0xFFE3F5EF), unchecked((int)0xFF5EEAD4)),
        new("Slate",      unchecked((int)0xFF0F1115), unchecked((int)0xFF1A1D23), unchecked((int)0xFFE5E7EB), unchecked((int)0xFF94A3B8)),
        new("Indigo",     unchecked((int)0xFF0D0F1C), unchecked((int)0xFF161A2E), unchecked((int)0xFFE8EAFB), unchecked((int)0xFF6366F1)),
        new("Lime",       unchecked((int)0xFF0E1208), unchecked((int)0xFF18200F), unchecked((int)0xFFEBF3DC), unchecked((int)0xFFA3E635)),
        new("AMOLED",     unchecked((int)0xFF000000), unchecked((int)0xFF0B0B0B), unchecked((int)0xFFEDEDED), unchecked((int)0xFF22D3EE)),
    };

    public static Color FromArgb(int argb) => Color.FromArgb(255, Color.FromArgb(argb));

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(255,
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    static int Luma(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;

    public static Color Contrast(Color c) => Luma(c) > 140 ? Color.Black : Color.White;

    public static bool IsDark(Color c) => Luma(c) <= 140;

    // ---- Rounded rectangles ----
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---- App logo "Tarkov Appraiser": magnifying glass (accent) with a ruble sign over a 2 x 2 stash grid,
    //      on a dark rounded tile. Drawn from a 64 x 64 design grid, so it stays sharp at every size.
    public static Bitmap Logo(int size, Color accent)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / 64f, size / 64f);

        var tileColor = Color.FromArgb(0x11, 0x13, 0x18);
        using (var tile = RoundRect(new RectangleF(0, 0, 64, 64), 14))
        using (var br = new SolidBrush(tileColor))
            g.FillPath(br, tile);

        // stash grid: 2 x 2 cells
        using (var cellFill = new SolidBrush(Color.FromArgb(0x1D, 0x21, 0x29)))
        using (var cellLine = new Pen(Color.FromArgb(0x3B, 0x41, 0x4D), 1.5f))
            foreach (var (x, y) in new[] { (8f, 8f), (33f, 8f), (8f, 33f), (33f, 33f) })
                using (var cellPath = RoundRect(new RectangleF(x, y, 21, 21), 3))
                {
                    g.FillPath(cellFill, cellPath);
                    g.DrawPath(cellLine, cellPath);
                }

        // lens (filled with the tile color, so the grid does not show through) + handle
        using (var inside = new SolidBrush(tileColor))
            g.FillEllipse(inside, 14, 14, 28, 28);
        using (var ring = new Pen(accent, 4.5f))
            g.DrawEllipse(ring, 14, 14, 28, 28);
        using (var handle = new Pen(accent, 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(handle, 38.5f, 38.5f, 52f, 52f);

        // ruble sign inside the lens
        using var rub = new GraphicsPath();
        rub.AddLine(23.5f, 35f, 23.5f, 19f);
        rub.AddLine(23.5f, 19f, 29f, 19f);
        rub.AddArc(24f, 19f, 10f, 10f, -90f, 180f);
        rub.AddLine(29f, 29f, 21f, 29f);
        rub.StartFigure();
        rub.AddLine(21f, 32.5f, 29f, 32.5f);
        using (var m = new Matrix())
        {
            // centered in the lens at 85 % size
            m.Translate(28f, 28f);
            m.Scale(0.85f, 0.85f);
            m.Translate(-27.5f, -27f);
            rub.Transform(m);
        }
        using (var pen = new Pen(Color.FromArgb(0xF2, 0xEE, 0xE0), 2.9f)
               { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawPath(pen, rub);

        return bmp;
    }

    // Placeholder picture (example item of the live preview): a question mark in the accent color
    // on a soft accent circle, transparent around it (the price box draws its picture tile behind it)
    public static Bitmap Placeholder(int size, Color accent)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float s = size;

        using (var soft = new SolidBrush(Color.FromArgb(40, accent)))
            g.FillEllipse(soft, s * 0.14f, s * 0.14f, s * 0.72f, s * 0.72f);
        using (var ring = new Pen(Color.FromArgb(150, accent), s * 0.025f) { DashStyle = DashStyle.Dash })
            g.DrawEllipse(ring, s * 0.14f, s * 0.14f, s * 0.72f, s * 0.72f);

        // the question mark as a path, so it is centered exactly and drawn smoothly
        using var path = new GraphicsPath();
        using (var family = new FontFamily(UiFont.Pick("Segoe UI Semibold", "Segoe UI")))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            path.AddString("?", family, (int)FontStyle.Regular, s * 0.5f, new RectangleF(0, 0, s, s), sf);
        var b = path.GetBounds();
        using (var m = new Matrix())
        {
            m.Translate(s / 2f - (b.X + b.Width / 2f), s / 2f - (b.Y + b.Height / 2f));
            path.Transform(m);
        }
        using (var br = new SolidBrush(accent))
            g.FillPath(br, path);
        return bmp;
    }

    // Icon for the window / tray (the tiny native handle is intentionally kept alive for the icon's lifetime)
    public static Icon LogoIcon(Color accent)
    {
        using var bmp = Logo(64, accent);
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---- Window chrome (Windows 11: native rounded corners + border color) ----
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? app, string? ids);

    public static bool DarkUi { get; set; } = true;

    public static void ExplorerTheme(IntPtr handle)
    {
        try { SetWindowTheme(handle, DarkUi ? "DarkMode_Explorer" : "Explorer", null); }
        catch { /* cosmetic */ }
    }

    // Returns false when the OS cannot round the window itself (Windows 10) - caller then uses a region
    public static bool RoundCorners(IntPtr handle, Color border, bool round = true)
    {
        if (Environment.OSVersion.Version.Build < 22000) return false;
        try
        {
            int pref = round ? 2 : 1; // DWMWCP_ROUND / DWMWCP_DONOTROUND
            DwmSetWindowAttribute(handle, 33, ref pref, sizeof(int));
            int col = border.R | (border.G << 8) | (border.B << 16);
            DwmSetWindowAttribute(handle, 34, ref col, sizeof(int)); // DWMWA_BORDER_COLOR
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---- Dark title bar (Windows 10 20H1+ / Windows 11) ----
    public static void DarkTitleBar(IntPtr handle, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref v, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref v, sizeof(int));
        }
        catch
        {
            // purely cosmetic
        }
    }

    // ---- Themed tray menu ----
    public static void StyleMenu(ToolStripDropDown menu, Color back, Color text, Color accent)
    {
        menu.Renderer = new MenuRenderer(back, text, accent);
        menu.BackColor = Blend(back, text, 0.04);
        menu.ForeColor = text;
        menu.Font = UiFont.Text(9.5f);
        menu.Padding = new Padding(4, 6, 4, 6);
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = menu.BackColor;
            item.ForeColor = text;
            if (item is ToolStripMenuItem mi)
            {
                mi.Padding = new Padding(2, 5, 2, 5);
                if (mi.HasDropDownItems) StyleMenu(mi.DropDown, back, text, accent);
            }
        }
    }

    // Menu entries with an on/off switch on the right (drawn by MenuRenderer)
    public sealed class SwitchMenuItem : ToolStripMenuItem
    {
        public bool On;
        public SwitchMenuItem(string text, EventHandler click) : base(text, null, click)
        {
            ShortcutKeyDisplayString = "\u2003\u2003\u2003";   // keeps room for the switch
        }
    }

    // Rounded hover, quiet separators, muted disabled lines, switches
    sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        readonly Color _back, _text, _muted, _hover, _border, _accent;

        public MenuRenderer(Color back, Color text, Color accent) : base(new ThemedColorTable(back, text, accent))
        {
            RoundedEdges = false;
            _back = Blend(back, text, 0.04);
            _text = text;
            _muted = Blend(text, back, 0.45);
            _hover = Blend(_back, text, 0.09);
            _border = Blend(back, text, 0.16);
            _accent = accent;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(_back);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_border);
            var r = e.ToolStrip.ClientRectangle;
            e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(_border);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var item = e.Item;
            if (item.Selected && item.Enabled)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = RoundRect(new RectangleF(2, 1, item.Width - 4, item.Height - 2), 6);
                using var b = new SolidBrush(_hover);
                g.FillPath(b, path);
            }
            if (item is SwitchMenuItem sw)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var track = new RectangleF(item.Width - 46, item.Height / 2f - 8, 32, 16);
                Color on = sw.Enabled ? _accent : Blend(_accent, _back, 0.5);
                using (var path = RoundRect(track, 8))
                {
                    if (sw.On) { using var b = new SolidBrush(on); g.FillPath(b, path); }
                    else { using var pen = new Pen(_muted, 1.3f); g.DrawPath(pen, path); }
                }
                float d = 10, x = sw.On ? track.Right - d - 3 : track.X + 3;
                using var knob = new SolidBrush(sw.On ? Contrast(on) : _muted);
                g.FillEllipse(knob, x, track.Y + 3, d, d);
            }
        }

        // drawn here (Windows would draw disabled lines with its own gray, embossed look)
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            bool shortcut = mi != null && e.Text == mi.ShortcutKeyDisplayString;
            if (shortcut && e.Item is SwitchMenuItem) return;
            Color c = e.Item.Tag is "header" ? _text
                    : e.Item.Tag is "status" ? _muted
                    : shortcut ? _muted
                    : e.Item.Enabled ? _text : Blend(_muted, _back, 0.25);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, c, e.TextFormat);
        }

        // pictures in full color, also on lines that cannot be clicked (logo, status dot)
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null) return;
            var r = e.ImageRectangle;
            if (!e.Item.Enabled && e.Item.Tag is not ("header" or "status"))
            {
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f };   // faded
                attrs.SetColorMatrix(cm);
                e.Graphics.DrawImage(e.Image, r, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, attrs);
                return;
            }
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(e.Image, r);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    }

    sealed class ThemedColorTable : ProfessionalColorTable
    {
        readonly Color _back, _hover, _border;

        public ThemedColorTable(Color back, Color text, Color accent)
        {
            _back = Blend(back, text, 0.04);
            _hover = Blend(back, accent, 0.35);
            _border = Blend(back, text, 0.18);
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _back;
        public override Color ImageMarginGradientBegin => _back;
        public override Color ImageMarginGradientMiddle => _back;
        public override Color ImageMarginGradientEnd => _back;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _border;
    }
}
