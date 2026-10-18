using System.Drawing.Drawing2D;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Custom-drawn, rounded controls used by the settings window.
//  Colors are assigned by SettingsForm (theme), these classes only paint.
// ------------------------------------------------------------

static class UiPaint
{
    public static void Smooth(Graphics g) => g.SmoothingMode = SmoothingMode.AntiAlias;

    // corner radius of buttons and inputs (cards use 12)
    public const float Radius = 8f;

    public static Color ParentBack(Control c) =>
        c.Parent != null && c.Parent.BackColor.A == 255 ? c.Parent.BackColor : c.BackColor;
}

/// <summary>
/// Text label drawn exactly like the checkbox text (opaque background, buffered, ClearType),
/// so all text in the settings window looks equally sharp.
/// </summary>
public sealed class Txt : Label
{
    public Txt()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color bg = BackColor.A == 255 ? BackColor : (Parent?.BackColor ?? Color.Black);
        g.Clear(bg);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, bg,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

/// <summary>Rounded button (normal, accent, color swatch, preset, close).</summary>
public sealed class Pill : Button
{
    bool _hover, _down;
    public Image? Picture { get; set; }       // drawn centered instead of the text (18 x 18)
    public string IconName { get; set; } = ""; // vector icon (Icons.cs) instead of the text

    public Pill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseMnemonic = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    static Color Pick(Color c, Color fallback) => c.IsEmpty || c.A == 0 ? fallback : c;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));

        var fill = _down ? Pick(FlatAppearance.MouseDownBackColor, BackColor)
                 : _hover ? Pick(FlatAppearance.MouseOverBackColor, BackColor)
                 : BackColor;

        int bw = FlatAppearance.BorderSize;
        float inset = bw / 2f;
        var rect = new RectangleF(inset, inset, Width - bw - 0.01f, Height - bw - 0.01f);
        float radius = Math.Min(UiPaint.Radius, Height / 2f);

        using (var path = Themes.RoundRect(rect, radius))
        {
            using (var br = new SolidBrush(fill))
                g.FillPath(br, path);
            if (bw > 0)
            {
                using var pen = new Pen(Pick(FlatAppearance.BorderColor, ForeColor), bw);
                g.DrawPath(pen, path);
            }
        }

        var icon = new RectangleF((Width - 18) / 2f, (Height - 18) / 2f, 18, 18);
        if (Picture != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Picture, icon);
            return;
        }
        if (IconName.Length > 0)
        {
            Icons.Draw(g, IconName, icon, ForeColor, 1.8f);
            return;
        }

        // opaque back color -> ClearType (sharp) text instead of aliased text
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, fill,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Page entry in the sidebar: rounded highlight, accent bar and icon.</summary>
public sealed class NavButton : Button
{
    bool _hover;
    public bool Selected { get; set; }
    public Color AccentColor { get; set; } = Color.DodgerBlue;
    public string IconName { get; set; } = "";

    public NavButton(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        UseMnemonic = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Size = new Size(210, 40);
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));

        var rect = new RectangleF(10, 2, Width - 20, Height - 4);
        Color? fill = Selected ? BackColor
                    : _hover ? FlatAppearance.MouseOverBackColor
                    : null;

        if (fill.HasValue && fill.Value.A != 0)
        {
            using var path = Themes.RoundRect(rect, UiPaint.Radius);
            using var br = new SolidBrush(fill.Value);
            g.FillPath(br, path);
        }

        if (Selected)
        {
            using var bar = Themes.RoundRect(new RectangleF(13, Height / 2f - 8, 3, 16), 1.5f);
            using var br = new SolidBrush(AccentColor);
            g.FillPath(br, bar);
        }

        bool hasIcon = IconName.Length > 0;
        if (hasIcon)
            Icons.Draw(g, IconName, new RectangleF(26, Height / 2f - 9, 18, 18),
                Selected ? AccentColor : ForeColor, 1.8f);

        int textLeft = hasIcon ? 56 : 30;
        Color textBack = fill.HasValue && fill.Value.A != 0 ? fill.Value : UiPaint.ParentBack(this);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(textLeft, 0, Width - textLeft - 10, Height), ForeColor, textBack,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Fluent switch. SwitchRight = false: switch, then the text (grids);
