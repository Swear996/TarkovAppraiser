using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Settings <-> controls, auto-save, closing (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Values <-> controls
    // ------------------------------------------------------------
    void Populate(AppSettings s)
    {
        _populating = true;
        try { Fill(s); }
        finally { _populating = false; }
    }

    void Fill(AppSettings s)
    {
        _mods = s.HotkeyMods;
        _vk = s.HotkeyVk;
        _pauseMods = s.PauseHotkeyMods;
        _pauseVk = s.PauseHotkeyVk;
        _pauseHotkey.Text = _pauseVk == 0 ? "\u2013" : AppSettings.HotkeyText(_pauseMods, _pauseVk);
        _stackMods = s.StackHotkeyMods;
        _stackVk = s.StackHotkeyVk;
        _stackHotkey.Text = _stackVk == 0 ? "\u2013" : AppSettings.HotkeyText(_stackMods, _stackVk);
        _histMods = s.HistoryHotkeyMods;
        _histVk = s.HistoryHotkeyVk;
        _histHotkey.Text = _histVk == 0 ? "\u2013" : AppSettings.HotkeyText(_histMods, _histVk);
        _histWin.Checked = s.HistoryWinVisible;
        _histOnlyGame.Checked = s.HistoryWinOnlyInGame;
        SetNum(_histOpacity, s.HistoryWinOpacity);
        _readStack.Checked = s.ReadStackCount;
        SelectChoice(_uiLang, s.UiLanguage, false);
        SelectChoice(_resolution, s.ResolutionHeight.ToString(), false);
        _showAmmo.Checked = s.ShowAmmoStats;
        _showArmor.Checked = s.ShowArmor;
        _showKey.Checked = s.ShowKeyInfo;
        _showUses.Checked = s.ShowUses;
        _dimJunk.Checked = s.DimJunk;
        _compact.Checked = s.OverlayCompact;
        _fade.Checked = s.OverlayFade;
        _inShots.Checked = s.ShowInScreenshots;
        _hotkey.Text = AppSettings.HotkeyText(_mods, _vk);

        _autostart.Checked = s.StartWithWindows;
        _startMin.Checked = s.StartMinimized;
        _debug.Checked = s.DebugLog;
        _checkUpdates.Checked = s.CheckForUpdates;

        SelectChoice(_apiLang, s.ApiLanguage, false);
        SelectChoice(_ocrLang, s.OcrLanguage, true);
        SelectChoice(_gameMode, s.GameMode, false);
        SetNum(_refresh, s.PriceRefreshMinutes);

        SetNum(_minSim, s.MinSimilarityPercent);
        _autoHover.Checked = s.AutoHover;
        SetNum(_autoDelay, s.AutoHoverDelayMs);
        _autoOnlyGame.Checked = s.AutoHoverOnlyInGame;
        SetNum(_capW, s.CaptureWidth);
        SetNum(_capH, s.CaptureHeight);
        SetNum(_capX, s.CaptureOffsetX);
        SetNum(_capY, s.CaptureOffsetY);

        SelectChoice(_position, s.Position, false);
        SetNum(_seconds, s.OverlaySeconds);
        SetNum(_opacity, s.OverlayOpacityPercent);
        _scale.Value = s.OverlayScalePercent;
        _order = OverlayPainter.OrderOf(s);
        _imageSide = s.ImageSide;
        SetNum(_offX, s.OffsetX);
        SetNum(_offY, s.OffsetY);
        _backArgb = s.BackColorArgb;
        _textArgb = s.TextColorArgb;

        _showLow.Checked = s.ShowFleaLow;
        _showAvg.Checked = s.ShowFleaAvg;
        _showNet.Checked = s.ShowFleaNet;
        _showBestOpt.Checked = s.ShowBestOption;
        _intel.Checked = s.IntelCenter;
        _showBest.Checked = s.ShowBestTrader;
        _showAll.Checked = s.ShowAllTraders;
        _showSlot.Checked = s.ShowPerSlot;
        _showItemImg.Checked = s.ShowItemImage;
        _showTraderImg.Checked = s.ShowTraderImages;

        _useTiers.Checked = s.UseTiers;
        SelectChoice(_tierBasis, s.TierBasis, false);
        _tierName.Checked = s.TierColorName;
        _tierLabel.Checked = s.TierShowLabel;
        _ammoTier.Checked = s.AmmoTierByPen;
        _tierFrame.Checked = s.TierFrame;
        for (int i = 0; i < 4; i++) SetNum(_tierTh[i], s.TierThresholds[i]);
        for (int i = 0; i < 5; i++) _tierColors[i] = s.TierColors[i];

        _preset = s.ThemePreset;
        _thBack = s.ThemeBackArgb;
        _thPanel = s.ThemePanelArgb;
        _thText = s.ThemeTextArgb;
        _thAccent = s.ThemeAccentArgb;

        ApplyTheme();
    }

    AppSettings Collect()
    {
        return new AppSettings
        {
            HotkeyMods = _mods,
            HotkeyVk = _vk,
            PauseHotkeyMods = _pauseMods,
            PauseHotkeyVk = _pauseVk,
            StackHotkeyMods = _stackMods,
            StackHotkeyVk = _stackVk,
            HistoryHotkeyMods = _histMods,
            HistoryHotkeyVk = _histVk,
            HistoryWinVisible = _histWin.Checked,
            HistoryWinOnlyInGame = _histOnlyGame.Checked,
            HistoryWinOpacity = (int)_histOpacity.Value,
            // moved / resized in game, not in this window: keep what is saved
            HistoryWinX = SettingsStore.Current.HistoryWinX,
            HistoryWinY = SettingsStore.Current.HistoryWinY,
            HistoryWinW = SettingsStore.Current.HistoryWinW,
            HistoryWinH = SettingsStore.Current.HistoryWinH,
            ReadStackCount = _readStack.Checked,
            UiLanguage = (_uiLang.SelectedItem as Choice)?.Value ?? "en",
            ResolutionHeight = int.TryParse((_resolution.SelectedItem as Choice)?.Value, out int resH) ? resH : 0,
            ShowAmmoStats = _showAmmo.Checked,
            ShowArmor = _showArmor.Checked,
            ShowKeyInfo = _showKey.Checked,
            ShowUses = _showUses.Checked,
            DimJunk = _dimJunk.Checked,
            OverlayCompact = _compact.Checked,
            OverlayFade = _fade.Checked,
            ShowInScreenshots = _inShots.Checked,
            WelcomeDone = true,

            StartWithWindows = _autostart.Checked,
            StartMinimized = _startMin.Checked,
            DebugLog = _debug.Checked,
            CheckForUpdates = _checkUpdates.Checked,

            ApiLanguage = (_apiLang.SelectedItem as Choice)?.Value ?? "en",
            OcrLanguage = (_ocrLang.SelectedItem as Choice)?.Value ?? "en-US",
            GameMode = (_gameMode.SelectedItem as Choice)?.Value ?? "regular",
            PriceRefreshMinutes = (int)_refresh.Value,

            MinSimilarityPercent = (int)_minSim.Value,
            AutoHover = _autoHover.Checked,
            AutoHoverDelayMs = (int)_autoDelay.Value,
            AutoHoverOnlyInGame = _autoOnlyGame.Checked,
            CaptureWidth = (int)_capW.Value,
            CaptureHeight = (int)_capH.Value,
            CaptureOffsetX = (int)_capX.Value,
            CaptureOffsetY = (int)_capY.Value,

            Position = (_position.SelectedItem as Choice)?.Value ?? "Cursor",
            OverlaySeconds = (int)_seconds.Value,
            OverlayOpacityPercent = (int)_opacity.Value,
            OverlayScalePercent = _scale.Value,
            OverlayOrder = _order.ToArray(),
            ImageSide = _imageSide,
            OffsetX = (int)_offX.Value,
            OffsetY = (int)_offY.Value,
            BackColorArgb = _backArgb,
            TextColorArgb = _textArgb,

            ShowFleaLow = _showLow.Checked,
            ShowFleaAvg = _showAvg.Checked,
            ShowFleaNet = _showNet.Checked,
            ShowBestOption = _showBestOpt.Checked,
            IntelCenter = _intel.Checked,
            ShowBestTrader = _showBest.Checked,
            ShowAllTraders = _showAll.Checked,
            ShowPerSlot = _showSlot.Checked,
            ShowItemImage = _showItemImg.Checked,
            ShowTraderImages = _showTraderImg.Checked,

            UseTiers = _useTiers.Checked,
            TierBasis = (_tierBasis.SelectedItem as Choice)?.Value ?? "PerSlot",
            TierColorName = _tierName.Checked,
            TierShowLabel = _tierLabel.Checked,
            AmmoTierByPen = _ammoTier.Checked,
            TierFrame = _tierFrame.Checked,
            TierThresholds = _tierTh.Select(n => (long)n.Value).ToArray(),
            TierColors = (int[])_tierColors.Clone(),

            ThemePreset = _preset,
            ThemeBackArgb = _thBack,
            ThemePanelArgb = _thPanel,
            ThemeTextArgb = _thText,
            ThemeAccentArgb = _thAccent
        };
    }

    // ------------------------------------------------------------
    //  Auto-save: every change is applied and saved after a short pause
    // ------------------------------------------------------------
    void ScheduleSave()
    {
        if (_populating) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveNow()
    {
        string? problem = Problem();
        if (problem != null)
        {
            ShowStatus("\u26A0  " + problem, warning: true, stay: true);
            return;
        }
        Result = Collect();
        _apply(Result);
        if (Result.UiLanguage != _uiLangAtStart)
        {
            ReopenRequested = true;   // opens again in the new language
            BeginInvoke(new Action(Close));
            return;
        }
        ShowStatus("\u2713  " + L.T("Saved"), warning: false, stay: false);
    }

    void ShowStatus(string text, bool warning, bool stay)
    {
        _savedLbl.Text = text;
        _savedLbl.ForeColor = warning ? Color.FromArgb(224, 165, 38) : CAccent;
        _savedLbl.Visible = true;
        _savedTimer.Stop();
        if (!stay) _savedTimer.Start();
    }

    // why the current input cannot be saved (null = fine)
    string? Problem()
    {
        for (int i = 1; i < _tierTh.Length; i++)
            if (_tierTh[i].Value <= _tierTh[i - 1].Value)
                return L.T("Loot Tiers: each tier must start higher than the one before");
        return null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_saveTimer.Enabled)
        {
            _saveTimer.Stop();
            SaveNow();   // a change from the last moment
        }
        base.OnFormClosing(e);
    }

    // Esc closes the window
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _search.Focused && _search.Text.Length > 0)
        {
            _search.Clear();   // Esc in the search field: clear it first
            return true;
        }
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
