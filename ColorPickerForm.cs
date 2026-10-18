using System.Drawing.Drawing2D;
using System.Globalization;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Color picker in the look of the app (replaces the Windows color dialog).
//  Opens under the color button; every change is applied live (ColorChanged).
//  Click outside or Enter = done, Esc = back to the color it had before, "Default" = standard color.
// ------------------------------------------------------------
sealed class ColorPickerForm : Form
{
    public event Action<Color>? ColorChanged;

    const int Pad = 14, AreaW = 248, AreaH = 150, HueH = 14, Sw = 22, SwCols = 9;
    static readonly Rectangle SvRect = new(Pad, Pad, AreaW, AreaH);
    static readonly Rectangle HueRect = new(Pad, Pad + AreaH + 12, AreaW, HueH);
    static readonly Rectangle PrevRect = new(Pad, HueRect.Bottom + 14, 56, 30);
    static int SwTop => PrevRect.Bottom + 14;

    // quick choices: neutrals, then colors that read well on dark and light backgrounds
    static readonly int[] Palette =
    {
        unchecked((int)0xFFFFFFFF), unchecked((int)0xFFD8D8DC), unchecked((int)0xFFA0A0A8), unchecked((int)0xFF5C5C66),
        unchecked((int)0xFF2A2A30), unchecked((int)0xFF1A1A1E), unchecked((int)0xFF101014), unchecked((int)0xFF000000),
        unchecked((int)0xFF8A6E4B),
        unchecked((int)0xFFE5484D), unchecked((int)0xFFF76B15), unchecked((int)0xFFFFC53D), unchecked((int)0xFF46A758),
        unchecked((int)0xFF12A594), unchecked((int)0xFF3E9BFF), unchecked((int)0xFF6E56CF), unchecked((int)0xFFD6409F),
        unchecked((int)0xFFB8A57A)
    };

    readonly Color _original, _panel, _text, _border, _accent;
    readonly Color? _default;
    Rectangle _defRect;
    readonly Font _btnFont = UiFont.Semi(9.5f);
    bool _defHover;
    readonly Host _hexHost;
    readonly TextBox _hex = new() { BorderStyle = BorderStyle.None, MaxLength = 7 };
    readonly System.Windows.Forms.Timer _live = new() { Interval = 30 };
    float _h, _s, _v;           // hue 0..360, saturation / value 0..1
    bool _dirty, _settingText;
    enum Drag { None, Area, Hue }
    Drag _drag;
    int _hoverSwatch = -1;

    public Color Value => FromHsv(_h, _s, _v);

    public ColorPickerForm(Color start, Color panel, Color text, Color border, Color accent, Color? standard = null)
    {
        _original = Color.FromArgb(255, start);
        _panel = panel; _text = text; _border = border; _accent = accent;
        _default = standard is Color d ? Color.FromArgb(255, d) : null;
        (_h, _s, _v) = ToHsv(_original);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = panel;
        Font = UiFont.Text(9.5f);
        int rows = (Palette.Length + SwCols - 1) / SwCols;
        ClientSize = new Size(AreaW + 2 * Pad, SwTop + rows * (Sw + 6) + 26);

        _hexHost = new Host
        {
            Left = PrevRect.Right + 10, Top = PrevRect.Top - 1, Width = 84, Height = 32,
            Fill = Themes.Blend(panel, text, 0.08), Border = Themes.Blend(panel, text, 0.2), Accent = accent
        };
        _hex.BackColor = _hexHost.Fill;
        _hex.ForeColor = text;
        _hex.Font = UiFont.Semi(10f);
        _hexHost.Controls.Add(_hex);
        Controls.Add(_hexHost);
        _defRect = new Rectangle(_hexHost.Right + 8, PrevRect.Top, Pad + AreaW - _hexHost.Right - 8, 30);
        Controls.Add(new Txt
        {
            Text = L.T("Esc = undo"), ForeColor = Themes.Blend(text, panel, 0.45), BackColor = panel, UseMnemonic = false,
            Font = UiFont.Text(8.5f), Left = Pad, Top = ClientSize.Height - 24, Width = AreaW, Height = 18
        });

        _hex.TextChanged += (_, _) =>
        {
            if (_settingText) return;
            if (TryHex(_hex.Text, out var c))
            {
                (_h, _s, _v) = ToHsv(c);
                MarkChanged(updateText: false);
            }
        };
        _live.Tick += (_, _) => Flush();
        _live.Start();
        UpdateText();
    }

