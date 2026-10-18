using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Card layout helpers (absolute positioning inside a card) (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Card layout helpers (absolute positioning inside a card)
    // ------------------------------------------------------------

    // Wraps an input control in a rounded host
    static Host Hosted(Control c, int left, int top, int width)
    {
        var h = new Host { Left = left, Top = top, Width = width, Height = 32 };
        h.Controls.Add(c);
        return h;
    }

    static Txt Lbl(string text, int left, int top, int width, string tag = "label", float size = 0, bool semi = false)
    {
        var t = new Txt
        {
            Text = L.T(text),
            Left = left, Top = top, Width = width, Height = 20,
            UseMnemonic = false, Tag = tag
        };
        if (size > 0)
        {
            t.Font = semi ? UiFont.Semi(size) : UiFont.Text(size);
            t.Height = Math.Max(18, t.Font.Height + 4);
        }
        return t;
    }

    // New card at the current position of the page; title = null: card without a title
    static Card NewCard(PagePanel p, string? title)
    {
        var c = new Card { Left = CardX, Top = p.Y, Width = CardW, Height = 60 };
        if (title != null)
        {
            c.Controls.Add(Stretch(Lbl(title, CardPad, 13, CardW - 2 * CardPad, "title", 10.5f, semi: true)));
            c.NextY = 42;
        }
        else c.NextY = 12;
        p.Body.Controls.Add(c);
        return c;
    }

    static void Done(PagePanel p, Card c)
    {
        c.Height = c.NextY + 6;
        p.Y += c.Height + 14;
    }

    // a line between this row and the row above (not above the first row)
    static void StartRow(Card c)
    {
        if (c.Rows > 0) c.Dividers.Add(c.NextY);
        c.Rows++;
    }

    // Controls inside a card follow its width (cards grow with the window):
    // Stretch = grows with the card, AtRight = stays at the right edge
    static Control Stretch(Control c) { c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; return c; }
    static Control AtRight(Control c) { c.Anchor = AnchorStyles.Top | AnchorStyles.Right; return c; }

    // returns the placed control (or its rounded host)
    static Control Place(Card c, Control ctl, int x, int width, int rowH)
    {
        Control placed;
        if (ctl is ComboBox or NumericUpDown or TextBox)
            placed = Hosted(ctl, x, c.NextY + (rowH - 32) / 2, width);
        else
        {
            ctl.SetBounds(x, c.NextY + (rowH - 28) / 2, width, 28);
            placed = ctl;
        }
        c.Controls.Add(placed);
        return placed;
    }

    // label (and an optional small hint below it) on the left, control on the right
    static void Row(Card c, string label, Control ctl, int width, string? hint = null)
    {
        StartRow(c);
        int h = hint == null ? RowH : RowH + 12;
        int labelW = CardW - 2 * CardPad - width - 12;
        c.Controls.Add(Stretch(Lbl(label, CardPad, c.NextY + (hint == null ? 12 : 8), labelW)));
        if (hint != null)
            c.Controls.Add(Stretch(Lbl(hint, CardPad, c.NextY + 28, labelW, "muted", 8.5f)));
        AtRight(Place(c, ctl, CardW - CardPad - width, width, h));
        c.NextY += h;
    }

    // two label + control pairs side by side
    static void Row2(Card c, string l1, Control c1, string l2, Control c2, int ctlW = 84)
    {
        StartRow(c);
        int half = (CardW - 2 * CardPad - 24) / 2;
        var lab1 = Lbl(l1, CardPad, c.NextY + 12, half - ctlW - 8);
        c.Controls.Add(lab1);
        var p1 = Place(c, c1, CardPad + half - ctlW, ctlW, RowH);
        int x2 = CardPad + half + 24;
        var lab2 = Lbl(l2, x2, c.NextY + 12, half - ctlW - 8);
        c.Controls.Add(lab2);
        var p2 = Place(c, c2, x2 + half - ctlW, ctlW, RowH);
        c.NextY += RowH;

        // each pair takes one half of the card, whatever its width
        c.Resize += (_, _) =>
        {
            int h = (c.Width - 2 * CardPad - 24) / 2;
            lab1.Width = h - ctlW - 8;
            p1.Left = CardPad + h - ctlW;
            lab2.Left = CardPad + h + 24;
            lab2.Width = h - ctlW - 8;
            p2.Left = CardPad + 2 * h + 24 - ctlW;
        };
    }

    // full-width switch: text on the left, switch on the right
    static void Switch(Card c, ModernCheck cb, string? hint = null)
    {
        StartRow(c);
        cb.Text = L.T(cb.Text);
        cb.SwitchRight = true;
        cb.UseMnemonic = false;
        if (hint == null)
        {
            cb.SetBounds(CardPad, c.NextY + 8, CardW - 2 * CardPad, 28);
            c.Controls.Add(Stretch(cb));
            c.NextY += RowH;
            return;
        }
        cb.SetBounds(CardPad, c.NextY + 4, CardW - 2 * CardPad, 26);
        c.Controls.Add(Stretch(cb));
        c.Controls.Add(Stretch(Lbl(hint, CardPad, c.NextY + 29, CardW - 2 * CardPad - 60, "muted", 8.5f)));
        c.NextY += RowH + 12;
    }

    // compact grid of switches in two columns
    static void Switches(Card c, params ModernCheck[] cbs)
    {
        StartRow(c);
        int colW = (CardW - 2 * CardPad - 16) / 2;
        int y = c.NextY + 6;
        for (int i = 0; i < cbs.Length; i++)
        {
            cbs[i].Text = L.T(cbs[i].Text);
            cbs[i].SwitchRight = false;
            cbs[i].UseMnemonic = false;
            cbs[i].SetBounds(CardPad + (i % 2) * (colW + 16), y + (i / 2) * 34, colW, 28);
            c.Controls.Add(cbs[i]);
        }
        c.NextY = y + (cbs.Length + 1) / 2 * 34 + 4;

        // two equal columns, whatever the card width
        c.Resize += (_, _) =>
        {
            int w = (c.Width - 2 * CardPad - 16) / 2;
            for (int i = 0; i < cbs.Length; i++)
            {
                cbs[i].Left = CardPad + (i % 2) * (w + 16);
                cbs[i].Width = w;
            }
        };
    }

    // small muted text that belongs to the card (no divider)
    static void Note(Card c, string text, int height)
    {
        c.Controls.Add(new Txt
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = L.T(text),
            Font = UiFont.Text(8.5f),
            Left = CardPad, Top = c.NextY + 2, Width = CardW - 2 * CardPad, Height = height,
            UseMnemonic = false, Tag = "hint"
        });
        c.NextY += height + 10;
    }

    static void Buttons(Card c, params Button[] buttons)
    {
        StartRow(c);
        int x = CardPad;
        foreach (var b in buttons)
        {
            b.Left = x;
            b.Top = c.NextY + 8;
            c.Controls.Add(b);
            x += b.Width + 10;
        }
        c.NextY += 34 + 16;
    }
}
