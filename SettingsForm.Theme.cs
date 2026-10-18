using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Colors of the window and all its controls (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Theme
    // ------------------------------------------------------------
    // background behind a label: the card it sits on, the sidebar / preview panel, or the page
    Color LabelBack(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
        {
            if (p is Card card) return card.BackColor;
            if (p.Tag is "side") return CBack;
        }
        return CPanel;
    }

    void ApplyTheme()
    {
        Themes.DarkUi = Themes.IsDark(CBack);
        BackColor = CBack;
        ForeColor = CText;
        ThemeAll(this);
        RefreshNav();
        UpdateSwatches();
        UpdateLogo();
        UpdatePriceStatus();
        if (IsHandleCreated) Themes.RoundCorners(Handle, CBorder);
        Invalidate(true);
    }

    // Hover hints in the colors of the window instead of the white Windows tooltip
    readonly Font _tipFont = UiFont.Text(9f);
    const int TipPadX = 10, TipPadY = 6;

    void StyleTips()
    {
        _tips.OwnerDraw = true;
        _tips.InitialDelay = 400;
        _tips.Popup += (_, e) =>
        {
            string text = (e.AssociatedControl != null ? _tips.GetToolTip(e.AssociatedControl) : null) ?? "";
            var sz = TextRenderer.MeasureText(text, _tipFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(sz.Width + TipPadX * 2, sz.Height + TipPadY * 2);
        };
        _tips.Draw += (_, e) =>
        {
            var g = e.Graphics;
            var back = Themes.Blend(CPanel, CText, 0.07);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(CBorder)) g.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(g, e.ToolTipText, _tipFont,
                new Rectangle(TipPadX, 0, e.Bounds.Width - TipPadX * 2, e.Bounds.Height), CText, back,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
    }

    void UpdateLogo()
    {
        var oldImg = _logo.Image;
        _logo.Image = Themes.Logo(128, CAccent);
        oldImg?.Dispose();

        var oldAbout = _aboutLogo.Image;
        _aboutLogo.Image = Themes.Logo(128, CAccent);
        oldAbout?.Dispose();

        var oldIcon = _ownIcon;
        _ownIcon = Themes.LogoIcon(CAccent);
        Icon = _ownIcon;
        oldIcon?.Dispose();
    }

    void ThemeAll(Control root)
    {
        foreach (Control c in root.Controls)
        {
            ThemeOne(c);
            ThemeAll(c);
        }
    }

    void ThemeOne(Control c)
    {
        switch (c)
        {
            case NavButton:
                break; // handled by RefreshNav

            case Button b when b.Tag is "swatch":
                break; // colored explicitly in UpdateSwatches

            case Button b when b.Tag is "chrome":
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = CPanel;
                b.ForeColor = CMuted;
                b.FlatAppearance.MouseOverBackColor = Themes.Blend(CPanel, CText, 0.10);
                b.FlatAppearance.MouseDownBackColor = Themes.Blend(CPanel, CText, 0.16);
                b.Invalidate();
                break;

            case Button b when b.Tag is "close":
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = CPanel;
                b.ForeColor = CMuted;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(190, 12, 28);
                b.Invalidate();
                break;

            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.Cursor = Cursors.Hand;
                b.FlatAppearance.BorderSize = 1;
                if (b.Tag is "accent")
                {
                    b.BackColor = CAccent;
                    b.ForeColor = Themes.Contrast(CAccent);
                    b.FlatAppearance.BorderColor = CAccent;
                    b.FlatAppearance.MouseOverBackColor = Themes.Blend(CAccent, Color.White, 0.18);
                    b.FlatAppearance.MouseDownBackColor = Themes.Blend(CAccent, Color.Black, 0.18);
                }
                else
                {
                    b.BackColor = CInput;
                    b.ForeColor = CText;
                    b.FlatAppearance.BorderColor = CBorder;
                    b.FlatAppearance.MouseOverBackColor = Themes.Blend(CInput, CText, 0.10);
                    b.FlatAppearance.MouseDownBackColor = Themes.Blend(CInput, CText, 0.18);
                }
                b.Invalidate();
                break;

            case ModernCheck cb:
                cb.Accent = CAccent;
                cb.BoxFill = CInput;
                cb.BoxBorder = Themes.Blend(CPanel, CText, 0.45);   // clearly visible even when off
                cb.BackColor = Color.Transparent;
                cb.ForeColor = CText;
                cb.Invalidate();
                break;

            case ModernCombo mc:
                mc.BackColor = CInput;
                mc.ForeColor = CText;
                mc.Accent = CAccent;
                mc.Arrow = CMuted;
                mc.Invalidate();
                break;

            case ModernNum mn:
                mn.BorderStyle = BorderStyle.None;
                mn.BackColor = CInput;
                mn.ForeColor = CText;
                mn.ButtonBack = CInput;
                mn.Arrow = CMuted;
                mn.Restyle();
                break;

            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = CInput;
                combo.ForeColor = CText;
                break;

            case NumericUpDown n:
                n.BorderStyle = BorderStyle.None;
                n.BackColor = CInput;
                n.ForeColor = CText;
                break;

            case TextBox t:
                t.BorderStyle = BorderStyle.None;
                t.BackColor = CInput;
                t.ForeColor = CText;
                break;

            case Collapsible col:
                col.Fill = CardOn(CPanel);
                col.Border = CBorder;
                col.Accent = CAccent;
                col.TextColor = CText;
                col.Muted = CMuted;
                col.Invalidate();
                break;

            case Host h:
                h.Fill = CInput;
                h.Border = CBorder;
                h.Accent = CAccent;
                h.Invalidate();
                break;

            case Card card:
                card.BackColor = CardOn(LabelBack(card));
                card.Border = Themes.Blend(LabelBack(card), CText, 0.13);
                card.Divider = Themes.Blend(card.BackColor, CText, 0.07);
                card.Invalidate();
                break;

            case HistoryList hl:
                hl.BackColor = CardOn(CPanel);
                hl.TextColor = CText;
                hl.Muted = CMuted;
                hl.Hover = Themes.Blend(hl.BackColor, CText, 0.06);
                hl.Tile = Themes.Blend(hl.BackColor, CText, 0.07);
                hl.Line = Themes.Blend(hl.BackColor, CText, 0.07);
                hl.Invalidate();
                break;

            case OverlayPreview pv:
                pv.BackColor = Themes.Blend(CBack, Color.Black, 0.35);
                pv.Grid = Themes.Blend(pv.BackColor, CText, 0.08);
                pv.Accent = CAccent;
                pv.HintColor = Themes.Blend(pv.BackColor, CText, 0.32);
                pv.TextColor = CText;
                pv.ChipBack = Themes.Blend(pv.BackColor, CText, 0.08);
                pv.Invalidate();
                break;

            case ColorDot:
                break; // colored in UpdateSwatches

            case ModernSlider sl:
                sl.Accent = CAccent;
                sl.Track = Themes.Blend(CPanel, CText, 0.16);
                sl.ForeColor = CText;
                sl.Invalidate();
                break;

            case Label l:
                // opaque background (not Transparent): only then Windows draws sharp ClearType text
                l.BackColor = LabelBack(l);
                switch (l.Tag as string)
                {
                    case "muted":
                    case "hint":
                        l.ForeColor = CMuted;
                        break;
                    case "section":
                    case "saved":
                        l.ForeColor = CAccent;
                        break;
                    case "tier":
                    case "dot":
                        break; // colored in UpdateSwatches / UpdatePriceStatus
                    default:
                        l.ForeColor = CText;
                        break;
                }
                break;

            case PictureBox:
                break;

            case Panel p:
                p.BackColor = p.Tag is "side" ? CBack : CPanel;
                break;
        }
    }

    void UpdateSwatches()
    {
        UpdateLive();
        ScheduleSave();   // colors / theme changed (nothing happens while the window is being filled)
        SetSwatch(_btnBack, _backArgb);
        SetSwatch(_btnText, _textArgb);
        SetSwatch(_thBackBtn, _thBack);
        SetSwatch(_thPanelBtn, _thPanel);
        SetSwatch(_thTextBtn, _thText);
        SetSwatch(_thAccentBtn, _thAccent);

        for (int i = 0; i < 5; i++)
        {
            if (_tierBtn[i] != null) SetSwatch(_tierBtn[i], _tierColors[i]);
            if (_tierLbl[i] != null) _tierLbl[i].ForeColor = Themes.FromArgb(_tierColors[i]);
        }

        bool known = false;
        foreach (var d in _presetDots)
        {
            if (d.Tag is not ThemePreset pr) continue;
            bool active = string.Equals(_preset, pr.Name, StringComparison.OrdinalIgnoreCase);
            known |= active;
            d.Back = Themes.FromArgb(pr.Panel);
            d.Dot = Themes.FromArgb(pr.Accent);
            d.Ring = CText;
            d.Selected = active;
            d.Invalidate();
        }
        _presetName.Text = known ? L.T("Theme:") + " " + _preset : L.T("Custom colors");
    }

    void SetSwatch(Button b, int argb)
    {
        var c = Themes.FromArgb(argb);
        b.BackColor = c;
        b.ForeColor = Themes.Contrast(c);
        b.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        b.FlatAppearance.BorderColor = CBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = c;
        b.FlatAppearance.MouseDownBackColor = c;
        b.Invalidate();
    }

    ColorPickerForm? _picker;

    // Opens the color picker under the button; every change is applied (and saved) right away
    void Pick(Control anchor, Func<int> get, Action<int> set, int standard, Action? after = null)
    {
        _picker?.Close();
        var f = new ColorPickerForm(Themes.FromArgb(get()), CPanel, CText, CBorder, CAccent, Themes.FromArgb(standard));
        f.ColorChanged += c =>
        {
            if (c.ToArgb() == get()) return;
            set(c.ToArgb());
            if (after != null) after(); else UpdateSwatches();
        };
        f.FormClosed += (_, _) => { if (_picker == f) _picker = null; };

        var at = anchor.PointToScreen(new Point(0, anchor.Height + 6));
        var area = Screen.FromControl(anchor).WorkingArea;
        if (at.Y + f.Height > area.Bottom) at.Y = anchor.PointToScreen(Point.Empty).Y - f.Height - 6;
        at.X = Math.Clamp(at.X, area.Left, area.Right - f.Width);
        at.Y = Math.Clamp(at.Y, area.Top, area.Bottom - f.Height);
        f.Location = at;
        _picker = f;
        f.Show(this);
        f.Activate();
    }
}