/// SwitchRight = true: text on the left, switch at the right edge (full-width rows).
/// </summary>
public sealed class ModernCheck : CheckBox
{
    bool _hover;
    public Color Accent { get; set; } = Color.DodgerBlue;
    public Color BoxFill { get; set; } = Color.FromArgb(40, 40, 46);
    public Color BoxBorder { get; set; } = Color.Gray;
    public bool SwitchRight { get; set; }

    public ModernCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        var parent = UiPaint.ParentBack(this);
        g.Clear(parent);

        const int tw = 38, th = 20;
        float ty = (Height - th) / 2f;
        float tx = SwitchRight ? Width - tw - 2 : 1;
        var track = new RectangleF(tx, ty, tw, th);
        var accent = Enabled ? Accent : Themes.Blend(Accent, parent, 0.6);
        using (var path = Themes.RoundRect(track, th / 2f))
        {
            using (var br = new SolidBrush(Checked ? accent : BoxFill))
                g.FillPath(br, path);
            using var pen = new Pen(Checked ? accent : _hover && Enabled ? Accent : BoxBorder, 1.2f);
            g.DrawPath(pen, path);
        }
        float k = th - 8;
        float kx = Checked ? track.Right - 4 - k : track.Left + 4;
        using (var knob = new SolidBrush(Checked ? Themes.Contrast(accent) : Themes.Blend(BoxBorder, ForeColor, 0.5)))
            g.FillEllipse(knob, kx, ty + 4, k, k);

        var textRect = SwitchRight
            ? new Rectangle(0, 0, Width - tw - 14, Height)
            : new Rectangle(tw + 12, 0, Width - tw - 12, Height);
        TextRenderer.DrawText(g, Text, Font, textRect,
            Enabled ? ForeColor : Themes.Blend(ForeColor, parent, 0.5), parent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Rounded settings card (Fluent). Its BackColor is the card color, so child controls blend in;
/// the area outside the rounded corners is painted in the parent's color.
/// </summary>
public sealed class Card : Panel
{
    public Color Border { get; set; } = Color.FromArgb(44, 49, 60);
    public Color Divider { get; set; } = Color.FromArgb(36, 40, 49);
    public readonly List<int> Dividers = new();   // y positions of the lines between rows
    public int NextY;                             // layout cursor while the card is built
    public int Rows;                              // rows added so far (the first row gets no divider)

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(Parent?.BackColor ?? BackColor);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Themes.RoundRect(rect, 12f))
        {
            using (var br = new SolidBrush(BackColor))
                g.FillPath(br, path);
            using var pen = new Pen(Border, 1f);
            g.DrawPath(pen, path);
        }

        g.SmoothingMode = SmoothingMode.None;
        using var line = new Pen(Divider, 1f);
        foreach (int y in Dividers)
            g.DrawLine(line, 16, y, Width - 17, y);
    }
}

/// <summary>Round color choice (theme presets): panel color with the accent as the right half.</summary>
public sealed class ColorDot : Control
{
    bool _hover;
    public Color Back { get; set; } = Color.Black;
    public Color Dot { get; set; } = Color.DodgerBlue;
    public Color Ring { get; set; } = Color.White;
    public bool Selected { get; set; }

    public ColorDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(32, 32);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));

        if (Selected || _hover)
        {
            using var ring = new Pen(Selected ? Ring : Themes.Blend(Ring, UiPaint.ParentBack(this), 0.5), 2f);
            g.DrawEllipse(ring, 1.5f, 1.5f, Width - 4f, Height - 4f);
        }
        var inner = new RectangleF(6, 6, Width - 13, Height - 13);
        using (var b = new SolidBrush(Back))
            g.FillEllipse(b, inner);
        using (var d = new SolidBrush(Dot))
            g.FillPie(d, inner.X, inner.Y, inner.Width, inner.Height, -60, 180);
    }
}

