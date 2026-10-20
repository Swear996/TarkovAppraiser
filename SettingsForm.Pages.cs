using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

// Sidebar (logo, pages, price status) and the content of the pages (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    // ------------------------------------------------------------
    //  Sidebar: logo, pages, price status
    // ------------------------------------------------------------
    void BuildSidebar()
    {
        _side.Controls.Add(_logo);
        // the name must stay on one line next to the logo: the font gets smaller until it fits
        // (with Windows scaling the text can come out a little wider than at 100 %)
        float nameSize = 11.5f;
        while (nameSize > 9f && TextRenderer.MeasureText("Tarkov Appraiser", UiFont.Semi(nameSize)).Width > SideW - 72) nameSize -= 0.5f;
        _side.Controls.Add(new Txt
        {
            Text = "Tarkov Appraiser",
            Font = UiFont.Semi(nameSize),
            Left = 64, Top = 29, Width = SideW - 66, Height = 24,
            UseMnemonic = false, Tag = "title", AutoEllipsis = false
        });

        // price status card at the bottom
        var card = new Card { Left = 12, Top = 512, Width = SideW - 24, Height = 116 };
        card.Controls.Add(new Txt
        {
            Text = L.T("PRICES"),
            Font = UiFont.Semi(8.5f),
            Left = 14, Top = 12, Width = 150, Height = 18,
            UseMnemonic = false, Tag = "section"
        });
        _priceDot.Font = UiFont.Text(8.5f);
        _priceDot.SetBounds(14, 34, 14, 18);
        _priceStatus.Font = UiFont.Text(8.5f);
        _priceStatus.SetBounds(30, 34, card.Width - 42, 36);
        card.Controls.Add(_priceDot);
        card.Controls.Add(_priceStatus);
        _btnUpdate = MakeButton("Update now", card.Width - 28);
        _btnUpdate.Height = 28;
        _btnUpdate.Left = 14;
        _btnUpdate.Top = 76;
        _btnUpdate.Click += async (_, _) => await UpdatePricesNowAsync();
        card.Controls.Add(_btnUpdate);
        _side.Controls.Add(card);

        // players online: a small card in the style of the price card, just above it
        _onlineCard.SetBounds(12, card.Top - 44, SideW - 24, 36);
        _onlineDot.Font = UiFont.Text(8.5f);
        _onlineDot.ForeColor = Color.FromArgb(76, 195, 138);
        _onlineDot.SetBounds(14, 9, 14, 18);
        _online.Font = UiFont.Text(8.5f);
        _online.SetBounds(30, 9, _onlineCard.Width - 42, 18);
        _onlineCard.Controls.Add(_onlineDot);
        _onlineCard.Controls.Add(_online);
        _onlineCard.Visible = false;   // until the counter answered
        _side.Controls.Add(_onlineCard);
        UpdateOnline();
        Online.Changed += OnOnlineChanged;

        var version = _versionLbl;
        version.Font = UiFont.Text(8.5f);
        version.SetBounds(24, WindowH - 40, SideW - 30, 20);
        version.Click += (_, _) => { if (Updates.Available != null) Updates.InstallRequested?.Invoke(); };
        _side.Controls.Add(version);
        ShowUpdateState();
        Updates.Found += OnUpdateFoundHere;

        // keep the price card and the version at the bottom (also when maximized)
        _side.Resize += (_, _) =>
        {
            if (_side.Height < 400) return;
            card.Top = _side.Height - 178;
            _onlineCard.Top = card.Top - 44;
            version.Top = _side.Height - 40;
        };
    }

    readonly Card _onlineCard = new();
    readonly Txt _onlineDot = new() { Text = "●", UseMnemonic = false, Tag = "dot" };
    readonly Txt _online = new() { UseMnemonic = false, Tag = "label" };

    void OnOnlineChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnOnlineChanged)); return; }
        UpdateOnline();
    }

    void UpdateOnline()
    {
        if (Online.Count is not int n) { _onlineCard.Visible = false; return; }
        _online.Text = string.Format(L.T(n == 1 ? "Online: {0} User" : "Online: {0} Users"), n.ToString("N0"));
        _onlineCard.Visible = true;
    }

    // what the recognition currently uses (shown on the Detection page, refreshed every 2 s)
    void UpdateResolutionInfo()
    {
        var s = new AppSettings { ResolutionHeight = int.TryParse((_resolution.SelectedItem as Choice)?.Value, out int h) ? h : 0 };
        var r = Resolution.Detect(Cursor.Position, s);
        string game = r.Game is Size g ? $"{g.Width} × {g.Height}" : L.T("not running");
        string used = r.Source switch
        {
            "manual" => L.T("set by you"),
            "game" => L.T("from the game window"),
            _ => L.T("from the screen")
        };
        _resInfo.Text = $"{L.T("Screen")}: {r.Screen.Width} × {r.Screen.Height}   ·   {L.T("Tarkov")}: {game}\r\n" +
                        $"{L.T("Used")}: {r.UsedHeight} px {used}   ·   {L.T("Tooltip size")} ×{r.Factor:0.00}";
    }

    void UpdatePriceStatus()
    {
        _priceStatus.Text = PriceState.Compact();
        string tip = PriceState.Text();
        if (tip != _priceTip)
        {
            _priceTip = tip;
            _tips.SetToolTip(_priceStatus, tip);
        }
        _priceDot.ForeColor = PriceState.Loading ? CAccent
            : PriceState.Health() switch
            {
                2 => Color.FromArgb(76, 195, 138),
                1 => Color.FromArgb(224, 165, 38),
                _ => Color.FromArgb(229, 72, 77)
            };
    }

    // ------------------------------------------------------------
    //  Pages
    // ------------------------------------------------------------
    PagePanel AddPage(string title, string subtitle, string icon)
    {
        var page = new PagePanel { Tag = "page" };
        page.Resize += (_, _) =>
        {
            int left = FitCards(page);
            if (page.Visible) AlignHeader(left);
        };
        _main.Controls.Add(page);
        _pages.Add(page);
        _pageInfo.Add((L.T(title), L.T(subtitle)));

        int index = _navs.Count;
        var nav = new NavButton(L.T(title))
        {
            Left = 0, Top = 124 + index * 42, Width = SideW, IconName = icon
        };
        nav.Click += (_, _) => SelectPage(index);
        _navs.Add(nav);
        _side.Controls.Add(nav);
        return page;
    }

    void SelectPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navs[i].Selected = i == index;
        }
        _livePanel.Visible = index == _overlayPage;   // the live preview belongs to the Overlay page
        if (index == HistoryPage) { _history.Reload(); FitHistoryCard(); }   // current prices
        _pageTitle.Text = _pageInfo[index].Title;
        _pageSub.Text = _pageInfo[index].Sub;
        AlignHeader(FitCards(_pages[index]));
        RefreshNav();
    }

    // Cards use the full width of the page up to a comfortable maximum. In a wider window (maximized)
    // the column of cards is centered, so labels and their controls stay close together.
    // Returns the left edge of the cards (the page title is aligned with it).
    const int MaxCardW = 1000;
    static int FitCards(PagePanel p)
    {
        int avail = p.ClientSize.Width - CardX - 24;
        int w = Math.Clamp(avail, 300, MaxCardW);
        int left = CardX + Math.Max(0, (avail - w) / 2);
        foreach (Control c in p.Body.Controls)
        {
            if (c is Card)
            {
                if (c.Width != w) c.Width = w;
                if (c.Left != left) c.Left = left;
            }
            else if (c is FlowLayoutPanel flow)
            {
                flow.Width = w;
                flow.Left = left;
                foreach (Control child in flow.Controls)
                    if (child.Width != w) child.Width = w;
            }
            else if (c.Tag as string == "section") c.Left = left + 4;   // small headings between cards ("CHANGELOG")
        }
        return left;
    }

    void AlignHeader(int left)
    {
        _pageTitle.Left = left;
        _pageSub.Left = left;
    }

    void RefreshNav()
    {
        foreach (var n in _navs)
        {
            n.AccentColor = CAccent;
            n.BackColor = n.Selected ? Themes.Blend(CBack, CText, 0.08) : CBack;
            n.ForeColor = n.Selected ? CText : CMuted;
            n.FlatAppearance.MouseOverBackColor = Themes.Blend(CBack, CText, 0.05);
            n.FlatAppearance.MouseDownBackColor = Themes.Blend(CBack, CText, 0.10);
            n.Invalidate();
        }
    }

    void BuildGeneral(PagePanel p)
    {
        foreach (var (v, t) in new[] { ("regular", "PvP (regular)"), ("pve", "PvE") })
            _gameMode.Items.Add(new Choice(v, t));

        _uiLang.Items.Add(new Choice("en", "English"));
        _uiLang.Items.Add(new Choice("de", "Deutsch"));

        var c = NewCard(p, "Hotkey");
        Row(c, "Show prices", _hotkey, 150, "Click, then press a key (F-keys work best)");
        Row(c, "Pause automatic mode", _pauseHotkey, 150, "Del = no hotkey");
        Row(c, "Type the stack count", _stackHotkey, 150, "For ammo stacks - Del = no hotkey");
        Row(c, "Show / hide the history list", _histHotkey, 150, "In-game list of the last items - Del = no hotkey");
        Done(p, c);

        c = NewCard(p, "Program");
        Row(c, "Language", _uiLang, 170);
        Done(p, c);

        c = NewCard(p, "Startup");
        Switch(c, _autostart);
        Switch(c, _startMin, "Otherwise this window opens at startup");
        if (Updates.Configured) Switch(c, _checkUpdates);
        else
        {
            _checkUpdates.Enabled = false;
            Switch(c, _checkUpdates, "No download source is set up in this build yet");
        }
        Done(p, c);

        c = NewCard(p, "Prices");
        Row(c, "Game mode", _gameMode, 170);
        Row(c, "Refresh every … minutes", _refresh, 84);
        Note(c, "Prices come from tarkov.dev. The current status is shown at the bottom left.", 18);
        Done(p, c);

        _hotkey.KeyDown += OnHotkeyKeyDown;
        _hotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _pauseHotkey.KeyDown += OnHotkeyKeyDown;
        _pauseHotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _stackHotkey.KeyDown += OnHotkeyKeyDown;
        _stackHotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        _histHotkey.KeyDown += OnHotkeyKeyDown;
        _histHotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true;
    }

    void BuildDetection(PagePanel p)
    {
        foreach (var (v, t) in new[]
        {
            ("en", "English"), ("de", "German"), ("ru", "Russian"), ("fr", "French"),
            ("es", "Spanish"), ("it", "Italian"), ("pl", "Polish"), ("pt", "Portuguese"),
            ("cs", "Czech"), ("hu", "Hungarian"), ("ro", "Romanian"), ("sk", "Slovak"),
            ("tr", "Turkish"), ("ja", "Japanese"), ("ko", "Korean"), ("zh", "Chinese")
        })
            _apiLang.Items.Add(new Choice(v, $"{t} ({v})"));

        try
        {
            foreach (var l in OcrEngine.AvailableRecognizerLanguages)
                _ocrLang.Items.Add(new Choice(l.LanguageTag, $"{l.DisplayName} ({l.LanguageTag})"));
        }
        catch (Exception ex)
        {
            Log.Write("Could not read OCR languages: " + ex.Message, true);
        }

        var c = NewCard(p, "Language");
        Row(c, "Game language", _apiLang, 200);
        Row(c, "Text recognition (OCR)", _ocrLang, 200);
        Note(c, "Both must match the game. Missing OCR languages are added in Windows Settings > Time & Language > Language.", 32);
        Done(p, c);

        c = NewCard(p, "Automatic mode");
        Switch(c, _autoHover, "The hotkey keeps working as well");
        Row(c, "Wait before reading (ms)", _autoDelay, 84, "About 300 ms is enough");
        Switch(c, _autoOnlyGame);
        Done(p, c);

        c = NewCard(p, "Stacks");
        Switch(c, _readStack, "Experimental - when unsure, the full stack is shown. You can always type the number with the stack hotkey.");
        Done(p, c);

        c = NewCard(p, "Matching");
        Row(c, "Minimum similarity (%)", _minSim, 84, "Higher = fewer wrong matches");
        Done(p, c);

        _resolution.Items.Add(new Choice("0", "Automatic"));
        foreach (var (h, name) in new[] { (720, "1280 × 720"), (768, "1366 × 768"), (900, "1600 × 900"), (1080, "1920 × 1080 (Full HD)"),
                                          (1200, "1920 × 1200"), (1440, "2560 × 1440"), (1600, "2560 × 1600"), (2160, "3840 × 2160 (4K)") })
            _resolution.Items.Add(new Choice(h.ToString(), name));

        c = NewCard(p, "Resolution");
        Row(c, "Game resolution", _resolution, 200, "Automatic finds the Tarkov window");
        _resInfo.Font = UiFont.Text(8.5f);
        _resInfo.SetBounds(CardPad, c.NextY + 4, CardW - 2 * CardPad, 36);
        _resInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        c.Controls.Add(_resInfo);
        c.NextY += 46;
        Done(p, c);
        _resolution.SelectedIndexChanged += (_, _) => UpdateResolutionInfo();
        _resTimer.Tick += (_, _) => { if (_pages.Count > 1 && _pages[1].Visible) UpdateResolutionInfo(); };
        _resTimer.Start();

        c = NewCard(p, "Capture area");
        Note(c, "Area around the cursor that is read for the item name. Set for 768 px height - other resolutions are scaled automatically.", 32);
        Row2(c, "Width (px)", _capW, "Height (px)", _capH);
        Row2(c, "Offset X", _capX, "Offset Y", _capY);
        Done(p, c);
    }

    async Task UpdatePricesNowAsync()
    {
        if (_updating || _btnUpdate == null) return;
        _updating = true;
        _btnUpdate.Text = L.T("Updating ...");
        _priceStatus.Text = L.T("Updating prices ...");
        try
        {
            await _refreshPrices();
            // the price loader may already be busy (e.g. startup): wait until it is done
            while (PriceState.Loading) await Task.Delay(200);
        }
        catch (Exception ex)
        {
            Log.Write("Manual price update: " + ex.Message, true);
        }
        finally
        {
            _updating = false;
            if (!IsDisposed)
            {
                _btnUpdate.Text = L.T("Update now");
                UpdatePriceStatus();
            }
        }
    }

    void BuildOverlay(PagePanel p)
    {
        _position.Items.Add(new Choice("Cursor", "Next to the mouse"));
        _position.Items.Add(new Choice("TopLeft", "Top left"));
        _position.Items.Add(new Choice("TopRight", "Top right"));
        _position.Items.Add(new Choice("BottomLeft", "Bottom left"));
        _position.Items.Add(new Choice("BottomRight", "Bottom right"));

        var c = NewCard(p, "Placement");
        Row(c, "Position", _position, 170);
        Row(c, "Display time (seconds)", _seconds, 84);
        Row2(c, "Mouse offset X", _offX, "Mouse offset Y", _offY);
        Done(p, c);

        c = NewCard(p, "Look");
        Row(c, "Size", _scale, 250);
        Row(c, "Opacity (%)", _opacity, 84);
        Row2(c, "Background", _btnBack, "Text", _btnText, 96);
        Switch(c, _fade, "The price box appears softly instead of popping up");
        Switch(c, _inShots, "Price box and in-game list appear in screenshots, OBS and Discord. Off = always invisible there");
        Done(p, c);

        c = NewCard(p, "What to show");
        Switch(c, _compact, "One line with the best way to sell - for fast looting. The switches below are kept for later.");
        Switches(c, _showLow, _showAvg, _showNet, _showBestOpt, _showBest,
                    _showAll, _showSlot, _showItemImg, _showTraderImg, _showAmmo,
                    _showArmor, _showKey, _showUses);
        Done(p, c);

        c = NewCard(p, "Flea market fee");
        Switch(c, _intel);
        Note(c, "Fee for one item at the lowest flea price (formula from the EFT wiki).", 18);
        Done(p, c);
    }

    void BuildTiers(PagePanel p)
    {
        _tierBasis.Items.Add(new Choice("PerSlot", "Price per slot"));
        _tierBasis.Items.Add(new Choice("Total", "Total item price"));

        var c = NewCard(p, "Loot tiers");
        Switch(c, _useTiers);
        Switch(c, _tierName);
        Switch(c, _tierLabel);
        Switch(c, _tierFrame, "Valuable loot stands out at a glance - junk keeps the normal frame");
        Switch(c, _dimJunk, "Not worth the space: shown dimmed and marked \"Junk\"");
        Row(c, "Tiers are based on", _tierBasis, 170, "Per slot: a 2-slot item counts half");
        Switch(c, _ammoTier, "By penetration: Junk below 20, Common 20+, Good 30+, Rare 40+, Legendary 50+. The prices keep the color of what the ammo sells for.");
        Done(p, c);

        c = NewCard(p, "Thresholds");
        c.Controls.Add(Lbl("Tier", CardPad, c.NextY, 140, "muted", 8.5f));
        c.Controls.Add(Lbl("Starts at (₽)", 170, c.NextY, 140, "muted", 8.5f));
        c.Controls.Add(Lbl("Color", 330, c.NextY, 120, "muted", 8.5f));
        c.NextY += 22;

        for (int k = 0; k < 5; k++)
        {
            int tier = k;
            StartRow(c);
            _tierLbl[k] = new Txt
            {
                Text = L.T(AppSettings.TierNames[k]),
                Font = UiFont.Semi(10f),
                Left = CardPad, Top = c.NextY + 11, Width = 140, Height = 22,
                UseMnemonic = false, Tag = "tier"
            };
            c.Controls.Add(_tierLbl[k]);

            if (k == 0)
                c.Controls.Add(Lbl("below the next tier", 170, c.NextY + 13, 140, "muted"));
            else
            {
                var num = new ModernNum
                {
                    Minimum = 0,
                    Maximum = 1_000_000_000,
                    Increment = 1000,
                    ThousandsSeparator = true
                };
                _tierTh[k - 1] = num;
                c.Controls.Add(Hosted(num, 170, c.NextY + 6, 140));
            }

            var btn = Swatch();
            btn.SetBounds(330, c.NextY + 8, CardW - CardPad - 330, 28);
            btn.Click += (_, _) => Pick(btn, () => _tierColors[tier], v => _tierColors[tier] = v, AppSettings.DefaultTierColors()[tier]);
            _tierBtn[k] = btn;
            c.Controls.Add(btn);
            c.NextY += RowH;
        }

        var btnDef = MakeButton("Reset tiers", 130);
        btnDef.Click += (_, _) =>
        {
            var d = AppSettings.DefaultThresholds();
            var col = AppSettings.DefaultTierColors();
            for (int i = 0; i < 4; i++) SetNum(_tierTh[i], d[i]);
            for (int i = 0; i < 5; i++) _tierColors[i] = col[i];
            UpdateSwatches();
        };
        Buttons(c, btnDef);
        Done(p, c);
    }

    void BuildAppearance(PagePanel p)
    {
        var c = NewCard(p, "Window theme");
        const int dot = 32, step = 38;
        int perRow = (CardW - 2 * CardPad + step - dot) / step;
        for (int i = 0; i < Themes.Presets.Length; i++)
        {
            var pr = Themes.Presets[i];
            var d = new ColorDot
            {
                Tag = pr,
                Left = CardPad - 3 + (i % perRow) * step,
                Top = c.NextY + (i / perRow) * step,
                Size = new Size(dot, dot)
            };
            d.Click += (_, _) =>
            {
                _thBack = pr.Back;
                _thPanel = pr.Panel;
                _thText = pr.Text;
                _thAccent = pr.Accent;
                _preset = pr.Name;
                ApplyTheme();
            };
            _tips.SetToolTip(d, pr.Name);
            _presetDots.Add(d);
            c.Controls.Add(d);
        }
        c.NextY += (Themes.Presets.Length + perRow - 1) / perRow * step + 2;
        _presetName.Font = UiFont.Text(8.5f);
        _presetName.SetBounds(CardPad, c.NextY, CardW - 2 * CardPad, 18);
        c.Controls.Add(Stretch(_presetName));
        c.NextY += 28;
        Row2(c, "Background", _thBackBtn, "Panels", _thPanelBtn, 96);
        Row2(c, "Text", _thTextBtn, "Accent", _thAccentBtn, 96);
        Done(p, c);

        var std = new AppSettings();   // standard colors for the "Default" button of the color picker
        _btnBack.Click += (_, _) => Pick(_btnBack, () => _backArgb, v => _backArgb = v, std.BackColorArgb);
        _btnText.Click += (_, _) => Pick(_btnText, () => _textArgb, v => _textArgb = v, std.TextColorArgb);
        void Custom() { _preset = "Custom"; ApplyTheme(); }
        _thBackBtn.Click += (_, _) => Pick(_thBackBtn, () => _thBack, v => _thBack = v, std.ThemeBackArgb, Custom);
        _thPanelBtn.Click += (_, _) => Pick(_thPanelBtn, () => _thPanel, v => _thPanel = v, std.ThemePanelArgb, Custom);
        _thTextBtn.Click += (_, _) => Pick(_thTextBtn, () => _thText, v => _thText = v, std.ThemeTextArgb, Custom);
        _thAccentBtn.Click += (_, _) => Pick(_thAccentBtn, () => _thAccent, v => _thAccent = v, std.ThemeAccentArgb, Custom);
    }

    // ---- FAQ: questions that open with a click (same look as the changelog) ----
    static readonly (string Q, string[] A)[] Faq =
    {
        ("The price box does not appear.", new[] {
            "Tarkov has to run in Borderless or Windowed mode, and the item's tooltip has to be visible.",
            "Automatic mode: check that it is switched on and not paused (Ctrl+F8)." }),
        ("It shows the wrong item.", new[] {
            "Detection › Language has to match the game language.",
            "On unusual screens, set the resolution by hand under Detection › Resolution." }),
        ("The stack count is wrong or missing.", new[] {
            "Hover the stack and press Shift+F8, type the number and press Enter. Tarkov Appraiser remembers how this number looks and reads it by itself from then on.",
            "The number is read from the cell; its tiny font can look slightly different in each cell of the stash." }),
        ("Armor shows \"(new)\" instead of the real durability.", new[] {
            "The real value (e.g. 179/190) is read from the corner of the item. If it can't be read, the durability of a new item is shown and marked \"new\"." }),
        ("What do the tiers mean?", new[] {
            "The price colors show what the item is worth, from Junk to Legendary - per slot or in total, see Loot Tiers.",
            "Ammo: the tier name shows how good the round is (penetration), the price keeps the color of what it sells for. You can switch this off under Loot Tiers." }),
        ("Can I get banned for this?", new[] {
            "Tarkov Appraiser only looks at the screen like a screenshot tool. It never reads the game's memory and never changes game files.",
            "It is a fan project and not made by Battlestate Games - use it at your own risk." }),
        ("Where are my settings and the log?", new[] {
            "About › Open log & settings folder. Export and import your settings there too." }),
        ("What does Tarkov Appraiser send over the internet?", new[] {
            "Prices and item data come from tarkov.dev, the update check from GitHub. For the \"players online\" number it sends a random ID (created once on your PC) every 10 minutes - no name, no game data, no IP address is stored. Nothing else." }),
        ("How do I report a problem?", new[] {
            "Switch on the debug log under About, reproduce the problem, then use About › Report a problem. It copies the details and opens the report page." }),
    };

    void BuildFaq(PagePanel p)
    {
        var flow = new FlowLayoutPanel
        {
            Left = CardX,
            Top = p.Y,
            Width = CardW,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 20)
        };
        foreach (var (q, a) in Faq)
            flow.Controls.Add(new Collapsible(new ChangelogEntry("", "", a.Select(L.T).ToArray()), false) { Width = CardW, Title = L.T(q) });
        p.Body.Controls.Add(flow);
    }

    // ---- update notice: version line in the sidebar + Updates card on the About page ----
    readonly Txt _versionLbl = new() { UseMnemonic = false, Tag = "muted" };
    readonly Txt _updateInfo = new() { UseMnemonic = false };
    readonly Button _btnCheckUpdate = MakeButton("Check for updates", 170);

    void OnUpdateFoundHere()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnUpdateFoundHere)); return; }
        ShowUpdateState();
    }

    void ShowUpdateState()
    {
        var r = Updates.Available;
        _versionLbl.Text = "Version " + Changelog.Current + (r != null ? "  \u00B7  " + string.Format(L.T("Update to {0}"), r.Version) + " \u203A" : "");
        _versionLbl.Tag = r != null ? "section" : "muted";   // accent color when an update is waiting
        _versionLbl.Cursor = r != null ? Cursors.Hand : Cursors.Default;
        _updateInfo.Text = r != null ? string.Format(L.T("Version {0} is available."), r.Version)
                         : Updates.Configured ? string.Format(L.T("Version {0}"), Changelog.Current) : L.T("Update check not set up in this build");
        _btnCheckUpdate.Text = L.T(r != null ? "Update now" : "Check for updates");
        _versionLbl.ForeColor = r != null ? CAccent : CMuted;
    }

    async Task CheckUpdateNowAsync()
    {
        if (Updates.Available != null) { Updates.InstallRequested?.Invoke(); return; }
        _btnCheckUpdate.Enabled = false;
        _updateInfo.Text = L.T("Checking …");
        var r = await Updates.CheckAsync();
        _btnCheckUpdate.Enabled = true;
        if (r != null) Updates.Announce(r);   // -> tray message + this window
        else
        {
            ShowUpdateState();
            _updateInfo.Text = string.Format(L.T("You have the newest version ({0})."), Changelog.Current);
        }
    }

    void BuildAbout(PagePanel p)
    {
        var c = NewCard(p, null);
        c.Controls.Add(_aboutLogo);
        c.Controls.Add(Lbl("Tarkov Appraiser", 82, 18, 300, "title", 15f, semi: true));
        c.Controls.Add(Stretch(Lbl("Version " + Changelog.Current + "  ·  " + L.T("Prices for your loot at a glance"), 82, 48, CardW - 98, "muted")));
        c.NextY = 78;
        Done(p, c);

        c = NewCard(p, "Updates");
        StartRow(c);
        _updateInfo.Font = UiFont.Text(9.5f);
        _updateInfo.SetBounds(CardPad, c.NextY + 14, CardW - 2 * CardPad - 180, 20);
        c.Controls.Add(Stretch(_updateInfo));
        _btnCheckUpdate.SetBounds(CardW - CardPad - 170, c.NextY + 7, 170, 32);
        _btnCheckUpdate.Click += async (_, _) => await CheckUpdateNowAsync();
        c.Controls.Add(AtRight(_btnCheckUpdate));
        c.NextY += RowH;
        Done(p, c);

        c = NewCard(p, "Troubleshooting");
        Switch(c, _debug);
        var btnFolder = MakeButton("Open log & settings folder", 220);
        btnFolder.Click += (_, _) => OpenDataFolder();
        var btnReport = MakeButton("Report a problem", 170);
        btnReport.Click += (_, _) =>
        {
            if (Ask("Report a problem",
                    "Tarkov Appraiser copies the details (version, Windows, end of the log) to the clipboard and opens the report page. Nothing is sent automatically - paste it there with Ctrl+V.",
                    0, "Continue", "Cancel") == 0)
                BugReport.Send(null);
        };
        Buttons(c, btnFolder, btnReport);
        Done(p, c);

        c = NewCard(p, "Settings file");
        Note(c, "Back up your settings or move them to another PC. Imported settings apply right away.", 32);
        var btnExport = MakeButton("Export …", 120);
        btnExport.Click += (_, _) => ExportSettings();
        var btnImport = MakeButton("Import …", 120);
        btnImport.Click += (_, _) => ImportSettings();
        Buttons(c, btnExport, btnImport);
        Done(p, c);

        p.Body.Controls.Add(Lbl("CHANGELOG", CardX + 4, p.Y + 4, 200, "section", 8.5f, semi: true));
        p.Y += 28;
        var flow = new FlowLayoutPanel
        {
            Left = CardX,
            Top = p.Y,
            Width = CardW,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 20)
        };
        bool first = true;
        foreach (var entry in Changelog.Entries)
        {
            flow.Controls.Add(new Collapsible(entry, first) { Width = CardW });   // newest entry starts expanded
            first = false;
        }
        p.Body.Controls.Add(flow);
    }
}
