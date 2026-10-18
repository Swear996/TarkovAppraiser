using System.Diagnostics;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Welcome window on the first start: how to use the hotkey, whether the Windows text recognition
//  for the game language is installed (without it nothing can be read), and a test price box.
// ------------------------------------------------------------
sealed class WelcomeForm : Form
{
    const int W = 540, Pad = 28, CardW = W - 2 * Pad;

    public bool OpenSettings { get; private set; }

    readonly Action<AppSettings> _test;
    readonly AppSettings _s;
    readonly Color _panel, _text, _muted, _accent, _card, _border;
    readonly Icon _icon;
    readonly Bitmap _logo;
    bool _regionFallback;

    public WelcomeForm(AppSettings s, Action<AppSettings> test)
    {
        _s = s;
        _test = test;
        _panel = Themes.FromArgb(s.ThemePanelArgb);
        _text = Themes.FromArgb(s.ThemeTextArgb);
        _accent = Themes.FromArgb(s.ThemeAccentArgb);
        _muted = Themes.Blend(_text, _panel, 0.4);
        _card = Themes.Blend(_panel, _text, 0.045);
        _border = Themes.Blend(_panel, _text, 0.14);

        Text = "Tarkov Appraiser";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Font = UiFont.Text(9.5f);
        BackColor = _panel;
        _icon = Themes.LogoIcon(_accent);
        Icon = _icon;
        _logo = Themes.Logo(128, _accent);

        int y = 28;
        Controls.Add(new PictureBox
        {
            Image = _logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
            Left = Pad, Top = y, Width = 56, Height = 56
        });
        Controls.Add(Line(L.T("Welcome to Tarkov Appraiser"), Pad + 70, y + 4, CardW - 70, 32, UiFont.Semi(16f), _text));
        Controls.Add(Line(L.T("Prices for your Tarkov loot at a glance."), Pad + 70, y + 38, CardW - 70, 20, null, _muted));
        y += 84;

        // 1 - hotkey
        string key = AppSettings.HotkeyText(s.HotkeyMods, s.HotkeyVk);
        y = Step(y, "1", L.T("Show prices"),
            string.Format(L.T("Hover an item in your stash or in a raid and press {0}. The price box appears next to the mouse."), key),
            null, null, null);

        // 2 - automatic mode: prices without pressing a key
        var auto = new ModernCheck
        {
            Text = L.T("Turn on automatic mode"),
            Checked = s.AutoHover,
            Accent = _accent,
            BoxFill = Themes.Blend(_card, _text, 0.08),
            BoxBorder = Themes.Blend(_card, _text, 0.45),
            ForeColor = _text,
            BackColor = Color.Transparent,
            SwitchRight = false,
            Width = 300, Height = 28
        };
        auto.CheckedChanged += (_, _) => _s.AutoHover = auto.Checked;   // saved when the window closes
        y = Step(y, "2", L.T("Automatic mode"),
            L.T("Or without a key: the prices appear as soon as the mouse rests on an item. Pause it any time with Ctrl+F8."),
            null, null, null, auto);

        // 3 - text recognition for the game language
        bool ok = OcrInstalled(s.OcrLanguage, out string langName);
        y = Step(y, "3", L.T("Text recognition"),
            ok ? string.Format(L.T("Installed: {0}. Tarkov Appraiser can read the item names."), langName)
               : string.Format(L.T("Missing: {0}. Add it in Windows Settings > Time & Language > Language (with \"Text recognition\"), then restart Tarkov Appraiser."), langName),
            ok ? "✓" : "⚠", ok ? Color.FromArgb(76, 195, 138) : Color.FromArgb(224, 165, 38),
            ok ? null : (L.T("Open language settings"), () => Open("ms-settings:regionlanguage")));

        // 4 - try it
        y = Step(y, "4", L.T("Try it"), L.T("Shows a sample price box at your mouse."), null, null,
            (L.T("Show test price box"), () => _test(_s)));

        // footer
        y += 8;
        var go = MakeButton(L.T("Let's go"), true);
        go.Left = W - Pad - go.Width;
        go.Top = y;
        go.Click += (_, _) => Close();
        var settings = MakeButton(L.T("Open settings"), false);
        settings.Left = go.Left - 8 - settings.Width;
        settings.Top = y;
        settings.Click += (_, _) => { OpenSettings = true; Close(); };
        Controls.Add(go);
        Controls.Add(settings);
        AcceptButton = go;
        ClientSize = new Size(W, y + 34 + 24);
    }