    // ---------- live update ----------
    void MarkChanged(bool updateText = true)
    {
        _dirty = true;
        if (updateText) UpdateText();
        Invalidate();
    }

    void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        ColorChanged?.Invoke(Value);
    }

    void UpdateText()
    {
        var c = Value;
        _settingText = true;
        _hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _settingText = false;
    }

    static bool TryHex(string s, out Color c)
    {
        c = Color.Empty;
        s = s.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        c = Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        return true;
    }

    // ---------- keys / closing ----------
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            (_h, _s, _v) = ToHsv(_original);
            _dirty = true;
            Flush();
            Close();
            return true;
        }
        if (keyData == Keys.Enter) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(new Action(() => { if (!IsDisposed && Visible) Close(); }));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Flush();
        _live.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Themes.RoundCorners(Handle, _border))
        {
            using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 10);
            Region = new Region(path);
        }
    }

    // ---------- mouse ----------
    Rectangle SwatchRect(int i)
    {
        float gap = (AreaW - SwCols * Sw) / (float)(SwCols - 1);
        return new Rectangle(Pad + (int)Math.Round(i % SwCols * (Sw + gap)), SwTop + i / SwCols * (Sw + 6), Sw, Sw);
    }

    int SwatchAt(Point p)
    {
        for (int i = 0; i < Palette.Length; i++)
            if (SwatchRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var area = SvRect; area.Inflate(4, 4);
        var hue = HueRect; hue.Inflate(0, 5);
        if (area.Contains(e.Location)) _drag = Drag.Area;
        else if (hue.Contains(e.Location)) _drag = Drag.Hue;
        else if (SwatchAt(e.Location) is int i and >= 0)
        {
            (_h, _s, _v) = ToHsv(Color.FromArgb(Palette[i]));
            MarkChanged();
            return;
        }
        else if (_default is Color std && _defRect.Contains(e.Location))
        {
            (_h, _s, _v) = ToHsv(std);
            MarkChanged();
            return;
        }
        else if (PrevRect.Contains(e.Location) && e.X < PrevRect.Left + PrevRect.Width / 2)
        {
            (_h, _s, _v) = ToHsv(_original);   // left half = color before: click to go back
            MarkChanged();
            return;
        }
        if (_drag != Drag.None) { Capture = true; DragTo(e.Location); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag != Drag.None) { DragTo(e.Location); return; }
        int sw = SwatchAt(e.Location);
        var area = SvRect; area.Inflate(4, 4);
        var hue = HueRect; hue.Inflate(0, 5);
        bool back = PrevRect.Contains(e.Location) && e.X < PrevRect.Left + PrevRect.Width / 2;
        bool def = _default != null && _defRect.Contains(e.Location);
        if (def != _defHover) { _defHover = def; Invalidate(); }
        Cursor = sw >= 0 || back || def ? Cursors.Hand : area.Contains(e.Location) || hue.Contains(e.Location) ? Cursors.Cross : Cursors.Default;
        if (sw != _hoverSwatch) { _hoverSwatch = sw; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        Capture = false;
        Flush();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverSwatch != -1 || _defHover) { _hoverSwatch = -1; _defHover = false; Invalidate(); }
    }

    void DragTo(Point p)
    {
        if (_drag == Drag.Area)
        {
            _s = Math.Clamp((p.X - SvRect.Left) / (float)SvRect.Width, 0f, 1f);
            _v = 1f - Math.Clamp((p.Y - SvRect.Top) / (float)SvRect.Height, 0f, 1f);
        }
        else
        {
            _h = Math.Clamp((p.X - HueRect.Left) / (float)HueRect.Width, 0f, 1f) * 360f;
            if (_h >= 360f) _h = 359.9f;
        }
        MarkChanged();
    }

    // ---------- painting ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        UiPaint.Smooth(g);

        // saturation / brightness area: hue color, white from the left, black from the bottom
        using (var path = Themes.RoundRect(SvRect, 8))
        {
            var clip = g.Clip;
            g.SetClip(path);
            using (var b = new SolidBrush(FromHsv(_h, 1, 1))) g.FillRectangle(b, SvRect);
            var r = new Rectangle(SvRect.X - 1, SvRect.Y - 1, SvRect.Width + 2, SvRect.Height + 2);
            using (var white = new LinearGradientBrush(r, Color.White, Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal))
                g.FillRectangle(white, SvRect);
            using (var black = new LinearGradientBrush(r, Color.FromArgb(0, Color.Black), Color.Black, LinearGradientMode.Vertical))
                g.FillRectangle(black, SvRect);
            g.Clip = clip;
            using var edge = new Pen(_border, 1f);
            g.DrawPath(edge, path);
        }
        var cur = Value;
        float mx = SvRect.Left + _s * SvRect.Width, my = SvRect.Top + (1 - _v) * SvRect.Height;
        DrawMarker(g, new RectangleF(mx - 7, my - 7, 14, 14), cur);

        // hue bar
        using (var path = Themes.RoundRect(HueRect, HueH / 2f))
        using (var hb = new LinearGradientBrush(new Rectangle(HueRect.X - 1, HueRect.Y, HueRect.Width + 2, HueRect.Height), Color.Red, Color.Red, LinearGradientMode.Horizontal))
        {
            var blend = new ColorBlend(7);
            for (int i = 0; i < 7; i++)
            {
                blend.Colors[i] = FromHsv(i * 60 % 360, 1, 1);
                blend.Positions[i] = i / 6f;
            }
            hb.InterpolationColors = blend;
            g.FillPath(hb, path);
        }
        float hx = HueRect.Left + _h / 360f * HueRect.Width;
        DrawMarker(g, new RectangleF(hx - 8, HueRect.Top + HueH / 2f - 8, 16, 16), FromHsv(_h, 1, 1));

        // before | now
        using (var path = Themes.RoundRect(PrevRect, 8))
        {
            var clip = g.Clip;
            g.SetClip(path);
            using (var b = new SolidBrush(_original)) g.FillRectangle(b, PrevRect.X, PrevRect.Y, PrevRect.Width / 2, PrevRect.Height);
            using (var b = new SolidBrush(cur)) g.FillRectangle(b, PrevRect.X + PrevRect.Width / 2, PrevRect.Y, PrevRect.Width - PrevRect.Width / 2, PrevRect.Height);
            g.Clip = clip;
            using var edge = new Pen(_border, 1f);
            g.DrawPath(edge, path);
        }

        // "Default" button (back to the standard color)
        if (_default is Color dc)
        {
            bool isDef = dc.ToArgb() == cur.ToArgb();
            using var path = Themes.RoundRect(_defRect, 8);
            using (var b = new SolidBrush(Themes.Blend(_panel, _text, _defHover && !isDef ? 0.16 : 0.08))) g.FillPath(b, path);
            using (var pen = new Pen(Themes.Blend(_panel, _text, 0.2), 1f)) g.DrawPath(pen, path);
            var dot = new Rectangle(_defRect.X + 9, _defRect.Y + _defRect.Height / 2 - 5, 10, 10);
            using (var b = new SolidBrush(dc)) g.FillEllipse(b, dot);
            using (var pen = new Pen(_border, 1f)) g.DrawEllipse(pen, dot);
            TextRenderer.DrawText(g, L.T("Default"), _btnFont,
                new Rectangle(dot.Right + 5, _defRect.Y, _defRect.Right - dot.Right - 5, _defRect.Height),
                isDef ? Themes.Blend(_text, _panel, 0.3) : _text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        // quick choices
        for (int i = 0; i < Palette.Length; i++)
        {
            var r = SwatchRect(i);
            var c = Color.FromArgb(Palette[i]);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            bool sel = c.ToArgb() == cur.ToArgb();
            using (var pen = new Pen(sel ? _accent : i == _hoverSwatch ? _text : _border, sel || i == _hoverSwatch ? 2f : 1f))
                g.DrawEllipse(pen, r.X - (sel ? 2f : 0f), r.Y - (sel ? 2f : 0f), r.Width + (sel ? 4f : 0f), r.Height + (sel ? 4f : 0f));
        }

        // outline (Windows 10 has no native border for this window)
        using (var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        using (var pen = new Pen(_border, 1f))
            g.DrawPath(pen, path);
    }

    static void DrawMarker(Graphics g, RectangleF r, Color fill)
    {
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
        using (var dark = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(dark, r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);
        using var white = new Pen(Color.White, 2.5f);
        g.DrawEllipse(white, r);
    }

    // ---------- HSV ----------
    static (float H, float S, float V) ToHsv(Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        float h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    static Color FromHsv(float h, float s, float v)
    {
        float c = v * s, x = c * (1 - Math.Abs(h / 60f % 2 - 1)), m = v - c;
        (float r, float g, float b) = (h % 360) switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        int To(float f) => Math.Clamp((int)Math.Round((f + m) * 255), 0, 255);
        return Color.FromArgb(255, To(r), To(g), To(b));
    }
}
