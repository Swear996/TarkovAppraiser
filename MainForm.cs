using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

class MainForm : Form
{
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID = 1;
    const int PAUSE_HOTKEY_ID = 2;   // pauses / resumes the automatic mode
    const int STACK_HOTKEY_ID = 3;   // type the stack count of the last item
    const int HISTORY_HOTKEY_ID = 4; // show / hide the in-game history list

    // last recognized item (for the stack hotkey)
    Item? _lastItem;
    Image? _lastImage;
    Point _lastCursor;
    DateTime _lastShown;
    const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    // ---- Auto hover ----
    readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 80 };
    Point _hoverPos;
    DateTime _hoverSince = DateTime.UtcNow;
    bool _hoverHandled;
    int _autoTries;           // screenshots without a visible tooltip at the current resting position
    // An overlay for a recognized item disappears as soon as the mouse leaves that item
    // (hotkey AND automatic mode), so it can never be mistaken for the price of the next item.
    bool _anchored;
    Point _anchorAt;
    int _anchorDist;
    uint _fgPid;
    bool _fgIsGame;

    // Sample items for the preview button; each click shows the next one
    // also used by the live preview of the settings window
    public static readonly Item[] Samples =
    {
        new("Duct tape", "Duct tape", "", "", 1, 5200, 4800,
            new List<TraderPrice> { new("Skier", 2900) }, "5734795124597738002c6176", 4800),
        new("Salewa FIRST AID KIT", "Salewa", "", "", 2, 41000, 38500,
            new List<TraderPrice> { new("Therapist", 22400), new("Skier", 21000) }, "544fb45d4bdc2dee738b4568", 35500),
        new("Ophthalmoscope", "Ophthalmoscope", "", "", 1, 45000, 42000,
            new List<TraderPrice> { new("Therapist", 25500) }, "5af0534a86f7743b6f354284", 40500),
        new("Tetriz portable game console", "Tetriz", "", "", 1, 76000, 71000,
            new List<TraderPrice> { new("Peacekeeper", 41000) }, "5c12620d86f7743f8b198b72", 65000),
        new("Graphics card", "GPU", "", "", 2, 312705, 301111,
            new List<TraderPrice> { new("Therapist", 100980) }, "57347ca924597744596b4e71", 160000),
        new("5.45x39mm BS gs", "BS", "", "", 1, 1150, 1090,
            new List<TraderPrice> { new("Prapor", 310) }, "", 450, null, false, 60),
    };
    int _sampleIndex;

    // price list for the samples (set whenever prices are loaded)
    static List<Item> s_prices = new();
    static Dictionary<string, Item> s_byId = new();

    static void SetPrices(List<Item> items)
    {
        s_prices = items;
        var map = new Dictionary<string, Item>();
        foreach (var it in items) if (it.Id.Length > 0) map[it.Id] = it;
        s_byId = map;
    }

    // item of the price list by id (barter rewards), null when unknown
    public static Item? ById(string id) => s_byId.TryGetValue(id, out var it) ? it : null;

    // Sample number i: the real item from the price list (current prices, right picture, game language),
    // found by its English name or its id; the built-in sample when the list is not loaded yet
    // Example item for the live preview: shows every part of the price box at once
    // (ammo values, armor, key, barter tip ... with sample values), so everything can be arranged.
    public static readonly Item Demo = new("Example item", "Example", "", "", 2, 41000, 38500,
        new List<TraderPrice> { new("Therapist", 22400), new("Skier", 21000) },
        "", 35500, null, false, 5);

    // Picture of a sample (the example item has none: it gets the question-mark placeholder)
    public static (string Id, string? Url) PictureOf(Item i) =>
        ReferenceEquals(i, Demo) ? ("", null) : (i.Id, i.ImageUrl);

    static Bitmap? s_placeholder;
    static int s_placeholderAccent;

    // question mark in the current accent color for the example item
    public static Image? PlaceholderFor(Item i, AppSettings s)
    {
        if (!ReferenceEquals(i, Demo)) return null;
        if (s_placeholder == null || s_placeholderAccent != s.ThemeAccentArgb)
        {
            s_placeholder?.Dispose();
            s_placeholder = Themes.Placeholder(256, Themes.FromArgb(s.ThemeAccentArgb));
            s_placeholderAccent = s.ThemeAccentArgb;
        }
        return s_placeholder;
    }

    // Sample 0 = the example item with everything, then the real samples
    public static Item Sample(int i)
    {
        int n = Samples.Length + 1;
        int k = ((i % n) + n) % n;
        if (k == 0) return Demo;
        var sample = Samples[k - 1];
        var list = s_prices;
        return list.FirstOrDefault(x => string.Equals(x.Name, sample.Name, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(x => x.Id.Length > 0 && x.Id == sample.Id)
            ?? sample;
    }

    readonly OverlayForm _overlay = new();
    readonly ToastForm _toast = new();

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    readonly NotifyIcon _tray = new();
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripItem _miSettings, _miReload, _miStatus, _miExit, _miHeader;
    readonly Themes.SwitchMenuItem _miAuto, _miPause, _miHistWin;
    HistoryWindow? _histWin;
    readonly ToolStripMenuItem _miRecent;
    bool _autoPaused;   // automatic mode paused (hotkey / tray menu), until resumed or restart

    // ---- Update check ----
    static string UpdateTitle => "Tarkov Appraiser - " + L.T("update available");
    string? _updateUrl;
    List<Item> _items = new();
    DateTime _lastLoad = DateTime.MinValue;
    bool _busy;
    bool _loading;
    bool _settingsOpen;
    Bitmap? _cleanShot;            // automatic mode: last screenshot without a tooltip (for the stack number)
    Rectangle _cleanRect;
    Point _cleanCursor;
    DateTime _cleanTime;
    SettingsForm? _settingsForm;   // the open settings window (tray switches change it directly)
    bool _recording;               // a hotkey field in the settings has the focus: hotkeys are released

    public MainForm()
    {
        ShowInTaskbar = false;

        _menu.ImageScalingSize = new Size(20, 20);
        History.Load();
        History.ShowRequested += (item, count) => ShowRecent(item, count);   // a line of a history list was clicked

        // header: logo + name, then the price status
        _miHeader = new ToolStripMenuItem("Tarkov Appraiser") { Tag = "header", Enabled = false };
        _miStatus = new ToolStripMenuItem(PriceState.Short()) { Enabled = false, Tag = "status" };
        _menu.Items.Add(_miHeader);
        _menu.Items.Add(_miStatus);
        _menu.Items.Add(new ToolStripSeparator());

        // automatic mode: switch it on/off, pause it
        _miAuto = new Themes.SwitchMenuItem(L.T("Automatic mode"), (_, _) => ToggleAutoMode());
        _miPause = new Themes.SwitchMenuItem(L.T("Pause automatic mode"), (_, _) => TogglePause());
        _menu.Items.Add(_miAuto);
        _menu.Items.Add(_miPause);

        // the last checked items: click = show the price again
        _miRecent = new ToolStripMenuItem(L.T("Recent items"));
        _miRecent.DropDownItems.Add(new ToolStripMenuItem("-"));   // filled when the menu opens
        _menu.Items.Add(_miRecent);
        _miHistWin = new Themes.SwitchMenuItem(L.T("History list in game"), (_, _) => ToggleHistoryWindow());
        _menu.Items.Add(_miHistWin);
        _menu.Items.Add(new ToolStripSeparator());

        _miSettings = _menu.Items.Add(L.T("Settings …"), null, (_, _) => OpenSettings());
        _miReload = _menu.Items.Add(L.T("Reload prices"), null, async (_, _) => await LoadPricesAsync(true));
        _menu.Opening += (_, _) =>
        {
            var cur = SettingsStore.Current;
            _miStatus.Text = PriceState.Short();
            SetStatusDot();
            _miAuto.On = cur.AutoHover;
            string pauseKey = cur.PauseHotkeyVk != 0 ? "  (" + AppSettings.HotkeyText(cur.PauseHotkeyMods, cur.PauseHotkeyVk) + ")" : "";
            _miPause.Text = L.T("Pause automatic mode") + pauseKey;
            _miPause.On = _autoPaused;
            _miPause.Visible = cur.AutoHover;   // only useful with the automatic mode
            FillRecentMenu();
            string histKey = cur.HistoryHotkeyVk != 0 ? "  (" + AppSettings.HotkeyText(cur.HistoryHotkeyMods, cur.HistoryHotkeyVk) + ")" : "";
            _miHistWin.Text = L.T("History list in game") + histKey;
            _miHistWin.On = cur.HistoryWinVisible;
        };
        _menu.Opened += (_, _) => Themes.RoundCorners(_menu.Handle, Themes.Blend(Themes.FromArgb(SettingsStore.Current.ThemePanelArgb), Themes.FromArgb(SettingsStore.Current.ThemeTextArgb), 0.16));
        _menu.Items.Add(new ToolStripSeparator());
        _miExit = _menu.Items.Add(L.T("Exit"), null, (_, _) => { _tray.Visible = false; Application.Exit(); });
        ApplyTrayTheme();

        _tray.Text = "Tarkov Appraiser";
        _tray.ContextMenuStrip = _menu;
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.Visible = true;
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_tray.BalloonTipTitle == UpdateTitle) AskForUpdate();
        };

        _hoverTimer.Tick += (_, _) => HoverTick();
        _hoverTimer.Start();
    }

    static double Dist(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // Is the active window the game? (process name cached per process id)
    bool GameIsForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != _fgPid)
            {
                _fgPid = pid;
                using var proc = Process.GetProcessById((int)pid);
                _fgIsGame = proc.ProcessName.StartsWith("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase);
            }
            return _fgIsGame;
        }
        catch
        {
            _fgPid = 0;
            return false;
        }
    }

    // Polls the mouse: when it rests for the configured time, run the recognition automatically.
    void HoverTick()
    {
        UpdateHistoryVisibility();

        if (_overlay.Visible && (GetAsyncKeyState(0x1B) & 0x8000) != 0)   // Esc
        {
            _overlay.HideNow();
            _anchored = false;
        }

        if (_anchored && Dist(Cursor.Position, _anchorAt) > _anchorDist)
        {
            _anchored = false;
            if (_overlay.Visible) _overlay.HideNow();
        }

        var s = SettingsStore.Current;
        if (!s.AutoHover || _autoPaused || _settingsForm?.ContainsFocus == true) return;   // not while working in the settings

        var p = Cursor.Position;
        bool moved = Math.Abs(p.X - _hoverPos.X) > 4 || Math.Abs(p.Y - _hoverPos.Y) > 4;

        // moving or dragging an item: restart the wait, hide an automatic overlay once the mouse left the item
        if (moved || MouseButtons != MouseButtons.None)
        {
            _hoverPos = p;
            _hoverSince = DateTime.UtcNow;
            _hoverHandled = false;
            _autoTries = 0;
            return;
        }

        if (_hoverHandled || _busy) return;
        if (_histWin is { Visible: true } && _histWin.Bounds.Contains(p)) return;   // over the in-game list: no tooltip there
        if ((DateTime.UtcNow - _hoverSince).TotalMilliseconds < s.AutoHoverDelayMs) return;
        if (s.AutoHoverOnlyInGame && !GameIsForeground()) return;

        _hoverHandled = true;   // one recognition per resting position
        _ = AutoCheckAsync();
    }

    // Automatic mode: when the game has not drawn its tooltip yet, look again on the next ticks
    // (only a screenshot + box search, no OCR) - for about one second per resting position.
    // Without a tooltip nothing is read: the mouse simply rests somewhere without an item.
    const int AutoBoxTries = 12;

    async Task AutoCheckAsync()
    {
        bool noTooltip = await CheckItemUnderCursorAsync(true);
        if (noTooltip && ++_autoTries < AutoBoxTries)
            _hoverHandled = false;   // try again on the next tick
    }

    // First start: short introduction (hotkey, text recognition check, test)
    void ShowWelcome()
    {
        bool openSettings;
        try
        {
            using var dlg = new WelcomeForm(SettingsStore.Current, PreviewOverlay);
            dlg.ShowDialog();
            openSettings = dlg.OpenSettings;
            _overlay.HideNow();
        }
        catch (Exception ex)
        {
            Log.Write("Welcome window: " + ex, true);
            openSettings = true;
        }
        var s = SettingsStore.Current;
        s.WelcomeDone = true;
        SettingsStore.Save(s);
        if (openSettings) OpenSettings();
    }

    // Stack hotkey: type how many are in the cell of the last recognized stackable item
    void AskStackCount()
    {
        var s = SettingsStore.Current;
        if (_lastItem == null || _lastItem.StackMax <= 1 || DateTime.UtcNow - _lastShown > TimeSpan.FromSeconds(30))
        {
            _toast.Pop(L.T("Show the price of a stack first (hover it, press the price hotkey)"), false, s);
            return;
        }
        var previous = GetForegroundWindow();   // the game gets the focus back afterwards
        int? count;
        using (var dlg = new StackInputForm(_lastItem, s, Cursor.Position))
        {
            dlg.ShowDialog();
            count = dlg.Count;
        }
        if (previous != IntPtr.Zero) SetForegroundWindow(previous);
        if (count == null) return;
        StackCount.Learn(count.Value);   // next time this number is recognized by its look

        _overlay.ShowItem(_lastItem, _lastCursor, s, _lastImage, count);
        History.Add(_lastItem, count);
        _anchored = true;
        _anchorAt = Cursor.Position;
        _anchorDist = 40;
        _lastShown = DateTime.UtcNow;
    }

    // Pause / resume the automatic mode (the hotkey keeps working)
    void TogglePause()
    {
        _autoPaused = !_autoPaused;
        _miPause.On = _autoPaused;
        var s = SettingsStore.Current;
        _toast.Pop(_autoPaused ? L.T("Automatic mode paused") : L.T("Automatic mode on"), _autoPaused, s);
        Log.Write("Automatic mode " + (_autoPaused ? "paused" : "resumed"));
    }

    // Asks GitHub for a newer release (see Updates.cs); shows a tray message and a menu entry
    async Task CheckForUpdatesAsync()
    {
        var release = await Updates.CheckAsync();
        if (release != null) Updates.Announce(release);   // -> OnUpdateFound (also from "Check for updates" in the settings)
    }

    void OnUpdateFound()
    {
        if (InvokeRequired) { BeginInvoke(new Action(OnUpdateFound)); return; }
        var release = Updates.Available;
        if (release == null || _updateUrl != null) return;
        _updateUrl = release.Url;
        _release = release;

        int at = _menu.Items.IndexOf(_miStatus) + 1;   // right under the header
        _menu.Items.Insert(at, new ToolStripMenuItem(L.T("Update available") + $": {release.Version} …", null, (_, _) => AskForUpdate()));
        ApplyTrayTheme();

        _tray.ShowBalloonTip(10000, UpdateTitle,
            string.Format(L.T("Version {0} is available (you have {1}). Click here to update."), release.Version, Changelog.Current),
            ToolTipIcon.Info);
    }

    Updates.Release? _release;
    bool _updating;

    // "Update now": download the new TarkovAppraiser.exe, swap it and restart
    async void AskForUpdate()
    {
        if (_release == null || _updating) return;
        var s = SettingsStore.Current;
        var palette = new UiDialog.Palette(Themes.FromArgb(s.ThemeBackArgb), Themes.FromArgb(s.ThemePanelArgb),
                                           Themes.FromArgb(s.ThemeTextArgb), Themes.FromArgb(s.ThemeAccentArgb));
        int choice = UiDialog.Show(null, palette, L.T("Update available"),
            string.Format(L.T("Version {0} is ready (you have {1}). Tarkov Appraiser downloads it and restarts - your settings stay."),
                _release.Version, Changelog.Current),
            0, L.T("Update now"), L.T("What's new"), L.T("Later"));
        if (choice == 1) { OpenUpdatePage(); return; }
        if (choice != 0) return;

        if (_release.DownloadUrl == null)
        {
            OpenUpdatePage();   // no exe attached to the release: download page
            return;
        }
        _updating = true;
        _tray.ShowBalloonTip(3000, "Tarkov Appraiser", L.T("Downloading the update …"), ToolTipIcon.Info);
        string? error = await Updates.InstallAsync(_release.DownloadUrl);
        if (error == null)
        {
            _tray.Visible = false;
            Application.Exit();   // the new version is already starting
            return;
        }
        _updating = false;
        Log.Write("Update failed: " + error, true);
        if (UiDialog.Show(null, palette, L.T("Update failed"),
                L.T("The update could not be installed automatically. Download it from the release page instead?") + "\n\n" + error,
                0, L.T("Open download page"), L.T("Cancel")) == 0)
            OpenUpdatePage();
    }

    void OpenUpdatePage()
    {
        if (_updateUrl == null) return;
        try { Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Could not open the download page: " + ex.Message, true); }
    }

    void ApplyTrayTheme()
    {
        var s = SettingsStore.Current;
        Themes.StyleMenu(_menu,
            Themes.FromArgb(s.ThemePanelArgb),
            Themes.FromArgb(s.ThemeTextArgb),
            Themes.FromArgb(s.ThemeAccentArgb));

        // texts in the current program language
        _miSettings.Text = L.T("Settings …");
        _miReload.Text = L.T("Reload prices");
        _miAuto.Text = L.T("Automatic mode");
        _miPause.Text = L.T("Pause automatic mode");
        _miRecent.Text = L.T("Recent items");
        _miHistWin.Text = L.T("History list in game");
        _miExit.Text = L.T("Exit");
        _miHeader.Font = UiFont.Semi(10f);

        // Menu icons in the current text color
        var fg = Themes.FromArgb(s.ThemeTextArgb);
        SetMenuIcon(_miSettings, "gear", fg);
        SetMenuIcon(_miReload, "reload", fg);
        SetMenuIcon(_miExit, "power", fg);
        SetMenuIcon(_miRecent, "history", fg);
        var oldLogo = _miHeader.Image;
        _miHeader.Image = Themes.Logo(40, Themes.FromArgb(s.ThemeAccentArgb));
        oldLogo?.Dispose();
        SetStatusDot();

        // Tray icon = app logo in the current accent color
        var oldIcon = _tray.Icon;
        _tray.Icon = Themes.LogoIcon(Themes.FromArgb(s.ThemeAccentArgb));
        oldIcon?.Dispose();
    }

    // small colored dot before the price status: green = fresh, orange = saved / old, red = none
    void SetStatusDot()
    {
        var c = PriceState.Health() switch
        {
            2 => Color.FromArgb(76, 195, 138),
            1 => Color.FromArgb(224, 165, 38),
            _ => Color.FromArgb(229, 72, 77)
        };
        var bmp = new Bitmap(40, 40);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(c);
            g.FillEllipse(b, 13, 13, 14, 14);
        }
        var old = _miStatus.Image;
        _miStatus.Image = bmp;
        old?.Dispose();
    }

    // ---- in-game history list ----
    void ToggleHistoryWindow()
    {
        if (_settingsForm != null) { _settingsForm.ToggleHistoryWindow(); return; }   // saved by the open window
        var s = SettingsStore.Current.Clone();
        s.HistoryWinVisible = !s.HistoryWinVisible;
        SettingsStore.Save(s);
        UpdateHistoryWindow();
    }

    void UpdateHistoryWindow()
    {
        var s = SettingsStore.Current;
        if (!s.HistoryWinVisible)
        {
            _histWin?.Hide();
            return;
        }
        if (_histWin == null || _histWin.IsDisposed)
        {
            _histWin = new HistoryWindow();
            _histWin.PlaceFrom(s);
            _histWin.BoundsSaved += SaveHistoryBounds;
            _histWin.CloseClicked += ToggleHistoryWindow;
        }
        _histWin.ApplyStyle(s);
        if (!_histWin.Visible && (!s.HistoryWinOnlyInGame || GameIsForeground() || OwnWindowInFront())) _histWin.Show();
    }

    // "only while Tarkov is in front": hide the list on the desktop / in other programs
    void UpdateHistoryVisibility()
    {
        if (_histWin == null || _histWin.IsDisposed) return;
        var s = SettingsStore.Current;
        if (!s.HistoryWinVisible) return;   // UpdateHistoryWindow handles switching it off
        bool show = !s.HistoryWinOnlyInGame || GameIsForeground() || OwnWindowInFront();
        if (show && !_histWin.Visible) _histWin.Show();
        else if (!show && _histWin.Visible) _histWin.Hide();
    }

    // the settings window, a dialog or the tray menu of Tarkov Appraiser is in front: keep the list
    static bool OwnWindowInFront()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    void SaveHistoryBounds()
    {
        if (_histWin == null) return;
        var s = SettingsStore.Current.Clone();
        var b = _histWin.Bounds;
        (s.HistoryWinX, s.HistoryWinY, s.HistoryWinW, s.HistoryWinH) = (b.X, b.Y, b.Width, b.Height);
        SettingsStore.Save(s);
    }

    // tray menu: switch the automatic mode on / off (saved like a change in the settings)
    void ToggleAutoMode()
    {
        _autoPaused = false;
        if (_settingsForm != null)
        {
            bool on = _settingsForm.ToggleAutoHover();   // saved by the open window
            _toast.Pop(on ? L.T("Automatic mode on") : L.T("Automatic mode off"), !on, SettingsStore.Current);
            return;
        }
        var s = SettingsStore.Current.Clone();
        s.AutoHover = !s.AutoHover;
        ApplyNewSettings(s);
        _toast.Pop(s.AutoHover ? L.T("Automatic mode on") : L.T("Automatic mode off"), !s.AutoHover, s);
        Log.Write("Automatic mode switched " + (s.AutoHover ? "on" : "off") + " from the tray menu");
    }

    // tray menu: the last checked items with their current value
    void FillRecentMenu()
    {
        foreach (ToolStripItem old in _miRecent.DropDownItems) old.Image?.Dispose();
        _miRecent.DropDownItems.Clear();
        var s = SettingsStore.Current;
        int n = 0;
        foreach (var r in History.Items)
        {
            var item = ById(r.Id);
            if (item == null) continue;
            long v = History.ValueOf(item, r.Count);
            string name = TextBuilder.DisplayName(item) + (r.Count is int c && item.StackMax > 1 ? $" ×{c}" : "");
            var mi = new ToolStripMenuItem(name, null, (_, _) => ShowRecent(item, r.Count))
            {
                ShortcutKeyDisplayString = v > 0 ? TextBuilder.Money(v) : "",
                Image = ItemImages.TryGet(item.Id) is Image img ? new Bitmap(img, 40, 40) : null
            };
            _miRecent.DropDownItems.Add(mi);
            if (++n >= 10) break;
        }
        if (n == 0)
            _miRecent.DropDownItems.Add(new ToolStripMenuItem(L.T("No items yet")) { Enabled = false });
        _miRecent.DropDownItems.Add(new ToolStripSeparator());
        _miRecent.DropDownItems.Add(new ToolStripMenuItem(L.T("Show all …"), null, (_, _) => OpenSettings(SettingsForm.HistoryPage)));
        Themes.StyleMenu(_miRecent.DropDown, Themes.FromArgb(s.ThemePanelArgb), Themes.FromArgb(s.ThemeTextArgb), Themes.FromArgb(s.ThemeAccentArgb));
    }

    async void ShowRecent(Item item, int? count)
    {
        var s = SettingsStore.Current;
        var at = Cursor.Position;
        // the price box of an earlier scan was tied to its item: this one is not (it stays for the display time)
        _anchored = false;
        // the automatic mode must not read here (it would hide this box right away)
        _hoverPos = at;
        _hoverHandled = true;
        Image? img = null;
        try { img = await ItemImages.GetAsync(item.Id, item.ImageUrl); } catch { }
        _anchored = false;
        _overlay.ShowItem(item, at, s, img, count);
    }

    static void SetMenuIcon(ToolStripItem item, string icon, Color color)
    {
        var old = item.Image;
        item.Image = Icons.Render(icon, 40, color);
        old?.Dispose();
    }

    // The main window stays invisible, but its handle must exist (otherwise no hotkey!)
    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        RegisterHotkey();

        if (!Ocr.EnsureEngine())
            _tray.ShowBalloonTip(6000, "Tarkov Appraiser",
                string.Format(L.T("No Windows text recognition found for '{0}'. Install the language (including text recognition) in the Windows settings."),
                    SettingsStore.Current.OcrLanguage), ToolTipIcon.Warning);

        ItemInfo.Loaded += () => { if (IsHandleCreated) BeginInvoke(new Action(() => _overlay.RefreshItem())); };
        AmmoStats.Loaded += () => { if (IsHandleCreated) BeginInvoke(new Action(() => _overlay.RefreshItem())); };

        TraderImages.LoadCached();
        if (SettingsStore.Current.ShowTraderImages)
            _ = TarkovApi.LoadTraderPortraitsAsync();
        _ = StartPricesAsync();
        Updates.Found += OnUpdateFound;
        Updates.InstallRequested = AskForUpdate;
        if (SettingsStore.Current.CheckForUpdates)
            _ = CheckForUpdatesAsync();

        UpdateHistoryWindow();   // in-game history list, when it was open last time

        if (!SettingsStore.Current.WelcomeDone)
            BeginInvoke(new Action(ShowWelcome));
        else if (!SettingsStore.Current.StartMinimized)
            BeginInvoke(new Action(() => OpenSettings()));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        UnregisterHotKey(Handle, PAUSE_HOTKEY_ID);
        UnregisterHotKey(Handle, STACK_HOTKEY_ID);
        UnregisterHotKey(Handle, HISTORY_HOTKEY_ID);
        _tray.Visible = false;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            // pressed again on the same item: hide the price box
            if (_overlay.Visible && _anchored && Dist(Cursor.Position, _anchorAt) <= _anchorDist)
            {
                _overlay.HideNow();
                _anchored = false;
                return;
            }
            BeginInvoke(new Action(async () => await CheckItemUnderCursorAsync(false)));
        }
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == PAUSE_HOTKEY_ID)
            BeginInvoke(new Action(TogglePause));
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == STACK_HOTKEY_ID)
            BeginInvoke(new Action(AskStackCount));
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HISTORY_HOTKEY_ID)
            BeginInvoke(new Action(ToggleHistoryWindow));
    }

    [DllImport("user32.dll")]
    static extern bool EnableWindow(IntPtr hWnd, bool enable);

    void ReleaseHotkeys()
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        UnregisterHotKey(Handle, PAUSE_HOTKEY_ID);
        UnregisterHotKey(Handle, STACK_HOTKEY_ID);
        UnregisterHotKey(Handle, HISTORY_HOTKEY_ID);
    }

    void RegisterHotkey()
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        UnregisterHotKey(Handle, PAUSE_HOTKEY_ID);
        UnregisterHotKey(Handle, STACK_HOTKEY_ID);
        UnregisterHotKey(Handle, HISTORY_HOTKEY_ID);
        var s = SettingsStore.Current;
        if (s.StackHotkeyVk != 0 && !RegisterHotKey(Handle, STACK_HOTKEY_ID, s.StackHotkeyMods | MOD_NOREPEAT, s.StackHotkeyVk))
            Log.Write("Could not register the stack hotkey: " + AppSettings.HotkeyText(s.StackHotkeyMods, s.StackHotkeyVk), true);
        if (s.HistoryHotkeyVk != 0 && !RegisterHotKey(Handle, HISTORY_HOTKEY_ID, s.HistoryHotkeyMods | MOD_NOREPEAT, s.HistoryHotkeyVk))
            Log.Write("Could not register the history hotkey: " + AppSettings.HotkeyText(s.HistoryHotkeyMods, s.HistoryHotkeyVk), true);
        if (s.PauseHotkeyVk != 0 && !RegisterHotKey(Handle, PAUSE_HOTKEY_ID, s.PauseHotkeyMods | MOD_NOREPEAT, s.PauseHotkeyVk))
            Log.Write("Could not register the pause hotkey: " + AppSettings.HotkeyText(s.PauseHotkeyMods, s.PauseHotkeyVk), true);
        if (!RegisterHotKey(Handle, HOTKEY_ID, s.HotkeyMods | MOD_NOREPEAT, s.HotkeyVk))
        {
            Log.Write("Could not register hotkey: " + AppSettings.HotkeyText(s.HotkeyMods, s.HotkeyVk), true);
            _tray.ShowBalloonTip(6000, "Tarkov Appraiser",
                string.Format(L.T("Hotkey {0} could not be registered (it may be used by another program). Please change it in the settings."),
                    AppSettings.HotkeyText(s.HotkeyMods, s.HotkeyVk)), ToolTipIcon.Warning);
        }
    }

    // ------------------------------------------------------------
    //  Settings
    // ------------------------------------------------------------
    void OpenSettings(int page = 0)
    {
        if (_settingsOpen) return;
        _settingsOpen = true;

        // The hotkeys keep working while the window is open; they are only released
        // while a hotkey field has the focus (so the keys can be recorded there).
        try
        {
            // Changes inside the window are applied and saved right away.
            // After a language change the window closes and opens again in the new language.
            bool reopen;
            do
            {
                using var dlg = new SettingsForm(SettingsStore.Current, PreviewOverlay, ApplyNewSettings, () => LoadPricesAsync(false));
                if (page > 0) dlg.ShowPage(page);
                page = 0;
                dlg.HotkeyRecording += rec =>
                {
                    _recording = rec;
                    if (rec) ReleaseHotkeys();
                    else RegisterHotkey();
                };
                _settingsForm = dlg;
                // the window is modal: keep the in-game list movable while it is open
                dlg.Shown += (_, _) => { if (_histWin is { IsHandleCreated: true }) EnableWindow(_histWin.Handle, true); };
                dlg.ShowDialog();
                _settingsForm = null;
                reopen = dlg.ReopenRequested;
            } while (reopen);
        }
        catch (Exception ex)
        {
            Log.Write("Settings window: " + ex, true);
            MessageBox.Show(L.T("Settings window error:") + " " + ex.Message);
        }
        finally
        {
            _settingsOpen = false;
            _settingsForm = null;
            _recording = false;
            _overlay.HideNow();
            RegisterHotkey();
        }
    }

    void ApplyNewSettings(AppSettings newSettings)
    {
        var old = SettingsStore.Current;
        SettingsStore.Save(newSettings);
        var s = SettingsStore.Current;

        Autostart.Set(s.StartWithWindows);
        Ocr.EnsureEngine();
        if (old.ShowInScreenshots != s.ShowInScreenshots)
        {
            _overlay.UpdateCapture();
            _toast.UpdateCapture();
            _histWin?.UpdateCapture();
        }
        bool keysChanged = old.HotkeyMods != s.HotkeyMods || old.HotkeyVk != s.HotkeyVk ||
                           old.PauseHotkeyMods != s.PauseHotkeyMods || old.PauseHotkeyVk != s.PauseHotkeyVk ||
                           old.StackHotkeyMods != s.StackHotkeyMods || old.StackHotkeyVk != s.StackHotkeyVk ||
                           old.HistoryHotkeyMods != s.HistoryHotkeyMods || old.HistoryHotkeyVk != s.HistoryHotkeyVk;
        if (keysChanged && !_recording) RegisterHotkey();   // new keys work right away
        ApplyTrayTheme();
        UpdateHistoryWindow();

        if (s.ShowTraderImages && !old.ShowTraderImages)
            _ = TarkovApi.LoadTraderPortraitsAsync();

        if (old.ApiLanguage != s.ApiLanguage || old.GameMode != s.GameMode)
            _ = LoadPricesAsync(false);
        if (old.GameMode != s.GameMode)
            _ = AmmoStats.LoadAsync(s);
        if (old.GameMode != s.GameMode || old.ApiLanguage != s.ApiLanguage)
            _ = ItemInfo.LoadAsync(s);
    }

    void PreviewOverlay(AppSettings s)
    {
        var sample = Sample(_sampleIndex);
        _sampleIndex++;
        _anchored = false;   // the preview stays visible while the mouse moves
        var pic = PictureOf(sample);
        var img = s.ShowItemImage ? PlaceholderFor(sample, s) ?? ItemImages.TryGet(pic.Id) : null;
        _overlay.ShowItem(sample, Cursor.Position, s, img);

        // picture not cached yet: download it, then refresh the preview
        if (s.ShowItemImage && img == null && pic.Id.Length > 0)
            _ = RefreshPreviewImageAsync(sample, s);
    }

    async Task RefreshPreviewImageAsync(Item sample, AppSettings s)
    {
        var pic = PictureOf(sample);
        var img = await ItemImages.GetAsync(pic.Id, pic.Url);
        if (img != null && _overlay.Visible)
            _overlay.ShowItem(sample, Cursor.Position, s, img);
    }

    // ------------------------------------------------------------
    //  Prices & recognition
    // ------------------------------------------------------------
    // Startup: use the saved price list right away (the hotkey works immediately),
    // then download the current prices in the background
    async Task StartPricesAsync()
    {
        var s = SettingsStore.Current;
        _ = AmmoStats.LoadAsync(s);   // saved values right away, current ones in the background
        _ = ItemInfo.LoadAsync(s);
        try
        {
            var saved = await Task.Run(() => TarkovApi.LoadSaved(s));
            if (saved is { } sv && _items.Count == 0)
            {
                _items = sv.Items;
                SetPrices(_items);
            History.Refresh();   // the lists show the new prices
                PriceState.Count = _items.Count;
                PriceState.FromCache = true;
                PriceState.Updated = sv.Time;
                Log.Write($"Started with {_items.Count} saved prices from {sv.Time:g}, downloading current prices ...");
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read the saved prices: " + ex.Message, true);
        }
        await LoadPricesAsync(false);
    }

    async Task LoadPricesAsync(bool manual)
    {
        if (_loading) return;
        _loading = true;
        PriceState.Loading = true;
        try
        {
            var s = SettingsStore.Current;
            _items = await TarkovApi.LoadItemsAsync(s);
            SetPrices(_items);
            History.Refresh();   // the lists show the new prices
            PriceState.Count = _items.Count;
            PriceState.FromCache = TarkovApi.UsedCache;
            PriceState.Updated = TarkovApi.UsedCache ? TarkovApi.CacheTime : DateTime.Now;
            PriceState.Error = null;

            if (TarkovApi.UsedCache)
            {
                // try again in about 2 minutes
                _lastLoad = DateTime.UtcNow.AddMinutes(-Math.Max(0, s.PriceRefreshMinutes - 2));
                Log.Write($"tarkov.dev unreachable - {_items.Count} items from cache (saved {TarkovApi.CacheTime}).", true);
                _tray.ShowBalloonTip(5000, "Tarkov Appraiser",
                    string.Format(L.T("tarkov.dev is currently unreachable. Using saved prices from {0}."), TarkovApi.CacheTime.ToString("g")),
                    ToolTipIcon.Warning);
            }
            else
            {
                _lastLoad = DateTime.UtcNow;
                Log.Write(string.Format(L.T("{0} items loaded."), _items.Count));
                if (manual)
                    _tray.ShowBalloonTip(2000, "Tarkov Appraiser", $"{_items.Count} items loaded.", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not load prices: " + ex.Message, true);
            PriceState.Error = ex.Message;
            _tray.ShowBalloonTip(5000, "Tarkov Appraiser",
                L.T("Could not load prices (tarkov.dev is not responding). Try again later: tray menu > Reload prices."),
                ToolTipIcon.Error);
        }
        finally
        {
            _loading = false;
            PriceState.Loading = false;
            _tray.Text = Truncate63("Tarkov Appraiser - " + PriceState.Short());
        }
    }

    static string Truncate63(string t) => t.Length <= 63 ? t : t.Substring(0, 63);

    // Debug log only: keeps the screenshots of failed recognitions (the newest 30) for troubleshooting
    static void SaveFailedShot(Bitmap bmp, string tag = "")
    {
        try
        {
            string dir = Path.Combine(SettingsStore.Dir, "failed");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}{tag}.png");
            bmp.Save(file, ImageFormat.Png);
            Log.Write("Screenshot saved: " + file);

            foreach (var old in new DirectoryInfo(dir).GetFiles("*.png")
                         .OrderByDescending(f => f.Name).Skip(30))
                old.Delete();
        }
        catch (Exception ex)
        {
            Log.Write("Could not save debug screenshot: " + ex.Message);
        }
    }

    // auto = triggered by hovering (silent on failure, only shows the overlay when an item was found)
    // Returns true when automatic mode found no tooltip on the screen yet (the caller may look again shortly).
    async Task<bool> CheckItemUnderCursorAsync(bool auto)
    {
        if (_busy) return false;
        _busy = true;
        _anchored = false;
        var s = SettingsStore.Current;
        try
        {
            bool stale = DateTime.UtcNow - _lastLoad > TimeSpan.FromMinutes(s.PriceRefreshMinutes);
            if (_items.Count == 0)
                await LoadPricesAsync(false);   // no data yet: wait for it
            else if (stale)
                _ = LoadPricesAsync(false);     // outdated: refresh in the background, do not block

            var cursor = Cursor.Position;

            if (_items.Count == 0)
            {
                if (auto) return false;
                _overlay.ShowMessage(_loading
                    ? L.T("Prices are still loading ...")
                    : L.T("Prices unavailable (tarkov.dev unreachable).") + "\n" + L.T("Tray menu > Reload prices."), cursor, s);
                return false;
            }

            if (!Ocr.EnsureEngine())
            {
                if (auto) return false;
                _overlay.ShowMessage(string.Format(L.T("No text recognition installed for '{0}'."), s.OcrLanguage), cursor, s);
                return false;
            }

            bool wasVisible = _overlay.Visible;
            _overlay.HideNow();
            bool waitForHide = wasVisible && !_overlay.ExcludedFromCapture;

            // the capture area is set for a 768 px high screen: larger resolutions show larger tooltips
            var res = Resolution.Detect(cursor, s);
            int screenH = res.UsedHeight;
            float fs = res.Factor;
            var rect = new Rectangle(
                cursor.X + (int)(s.CaptureOffsetX * fs), cursor.Y + (int)(s.CaptureOffsetY * fs),
                (int)(s.CaptureWidth * fs), (int)(s.CaptureHeight * fs));
            rect.Intersect(SystemInformation.VirtualScreen);
            if (rect.Width < 50 || rect.Height < 50) return false;

            // the in-game list is visible in screenshots and lies in the area: hide it for the moment of the capture
            bool hideList = _histWin is { Visible: true, ExcludedFromCapture: false } && _histWin.Bounds.IntersectsWith(rect);
            if (hideList) { _histWin!.Hide(); waitForHide = true; }
            if (waitForHide) await Task.Delay(40);   // make sure our windows are not part of the screenshot

            using var shot = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
            if (hideList) _histWin!.Show();

            var items = _items;
            int maxDim = (int)OcrEngine.MaxImageDimension;   // Windows OCR rejects larger pictures
            Item? item = null;
            Item? closest = null;
            Item? other = null;   // set when the result was ambiguous (closest vs. other)
            double score = 0;

            // 1. Find the game's tooltip box next to the cursor and read ONLY that box
            //    (no inventory labels, no line merging with neighbouring text)
            var sw = Stopwatch.StartNew();
            var cursorInShot = new Point(cursor.X - rect.X, cursor.Y - rect.Y);
            var box = TooltipFinder.Nearest(TooltipFinder.Find(shot), cursorInShot, (int)(120 * fs));
            Func<Item, bool>? widthFilter = null;   // names that fit the found tooltip box
            long findMs = sw.ElapsedMilliseconds;

            if (box is Rectangle b)
            {
                // text size compared to the 768 px screen the readings were tuned on
                float ft = TooltipFinder.SizeFactor(b, screenH);
                Log.Write($"Tooltip {b.Width}x{b.Height} found in {findMs} ms ({res.Source} {screenH} px, size x{ft:0.##})");

                // Several readings of the small cut-out: Windows OCR misreads different words at different
                // sizes / contrasts ("7.62x39" -> "763<39", "Awl" -> nothing). All readings are matched
                // together; it stops as soon as one gives a sure result.
                float maxTipScale = Math.Max(1f, (maxDim - 40f) / Math.Max(b.Width, b.Height));
                var variants = new List<(bool Invert, int Scale, int Copies, bool Binary)>
                {
                    (true, 3, 1, false), (false, 2, 1, false), (true, 5, 1, false), (false, 4, 1, false)
                };
                // short names ("Awl", letters only about 8 px high): much larger and hard black / white
                bool shortName = b.Width < b.Height * 3;
                if (shortName)
                {
                    // tiled "Awl Awl Awl" first (see TooltipFinder.PrepareTiled), then the larger variants
                    variants.Insert(0, (true, 3, 3, false));
                    variants.Insert(1, (false, 3, 3, false));
                    variants.Insert(2, (true, 4, 4, false));
                    variants.Add((true, 3, 1, true));
                    variants.Add((true, 6, 1, false));
                }
                var readings = new List<List<OcrLineInfo>>();
                foreach (var (invert, wanted, copies, binary) in variants)
                {
                    // enlarge to the same text size as on the tuned screen
                    float tipScale = Math.Max(1f, Math.Min(wanted / ft, maxTipScale));
                    List<OcrLineInfo> tipLines;
                    if (copies == 1)
                    {
                        using var tip = TooltipFinder.PrepareForOcr(shot, b, tipScale, invert, binary);
                        tipLines = await Ocr.ReadLinesAsync(tip, splitGaps: false);
                    }
                    else
                    {
                        using var tip = TooltipFinder.PrepareTiled(shot, b, Math.Max(1f, wanted / ft), copies, invert, maxDim);
                        if (tip == null) continue;
                        var read = await Ocr.ReadLinesAsync(tip, splitGaps: false);
                        // "Awl Awl Awl": keep the first copy's words
                        var words = Matcher.TooltipText(read).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        int keep = (int)Math.Round((double)words.Length / copies);
                        tipLines = keep == 0 ? new List<OcrLineInfo>()
                            : new List<OcrLineInfo> { new(string.Join(" ", words.Take(keep)), 0, 0, 0, 0, 0, 1) };
                        Log.Write($"Tooltip OCR (tiled {copies}x, {(invert ? "inverted" : "plain")}) raw: {string.Join(" ", words)}");
                    }
                    Log.Write($"Tooltip OCR ({(binary ? "black/white" : invert ? "inverted" : "plain")} x{(copies > 1 ? wanted / ft : tipScale):0.##}" +
                              (copies > 1 ? $", tiled {copies}x" : "") + "): " + Matcher.TooltipText(tipLines));
                    if (tipLines.Count == 0) continue;
                    readings.Add(tipLines);

                    sw.Restart();
                    (item, score, closest, other) = await Task.Run(() => Matcher.FindInTooltip(readings, items, s, b));
                    Log.Write((item == null
                        ? $"Tooltip: no match (closest: {closest?.Name} at {score:P0})"
                        : $"Tooltip match: {item.Name} ({score:P0})") + $"  [matching {sw.ElapsedMilliseconds} ms]");

                    if (item != null && score >= Matcher.SureScore) break;   // sure enough
                }
                widthFilter = Matcher.TooltipWidthFilter(b, readings);
            }
            else
            {
                Log.Write($"No tooltip box found next to the cursor ({findMs} ms)");
                if (auto)
                {
                    // keep this picture: the stack number is not covered by the tooltip yet
                    _cleanShot?.Dispose();
                    _cleanShot = (Bitmap)shot.Clone();
                    _cleanRect = rect;
                    _cleanCursor = cursor;
                    _cleanTime = DateTime.UtcNow;
                    return true;
                }   // tooltip not visible (yet): look again shortly, no OCR needed
                if (s.DebugLog)
                    SaveFailedShot(shot, "-nobox");   // helps to improve the box detection
            }

            // 2. Fallback (hotkey only): read the whole capture area. Automatic mode never guesses from
            //    the inventory labels around the tooltip.
            if (item == null && !auto)
            {
                // 2x upscaling greatly improves OCR for small fonts (limited by the OCR size limit)
                int scale = fs >= 1.8f ? 1 : Math.Clamp(maxDim / Math.Max(shot.Width, shot.Height), 1, 2);

                using var scaled = new Bitmap(shot.Width * scale, shot.Height * scale);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shot, 0, 0, scaled.Width, scaled.Height);
                }

                var lines = await Ocr.ReadLinesAsync(scaled);

                // Cursor position inside the (scaled) screenshot
                double cx = cursorInShot.X * scale;
                double cy = cursorInShot.Y * scale;

                sw.Restart();
                // a found tooltip box also limits the fallback to names of the right length
                var (fItem, fScore, fClosest, fOther) = await Task.Run(() => Matcher.Find(lines, items, s, cx, cy, widthFilter));

                Log.Write("OCR (whole area): " + string.Join(" | ", lines.Select(l => l.Text)));
                Log.Write((fItem == null
                    ? $"No match (closest: {fClosest?.Name} at {fScore:P0})"
                    : $"Match: {fItem.Name} ({fScore:P0})") + $"  [matching {sw.ElapsedMilliseconds} ms]");

                if (fItem != null || (other == null && fScore > score))
                    (item, score, closest, other) = (fItem, fScore, fClosest, fOther);
            }

            if (item == null && s.DebugLog && box != null)
                SaveFailedShot(shot);   // (without a box the picture was already saved above)

            if (item == null)
            {
                if (auto) return false;
                string msg;
                if (closest != null && other != null)
                    msg = L.T("Not sure which item this is:") + $"\n{closest.Name}\n" + L.T("or") + $" {other.Name}";
                else
                {
                    msg = L.T("No item recognized.") + "\n" + L.T("Hold the mouse still over the item until the tooltip is visible.");
                    if (closest != null && score >= 0.45)
                        msg += "\n" + L.T("Closest guess:") + $" {closest.Name} ({score:P0})";
                }
                _overlay.ShowMessage(msg, cursor, s);
                return false;
            }

            // item picture: wait at most 1.5 s (otherwise show the prices right away)
            Image? itemImg = null;
            if (s.ShowItemImage && item.Id.Length > 0)
            {
                var imgTask = ItemImages.GetAsync(item.Id, item.ImageUrl);
                await Task.WhenAny(imgTask, Task.Delay(1500));
                if (imgTask.IsCompletedSuccessfully) itemImg = imgTask.Result;
            }

            // automatic mode: the mouse already moved on while the OCR was running -> do not show a stale overlay
            if (auto && (Dist(Cursor.Position, cursor) > 12 || !s.AutoHover)) return false;

            // ammo and other stacks: how many are in the cell (only used when the reading is plausible)
            int? stackCount = null;
            if (item.StackMax > 1 && s.ReadStackCount)
            {
                try
                {
                    // first the picture from just before the tooltip appeared (automatic mode): nothing covers the number there
                    if (_cleanShot != null && Dist(_cleanCursor, cursor) <= 4 && DateTime.UtcNow - _cleanTime < TimeSpan.FromSeconds(3))
                        stackCount = await StackCount.ReadAsync(_cleanShot, new Point(cursor.X - _cleanRect.X, cursor.Y - _cleanRect.Y),
                                                                fs, null, item.StackMax, "before tooltip");
                    stackCount ??= await StackCount.ReadAsync(shot, cursorInShot, fs, box, item.StackMax);
                }
                catch (Exception ex) { Log.Write("Stack count: " + ex.Message); }
            }

            // armor: the real durability of this piece ("179/190" in the corner of the item)
            (int Cur, int Max)? durability = null;
            if (s.ShowArmor && ItemInfo.ArmorOf(item.Id) is { Durability: > 0 } armor)
            {
                try
                {
                    if (_cleanShot != null && Dist(_cleanCursor, cursor) <= 4 && DateTime.UtcNow - _cleanTime < TimeSpan.FromSeconds(3))
                        durability = await StackCount.ReadDurabilityAsync(_cleanShot, new Point(cursor.X - _cleanRect.X, cursor.Y - _cleanRect.Y),
                                                                          fs, null, armor.Durability);
                    durability ??= await StackCount.ReadDurabilityAsync(shot, cursorInShot, fs, box, armor.Durability);
                }
                catch (Exception ex) { Log.Write("Durability: " + ex.Message); }
            }
            // keys: the uses left ("37/40" in red in the corner of the key) - passed on the same way as the durability
            else if (s.ShowKeyInfo && ItemInfo.KeyUses(item.Id) is > 0 and var maxUses)
            {
                try
                {
                    if (_cleanShot != null && Dist(_cleanCursor, cursor) <= 4 && DateTime.UtcNow - _cleanTime < TimeSpan.FromSeconds(3))
                        durability = await StackCount.ReadKeyUsesAsync(_cleanShot, new Point(cursor.X - _cleanRect.X, cursor.Y - _cleanRect.Y),
                                                                       fs, null, maxUses);
                    durability ??= await StackCount.ReadKeyUsesAsync(shot, cursorInShot, fs, box, maxUses);
                }
                catch (Exception ex) { Log.Write("Key uses: " + ex.Message); }
            }

            _overlay.ShowItem(item, cursor, s, itemImg, stackCount, durability);
            History.Add(item, stackCount);
            _lastItem = item;
            _lastImage = itemImg;
            _lastCursor = cursor;
            _lastShown = DateTime.UtcNow;
            _anchored = true;
            _anchorAt = cursor;
            _anchorDist = (int)((auto ? 12 : 30) * fs);   // hotkey: a little more room for small mouse movements

            return false;
        }
        catch (Exception ex)
        {
            Log.Write("Recognition error: " + ex, true);
            if (!auto) _overlay.ShowMessage(L.T("Error:") + " " + ex.Message, Cursor.Position, s);
            return false;
        }
        finally
        {
            _busy = false;
        }
    }
}