/// <summary>
/// Dark drop-down list: the closed box and the list items are drawn by the app
/// (the Windows version has a white border and a white arrow button).
/// </summary>
public sealed class ModernCombo : ComboBox
{
    public Color Accent { get; set; } = Color.DodgerBlue;
    public Color Arrow { get; set; } = Color.Gray;

    public ModernCombo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 22;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font,
            new Rectangle(0, 0, Width - 22, Height), ForeColor, BackColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        UiPaint.Smooth(g);
        float cx = Width - 9f, cy = Height / 2f;
        using var pen = new Pen(Arrow, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var back = selected ? Themes.Blend(BackColor, Accent, 0.35) : BackColor;
        using (var br = new SolidBrush(back))
            e.Graphics.FillRectangle(br, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), ForeColor, back,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

/// <summary>Number field with dark up/down buttons (painted over the white Windows buttons).</summary>
public sealed class ModernNum : NumericUpDown
{
    public Color ButtonBack { get; set; } = Color.FromArgb(40, 40, 46);
    public Color Arrow { get; set; } = Color.Gray;
    readonly Control? _spin;

    public ModernNum()
    {
        _spin = Controls.Cast<Control>().FirstOrDefault(c => c is not TextBox);
        if (_spin != null) _spin.Paint += PaintSpin;
    }

    public void Restyle() => _spin?.Invalidate();

    void PaintSpin(object? sender, PaintEventArgs e)
    {
        if (sender is not Control c) return;
        var g = e.Graphics;
        g.Clear(ButtonBack);
        UiPaint.Smooth(g);
        float cx = c.Width / 2f, half = c.Height / 2f;
        using var pen = new Pen(Arrow, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float uy = half / 2f + 0.5f, dy = half + half / 2f - 0.5f;
        g.DrawLines(pen, new[] { new PointF(cx - 3.5f, uy + 1.5f), new PointF(cx, uy - 1.5f), new PointF(cx + 3.5f, uy + 1.5f) });
        g.DrawLines(pen, new[] { new PointF(cx - 3.5f, dy - 1.5f), new PointF(cx, dy + 1.5f), new PointF(cx + 3.5f, dy - 1.5f) });
    }
}

/// <summary>Fluent slider with the value shown on the right ("100 %"). Mouse, wheel and arrow keys.</summary>
public sealed class ModernSlider : Control
{
    int _value = 100;
    bool _drag, _hover;
    const int TextW = 50;

    public int Minimum { get; set; } = 50;
    public int Maximum { get; set; } = 200;
    public int Step { get; set; } = 5;
    public string Suffix { get; set; } = " %";
    public Color Accent { get; set; } = Color.DodgerBlue;
    public Color Track { get; set; } = Color.FromArgb(50, 54, 62);
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ModernSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = 28;
    }

    RectangleF TrackRect => new(9, Height / 2f - 2, Math.Max(10, Width - TextW - 18), 4);

    float XOf(int v)
    {
        var t = TrackRect;
        return t.X + t.Width * (v - Minimum) / Math.Max(1f, Maximum - Minimum);
    }

    int ValueAt(int x)
    {
        var t = TrackRect;
        float raw = Minimum + (x - t.X) / t.Width * (Maximum - Minimum);
        return (int)Math.Round(raw / Step) * Step;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _drag = true;
        Capture = true;
        Value = ValueAt(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) Value = ValueAt(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Capture = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * Step;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= Step;
        else if (e.KeyCode is Keys.Right or Keys.Up) Value += Step;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        var parent = UiPaint.ParentBack(this);
        g.Clear(parent);

        var t = TrackRect;
        float x = XOf(_value);
        using (var path = Themes.RoundRect(t, 2))
        using (var br = new SolidBrush(Track))
            g.FillPath(br, path);
        using (var path = Themes.RoundRect(new RectangleF(t.X, t.Y, Math.Max(4, x - t.X), t.Height), 2))
        using (var br = new SolidBrush(Accent))
            g.FillPath(br, path);

        // thumb: ring in the accent color around a light knob (bigger while hovered / focused)
        float r = _hover || _drag || Focused ? 8.5f : 7.5f;
        float cy = Height / 2f;
        using (var ring = new SolidBrush(Accent))
            g.FillEllipse(ring, x - r, cy - r, r * 2, r * 2);
        using (var knob = new SolidBrush(Themes.Contrast(Accent)))
            g.FillEllipse(knob, x - r + 4, cy - r + 4, r * 2 - 8, r * 2 - 8);

        TextRenderer.DrawText(g, _value + Suffix, Font, new Rectangle(Width - TextW, 0, TextW, Height), ForeColor, parent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Thin Fluent scroll bar: a rounded thumb without arrows that gets wider while the mouse is over it.
/// Maximum = content height, LargeChange = visible height, Value = scroll position.
/// </summary>
public sealed class ModernScrollBar : Control
{
    int _value;
    bool _hover, _drag;
    int _grab;   // where the thumb was grabbed (pixels from its top)

    public int Maximum { get; set; } = 100;
    public int LargeChange { get; set; } = 10;
    public Color Thumb { get; set; } = Color.Gray;
    public Color ThumbHover { get; set; } = Color.Silver;
    public event EventHandler? Scroll;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, 0, Math.Max(0, Maximum - LargeChange));
            if (v == _value) return;
            _value = v;
            Invalidate();
            Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    public ModernScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Width = 12;
    }

    int Range => Math.Max(0, Maximum - LargeChange);

    Rectangle ThumbRect()
    {
        if (Maximum <= 0 || Range == 0) return Rectangle.Empty;
        int track = Height - 8;
        int h = Math.Max(36, (int)((long)track * LargeChange / Maximum));
        int y = 4 + (int)((long)(track - h) * _value / Range);
        return new Rectangle(0, y, Width, h);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var t = ThumbRect();
        if (t.IsEmpty) return;
        if (t.Contains(e.Location))
        {
            _drag = true;
            _grab = e.Y - t.Y;
            Capture = true;
        }
        else
            Value += (e.Y < t.Y ? -1 : 1) * (int)(LargeChange * 0.9);   // click on the track: one page
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drag) return;
        var t = ThumbRect();
        int free = Math.Max(1, Height - 8 - t.Height);
        Value = (int)((long)(e.Y - _grab - 4) * Range / free);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));
        var t = ThumbRect();
        if (t.IsEmpty) return;
        float w = _hover || _drag ? 6f : 3f;
        var r = new RectangleF(Width - w - 3, t.Y, w, t.Height);
        using var path = Themes.RoundRect(r, w / 2f);
        using var br = new SolidBrush(_hover || _drag ? ThumbHover : Thumb);
        g.FillPath(br, path);
    }
}

/// <summary>Rounded container for a TextBox / NumericUpDown / ComboBox (focus = accent border).</summary>
public sealed class Host : Panel
{
    bool _focus;
    public Color Fill { get; set; } = Color.FromArgb(40, 40, 46);
    public Color Border { get; set; } = Color.Gray;
    public Color Accent { get; set; } = Color.DodgerBlue;

