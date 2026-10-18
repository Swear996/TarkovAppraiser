using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Live preview of the overlay (right side, Overlay page) and change tracking (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Live preview (right side)
    // ------------------------------------------------------------
    void BuildLivePanel()
    {
        _livePanel.Controls.Add(Lbl("Live preview", 0, 2, 220, "title", 10.5f, semi: true));
        _livePanel.Controls.Add(_live);

        _liveNext.Click += (_, _) => { _liveIndex++; UpdateLive(); };
        _livePanel.Controls.Add(_liveNext);

        _liveTest.Click += (_, _) => _preview(Collect());
        _tips.SetToolTip(_liveTest, L.T("Shows the real overlay at your mouse"));
        _livePanel.Controls.Add(_liveTest);

        // drag & drop in the preview: new order of the parts / side of the picture
        _live.LayoutChanged += edit =>
        {
            _order = edit.Order;
            _imageSide = edit.ImageSide;
            // dragged into / out of the tray: same as the switch in "What to show"
            _populating = true;   // one save for the whole drop
            try
            {
                if (edit.Hide != null) SetPart(edit.Hide, false);
                if (edit.Show != null) SetPart(edit.Show, true);
            }
            finally { _populating = false; }
            Changed();
        };
        var reset = new Pill { Tag = "button", IconName = "reload", TabStop = false };
        _tips.SetToolTip(reset, L.T("Reset the layout"));
        reset.Click += (_, _) =>
        {
            _order = OverlayPainter.DefaultOrder.ToList();
            _imageSide = "Left";
            Changed();
        };
        _livePanel.Controls.Add(reset);

        // preview fills the panel, buttons below it
        _livePanel.Resize += (_, _) =>
        {
            int w = _livePanel.ClientSize.Width - 24;
            int bottom = _livePanel.ClientSize.Height;
            if (w < 100 || bottom < 200) return;   // minimized
            _live.SetBounds(0, 30, w, Math.Max(120, bottom - 30 - 56));
            int bw = (w - 34 - 16) / 2;
            _liveNext.SetBounds(0, _live.Bottom + 12, bw, 34);
            _liveTest.SetBounds(bw + 8, _live.Bottom + 12, bw, 34);
            reset.SetBounds(w - 34, _live.Bottom + 12, 34, 34);
        };
    }

    // switch that belongs to a part of the overlay (drag & drop tray)
    void SetPart(string key, bool on)
    {
        switch (key)
        {
            case "image": _showItemImg.Checked = on; break;
            case "ammo": _showAmmo.Checked = on; break;
            case "armor": _showArmor.Checked = on; break;
            case "key": _showKey.Checked = on; break;
            case "use": _showUses.Checked = on; break;
            case "flea": _showLow.Checked = on; break;
            case "net": _showNet.Checked = on; break;
            case "avg": _showAvg.Checked = on; break;
            case "best": _showBestOpt.Checked = on; break;
            case "trader":
                if (on) _showBest.Checked = true;
                else { _showBest.Checked = false; _showAll.Checked = false; }
                break;
        }
    }

    // a setting changed: refresh the preview, save shortly after
    void Changed()
    {
        UpdateLive();
        ScheduleSave();
    }

    // every change in the window refreshes the live preview and is saved
    void HookLive(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case ModernCheck cb: cb.CheckedChanged += (_, _) => Changed(); break;
                case ModernScrollBar bar:
                bar.Thumb = Themes.Blend(CPanel, CText, 0.22);
                bar.ThumbHover = Themes.Blend(CPanel, CText, 0.40);
                bar.Invalidate();
                break;

            case ModernSlider sl: sl.ValueChanged += (_, _) => Changed(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => Changed(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Changed(); break;
            }
            HookLive(c);
        }
    }

    void UpdateLive()
    {
        try
        {
            var item = MainForm.Sample(_liveIndex);
            var pic = MainForm.PictureOf(item);
            var s = Collect();
            var img = MainForm.PlaceholderFor(item, s) ?? ItemImages.TryGet(pic.Id);
            _live.Show(s, item, img);
            if (img == null && pic.Id.Length > 0)
                _ = LoadLiveImageAsync(pic.Id, pic.Url);
        }
        catch (Exception ex)
        {
            Log.Write("Live preview: " + ex.Message);
        }
    }

    readonly HashSet<string> _liveLoading = new();

    async Task LoadLiveImageAsync(string id, string? url)
    {
        if (!_liveLoading.Add(id)) return;   // already downloading
        var img = await ItemImages.GetAsync(id, url);
        if (img != null && !IsDisposed) UpdateLive();
    }
}
