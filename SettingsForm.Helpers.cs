using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Hotkey fields, export / import, dialogs, small control factories (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Events / helpers
    // ------------------------------------------------------------
    void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        // 0 = show prices, 1 = pause, 2 = stack count, 3 = history list
        int slot = sender == _pauseHotkey ? 1 : sender == _stackHotkey ? 2 : sender == _histHotkey ? 3 : 0;

        // the extra hotkeys can be switched off
        if (slot > 0 && e.KeyCode is Keys.Delete or Keys.Back)
        {
            SetHotkey(slot, 0, 0);
            ScheduleSave();
            return;
        }

        uint mods = (e.Alt ? 1u : 0u) | (e.Control ? 2u : 0u) | (e.Shift ? 4u : 0u);
        uint vk = (uint)e.KeyCode;
        bool plainKey = (vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A) || vk == 0x20 || vk == 0x0D;
        if (mods == 0 && plainKey)
        {
            // would block the key everywhere in Windows
            ShowStatus("\u26A0  " + L.T("Use an F-key or add Ctrl / Alt / Shift"), warning: true, stay: false);
            return;
        }

        // every hotkey must be different
        var all = new (uint M, uint V)[] { (_mods, _vk), (_pauseMods, _pauseVk), (_stackMods, _stackVk), (_histMods, _histVk) };
        for (int j = 0; j < all.Length; j++)
        {
            if (j != slot && all[j].V != 0 && all[j].M == mods && all[j].V == vk)
            {
                ShowStatus("\u26A0  " + L.T("This key is already used by another hotkey"), warning: true, stay: false);
                return;
            }
        }

        SetHotkey(slot, mods, vk);
        ScheduleSave();
    }

    void SetHotkey(int slot, uint mods, uint vk)
    {
        string text = vk == 0 ? "\u2013" : AppSettings.HotkeyText(mods, vk);
        switch (slot)
        {
            case 1: _pauseMods = mods; _pauseVk = vk; _pauseHotkey.Text = text; break;
            case 2: _stackMods = mods; _stackVk = vk; _stackHotkey.Text = text; break;
            case 3: _histMods = mods; _histVk = vk; _histHotkey.Text = text; break;
            default: _mods = mods; _vk = vk; _hotkey.Text = text; break;
        }
    }

    void ExportSettings()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Export settings",
            Filter = "Settings (*.json)|*.json",
            FileName = "TarkovAppraiser-settings.json"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SettingsStore.Export(Collect(), dlg.FileName);   // what the window currently shows
            Ask("Export", "Settings exported.", 0, "OK");
        }
        catch (Exception ex)
        {
            Ask("Export", L.T("Could not export:") + " " + ex.Message, 0, "OK");
        }
    }

    void ImportSettings()
    {
        using var dlg = new OpenFileDialog { Title = "Import settings", Filter = "Settings (*.json)|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Populate(SettingsStore.Import(dlg.FileName));
            SaveNow();
            Ask("Import", "Settings loaded and applied.", 0, "OK");
        }
        catch (Exception ex)
        {
            Ask("Import", L.T("This is not a valid settings file:") + "\n" + ex.Message, 0, "OK");
        }
    }

    // message / question in the design of this window; returns the clicked button (-1 = Esc)
    int Ask(string title, string text, int accentIndex, params string[] buttons) =>
        UiDialog.Show(this, new UiDialog.Palette(CBack, CPanel, CText, CAccent), L.T(title), L.T(text), accentIndex,
            buttons.Select(L.T).ToArray());

    void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + SettingsStore.Dir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Ask("Folder", L.T("Could not open the folder:") + " " + ex.Message, 0, "OK");
        }
    }

    static NumericUpDown Num(int min, int max) =>
        new ModernNum { Minimum = min, Maximum = max };

    static void SetNum(NumericUpDown n, long value) =>
        n.Value = Math.Min(n.Maximum, Math.Max(n.Minimum, value));

    static Button Swatch() =>
        new Pill { Tag = "swatch", Cursor = Cursors.Hand, Height = 28 };

    static Button MakeButton(string text, int width, bool accent = false) =>
        new Pill
        {
            Text = L.T(text),
            Width = width,
            Height = 34,
            Tag = accent ? "accent" : "button"
        };

    static void SelectChoice(ComboBox c, string value, bool addIfMissing)
    {
        foreach (var o in c.Items)
        {
            if (o is Choice ch && string.Equals(ch.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                c.SelectedItem = o;
                return;
            }
        }

        if (addIfMissing)
        {
            var missing = new Choice(value, value + " (not installed)");
            c.Items.Add(missing);
            c.SelectedItem = missing;
            return;
        }

        if (c.Items.Count > 0) c.SelectedIndex = 0;
    }
}