    public Host()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 32;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control == null) return;
        e.Control.Enter += (_, _) => { _focus = true; Invalidate(); };
        e.Control.Leave += (_, _) => { _focus = false; Invalidate(); };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        foreach (Control c in Controls)
        {
            int top = Math.Max(0, (Height - c.Height) / 2);
            if (c.Left != 10 || c.Top != top || c.Width != Width - 20)
            {
                c.Left = 10;
                c.Top = top;
                c.Width = Width - 20;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Themes.RoundRect(rect, UiPaint.Radius * 0.9f);
        using (var br = new SolidBrush(Fill))
            g.FillPath(br, path);
        using var pen = new Pen(_focus ? Accent : Border, _focus ? 1.6f : 1f);
        g.DrawPath(pen, path);
    }
}

/// <summary>Expandable changelog entry: click the header to show / hide the change list.</summary>
public sealed class Collapsible : Control
{
    const int HeaderH = 42;
    const TextFormatFlags BodyFlags = TextFormatFlags.Left | TextFormatFlags.Top |
                                      TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    readonly ChangelogEntry _entry;
    bool _open;
    bool _hover;

    public Color Fill { get; set; } = Color.FromArgb(40, 40, 46);
    public Color Border { get; set; } = Color.Gray;
    public Color Accent { get; set; } = Color.DodgerBlue;
    public Color TextColor { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;
    public string? Title { get; set; }   // header text instead of "Version x" (FAQ: the question)

    public Collapsible(ChangelogEntry entry, bool open)
    {
        _entry = entry;
        _open = open;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Width = 600;
        Margin = new Padding(0, 0, 0, 10);
        UpdateHeight();
    }

    int TextWidth => Math.Max(50, Width - 28 - 26);

    int[] LineHeights() =>
        _entry.Items.Select(t =>
            Math.Max(Font.Height, TextRenderer.MeasureText(t, Font, new Size(TextWidth, 0), BodyFlags).Height) + 6).ToArray();

    void UpdateHeight()
    {
        int h = HeaderH;
        if (_open) h += LineHeights().Sum() + 18;
        if (Height != h) Height = h;
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateHeight(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateHeight(); Invalidate(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateHeight(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool h = e.Y < HeaderH;
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.Y >= HeaderH) return;
        _open = !_open;
        UpdateHeight();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UiPaint.Smooth(g);
        g.Clear(UiPaint.ParentBack(this));

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var cardBack = _hover ? Themes.Blend(Fill, TextColor, 0.05) : Fill;
        using (var path = Themes.RoundRect(rect, UiPaint.Radius))
        {
            using (var br = new SolidBrush(cardBack))
                g.FillPath(br, path);
            using var pen = new Pen(_open || _hover ? Accent : Border, 1f);
            g.DrawPath(pen, path);
        }

        // chevron
        float cx = 24, cy = HeaderH / 2f;
        using (var pen = new Pen(Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            if (_open)
                g.DrawLines(pen, new[] { new PointF(cx - 5, cy - 2), new PointF(cx, cy + 3), new PointF(cx + 5, cy - 2) });
            else
                g.DrawLines(pen, new[] { new PointF(cx - 2, cy - 5), new PointF(cx + 3, cy), new PointF(cx - 2, cy + 5) });
        }

        using (var bold = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold))
            TextRenderer.DrawText(g, Title ?? "Version " + _entry.Version, bold,
                new Rectangle(44, 0, Width - 44 - (_entry.Date.Length > 0 ? 120 : 14), HeaderH), TextColor, cardBack,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, _entry.Date, Font,
            new Rectangle(Width - 130, 0, 116, HeaderH), Muted, cardBack,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (!_open) return;

        using (var sep = new Pen(Border))
            g.DrawLine(sep, 14, HeaderH, Width - 14, HeaderH);

        int y = HeaderH + 10;
        var heights = LineHeights();
        for (int i = 0; i < _entry.Items.Length; i++)
        {
            using (var dot = new SolidBrush(Accent))
                g.FillEllipse(dot, 18, y + Font.Height / 2f - 2f, 5, 5);
            TextRenderer.DrawText(g, _entry.Items[i], Font,
                new Rectangle(34, y, TextWidth, heights[i]), TextColor, cardBack, BodyFlags);
            y += heights[i];
        }
    }
}