    // one numbered step in a card; returns the y below it
    int Step(int y, string number, string title, string text, string? mark, Color? markColor,
             (string Text, Action Click)? action, Control? extra = null)
    {
        var card = new Card { Left = Pad, Top = y, Width = CardW, BackColor = _card, Border = _border };
        card.Controls.Add(Line(number, 16, 14, 22, 26, UiFont.Semi(13f), _accent, _card));
        card.Controls.Add(Line(title, 46, 16, CardW - 100, 22, UiFont.Semi(10.5f), _text, _card));
        if (mark != null)
            card.Controls.Add(Line(mark, CardW - 40, 14, 24, 24, UiFont.Semi(12f), markColor ?? _text, _card));

        int textH = TextRenderer.MeasureText(text, Font, new Size(CardW - 62, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 2;
        card.Controls.Add(Line(text, 46, 42, CardW - 62, textH, null, _muted, _card));
        int h = 42 + textH + 14;
        if (action is { } a)
        {
            var b = MakeButton(a.Text, false);
            b.Left = 46;
            b.Top = h - 4;
            b.Click += (_, _) => a.Click();
            card.Controls.Add(b);
            h += 34 + 10;
        }
        if (extra != null)
        {
            extra.Left = 46;
            extra.Top = h - 6;
            card.Controls.Add(extra);
            h += extra.Height + 8;
        }
        card.Height = h;
        Controls.Add(card);
        return y + h + 12;
    }

    Txt Line(string text, int x, int y, int w, int h, Font? font, Color color, Color? back = null)
    {
        var t = new Txt
        {
            Text = text, Left = x, Top = y, Width = w, Height = h,
            ForeColor = color, BackColor = back ?? _panel, UseMnemonic = false
        };
        if (font != null) t.Font = font;
        return t;
    }

    Pill MakeButton(string text, bool accent)
    {
        var input = Themes.Blend(_panel, _text, 0.08);
        var b = new Pill
        {
            Text = text,
            Width = Math.Max(110, TextRenderer.MeasureText(text, Font).Width + 36),
            Height = 34,
            BackColor = accent ? _accent : input,
            ForeColor = accent ? Themes.Contrast(_accent) : _text,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = accent ? _accent : _border;
        b.FlatAppearance.MouseOverBackColor = accent ? Themes.Blend(_accent, Color.White, 0.18) : Themes.Blend(input, _text, 0.10);
        b.FlatAppearance.MouseDownBackColor = accent ? Themes.Blend(_accent, Color.Black, 0.18) : Themes.Blend(input, _text, 0.18);
        return b;
    }

    static bool OcrInstalled(string tag, out string name)
    {
        name = tag;
        try
        {
            foreach (var l in OcrEngine.AvailableRecognizerLanguages)
                if (string.Equals(l.LanguageTag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    name = $"{l.DisplayName} ({l.LanguageTag})";
                    return true;
                }
        }
        catch (Exception ex)
        {
            Log.Write("OCR language check: " + ex.Message);
        }
        return false;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Could not open " + target + ": " + ex.Message); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _regionFallback = !Themes.RoundCorners(Handle, _border);
        if (_regionFallback)
        {
            using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 12);
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_regionFallback) return;   // Windows 11 draws the border itself
        UiPaint.Smooth(e.Graphics);
        using var path = Themes.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12);
        using var pen = new Pen(_border);
        e.Graphics.DrawPath(pen, path);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _logo.Dispose();
        }
        base.Dispose(disposing);
    }
}
