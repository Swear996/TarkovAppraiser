using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;

namespace TarkovPriceOverlay;

public sealed record Choice(string Value, string Text)
{
    public override string ToString() => L.T(Text);
}

// ------------------------------------------------------------
//  Settings window (Fluent): sidebar with pages and price status, settings grouped in cards,
//  live preview of the overlay on the right.
//  Split into several files (partial class):
//    SettingsForm.cs          fields, constructor, window frame (drag, maximize, round corners)
//    SettingsForm.Pages.cs    sidebar and the content of every page
//    SettingsForm.Live.cs     live preview and change tracking
//    SettingsForm.Values.cs   settings <-> controls, auto-save
//    SettingsForm.Theme.cs    colors
//    SettingsForm.Helpers.cs  hotkeys, export / import, dialogs
//    SettingsForm.Layout.cs   card layout helpers
// ------------------------------------------------------------
public sealed partial class SettingsForm : Form
{
    // A page: the content sits on Body, which is moved up and down (mouse wheel or the thin
    // scroll bar on the right). No Windows scroll bars, so nothing scrolls sideways.
    sealed class PagePanel : Panel
    {
        public int Y = 4;
        public readonly Panel Body = new() { Left = 0, Top = 0, Tag = "page" };
        public readonly ModernScrollBar Bar = new();
        int _offset;

        public PagePanel()
        {
            Dock = DockStyle.Fill;
            Visible = false;
            DoubleBuffered = true;
            Controls.Add(Bar);    // added first = on top of the body
            Controls.Add(Body);
            Bar.Scroll += (_, _) => ScrollTo(Bar.Value);
            Body.ControlAdded += (_, e) =>
            {
                if (e.Control != null) e.Control.SizeChanged += (_, _) => UpdateExtent();
            };
        }

        protected override void OnResize(EventArgs e)
        {
            Body.Width = ClientSize.Width;
            Bar.SetBounds(ClientSize.Width - Bar.Width - 2, 0, Bar.Width, ClientSize.Height);
            base.OnResize(e);   // raises Resize: the cards are fitted to the new width
            UpdateExtent();
        }

        // content height changed (cards built, changelog entry opened ...)
        public void UpdateExtent()
        {
            int content = 0;
            foreach (Control c in Body.Controls) content = Math.Max(content, c.Bottom);
            content += 18;
            Body.Height = Math.Max(content, ClientSize.Height);
            Bar.Maximum = content;
            Bar.LargeChange = Math.Max(1, ClientSize.Height);
            Bar.Visible = content > ClientSize.Height;
            ScrollTo(_offset);
            Bar.Invalidate();
        }

        // scrolls so that a card is fully visible (search result)
        public void Reveal(Control c)
        {
            if (c.Top + Body.Top < 0 || c.Bottom + Body.Top > ClientSize.Height)
                ScrollTo(c.Top - 12);
        }

        void ScrollTo(int value)
        {
            _offset = Math.Clamp(value, 0, Math.Max(0, Body.Height - ClientSize.Height));
            if (Body.Top != -_offset) Body.Top = -_offset;
            Bar.Value = _offset;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(_offset - e.Delta / 2);   // one notch = 60 px
            if (e is HandledMouseEventArgs h) h.Handled = true;
        }
    }

    // ------------------------------------------------------------
    //  Window dragging (the window has no title bar)
    // ------------------------------------------------------------
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HTCAPTION = 2;

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ------------------------------------------------------------
    //  Layout
    // ------------------------------------------------------------
    const int WindowW = 1100, WindowH = 690;
    const int SideW = 210, LiveW = 360;
    const int CardX = 24, CardW = 474, CardPad = 16, RowH = 44;

    // ------------------------------------------------------------
    //  State
    // ------------------------------------------------------------
    readonly Action<AppSettings> _preview;
    readonly Action<AppSettings> _apply;   // applies + stores the settings (window stays open)
    readonly Func<Task> _refreshPrices;    // reloads the price list
    readonly System.Windows.Forms.Timer _priceTimer = new() { Interval = 1000 };
    readonly ToolTip _tips = new();
    readonly Txt _priceStatus = new() { UseMnemonic = false, Tag = "status" };
    readonly Txt _priceDot = new() { UseMnemonic = false, Tag = "dot", Text = "●" };
    Button? _btnUpdate;
    bool _updating;
    string _priceTip = "";
    readonly Txt _savedLbl = new()
    {
        Text = "✓  Settings saved",
        AutoSize = true,
        Visible = false,
        UseMnemonic = false,
        Tag = "saved",
        Font = UiFont.Semi(9.5f),
        Margin = new Padding(0, 9, 14, 0)
    };
    readonly System.Windows.Forms.Timer _savedTimer = new() { Interval = 2500 };
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 400 };   // short pause after a change, then save
    bool _populating;   // controls are being filled: no auto-save
    public AppSettings? Result { get; private set; }

    uint _mods;
    uint _vk;
    int _backArgb;   // overlay background
    int _textArgb;   // overlay text
    int _thBack, _thPanel, _thText, _thAccent;   // window theme
    string _preset = "";
    readonly int[] _tierColors = new int[5];
    bool _regionFallback;
    Icon? _ownIcon;

    // layout containers
    readonly Panel _side = new() { Dock = DockStyle.Left, Width = SideW, Tag = "side" };
    readonly Panel _livePanel = new() { Dock = DockStyle.Right, Width = LiveW, Tag = "page" };
    readonly Panel _main = new() { Dock = DockStyle.Fill, Tag = "page" };
    readonly Panel _header = new() { Dock = DockStyle.Top, Height = 84, Tag = "page" };
    readonly Txt _pageTitle = new() { Left = 24, Top = 20, Width = 700, Height = 32, UseMnemonic = false, Tag = "title" };
    readonly Txt _pageSub = new() { Left = 24, Top = 54, Width = 700, Height = 20, UseMnemonic = false, Tag = "muted" };
    readonly Pill _minBtn = new() { Tag = "chrome", Width = 34, Height = 28, Top = 14, TabStop = false };
    readonly Pill _maxBtn = new() { Tag = "chrome", Width = 34, Height = 28, Top = 14, TabStop = false };
    readonly Pill _closeBtn = new() { Tag = "close", Width = 34, Height = 28, Top = 14, TabStop = false };
    int _overlayPage;
    readonly PictureBox _logo = new()
    {
        Left = 20, Top = 22, Width = 36, Height = 36,
        SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent
    };
    readonly PictureBox _aboutLogo = new()
    {
        Left = 16, Top = 16, Width = 52, Height = 52,
        SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent
    };
    readonly FlowLayoutPanel _bottom = new()
    {
        Dock = DockStyle.Bottom,
        Height = 62,
        FlowDirection = FlowDirection.RightToLeft,
        Padding = new Padding(18, 12, 26, 10),
        Tag = "page"
    };
    readonly List<PagePanel> _pages = new();
    readonly List<(string Title, string Sub)> _pageInfo = new();
    readonly List<NavButton> _navs = new();
    readonly List<ColorDot> _presetDots = new();
    readonly Txt _presetName = new() { UseMnemonic = false, Tag = "muted" };

    // ---- Live preview ----
    readonly OverlayPreview _live = new() { Left = 0, Top = 30, Width = LiveW - 24, Height = 440 };
    readonly Button _liveNext = MakeButton("Next item", 160);
    readonly Button _liveTest = MakeButton("Test on screen", 160);
    int _liveIndex;

    // ---- General ----
    readonly TextBox _hotkey = new() { ReadOnly = true };
    readonly TextBox _pauseHotkey = new() { ReadOnly = true };
    uint _pauseMods, _pauseVk;
    readonly TextBox _stackHotkey = new() { ReadOnly = true };
    uint _stackMods, _stackVk;
    readonly TextBox _histHotkey = new() { ReadOnly = true };
    uint _histMods, _histVk;
    readonly ModernCheck _histWin = new() { Text = "Show the list in game" };
    readonly ModernCheck _histOnlyGame = new() { Text = "Only while Tarkov is in front" };
    readonly NumericUpDown _histOpacity = Num(20, 100);
    readonly Txt _raidInfo = new() { UseMnemonic = false };
    readonly ModernCheck _readStack = new() { Text = "Read the stack count (e.g. 18 rounds)" };
    readonly ComboBox _uiLang = new ModernCombo();
    readonly string _uiLangAtStart = L.Language;            // language this window was built in
    public bool ReopenRequested { get; private set; }   // language changed: the window is rebuilt
    readonly ModernCheck _autostart = new() { Text = "Start with Windows" };
    readonly ModernCheck _startMin = new() { Text = "Start minimized to the tray" };
    readonly ModernCheck _checkUpdates = new() { Text = "Check for a new version at startup" };
    readonly ComboBox _gameMode = new ModernCombo();
    readonly NumericUpDown _refresh = Num(1, 120);

    // ---- Detection ----
    readonly ComboBox _apiLang = new ModernCombo();
    readonly ComboBox _ocrLang = new ModernCombo();
    readonly NumericUpDown _minSim = Num(50, 95);
    readonly ModernCheck _autoHover = new() { Text = "Show prices when the mouse rests on an item" };
    readonly NumericUpDown _autoDelay = Num(200, 3000);
    readonly ModernCheck _autoOnlyGame = new() { Text = "Only while Tarkov is the active window" };
    readonly ComboBox _resolution = new ModernCombo();
    readonly Txt _resInfo = new() { UseMnemonic = false, Tag = "muted" };
    readonly System.Windows.Forms.Timer _resTimer = new() { Interval = 2000 };
    readonly NumericUpDown _capW = Num(200, 1920);
    readonly NumericUpDown _capH = Num(100, 1080);
    readonly NumericUpDown _capX = Num(-1500, 1500);
    readonly NumericUpDown _capY = Num(-1500, 1500);

    // ---- Overlay ----
    readonly ComboBox _position = new ModernCombo();
    readonly NumericUpDown _seconds = Num(1, 30);
    readonly NumericUpDown _offX = Num(-500, 500);
    readonly NumericUpDown _offY = Num(-500, 500);
    readonly ModernCheck _showLow = new() { Text = "Lowest flea price" };
    readonly ModernCheck _showAvg = new() { Text = "24h average" };
    readonly ModernCheck _showNet = new() { Text = "After the flea fee" };
    readonly ModernCheck _showBestOpt = new() { Text = "Where to sell" };
    readonly ModernCheck _showBest = new() { Text = "Best trader" };
    readonly ModernCheck _showAll = new() { Text = "All traders" };
    readonly ModernCheck _showSlot = new() { Text = "Price per slot" };
    readonly ModernCheck _showItemImg = new() { Text = "Item picture" };
    readonly ModernCheck _showTraderImg = new() { Text = "Trader portraits" };
    readonly ModernCheck _showAmmo = new() { Text = "Ammo values" };
    readonly ModernCheck _showArmor = new() { Text = "Armor class" };
    readonly ModernCheck _showKey = new() { Text = "Key info" };
    readonly ModernCheck _showUses = new() { Text = "Barter / Craft tip" };
    readonly ModernCheck _dimJunk = new() { Text = "Dim junk (below the first tier)" };
    readonly ModernCheck _compact = new() { Text = "Compact - name and best price only" };
    readonly ModernCheck _fade = new() { Text = "Fade in" };
    readonly ModernCheck _inShots = new() { Text = "Show in screenshots and recordings" };
    readonly ModernCheck _intel = new() { Text = "Intelligence Center level 3 (30 % lower fee)" };

    // ---- Loot tiers ----
    readonly ModernCheck _useTiers = new() { Text = "Color prices by tier" };
    readonly ComboBox _tierBasis = new ModernCombo();
    readonly ModernCheck _tierName = new() { Text = "Color the item name" };
    readonly ModernCheck _tierLabel = new() { Text = "Show the tier name" };
    readonly ModernCheck _ammoTier = new() { Text = "Ammo: tier by how good it is" };
    readonly ModernCheck _tierFrame = new() { Text = "Frame the price box in the tier color" };
    readonly Txt[] _tierLbl = new Txt[5];
    readonly NumericUpDown[] _tierTh = new NumericUpDown[4];
    readonly Button[] _tierBtn = new Button[5];

    // ---- Appearance ----
    readonly NumericUpDown _opacity = Num(20, 100);
    readonly ModernSlider _scale = new() { Minimum = 50, Maximum = 200, Step = 5 };
    List<string> _order = OverlayPainter.DefaultOrder.ToList();   // layout from the live preview
    string _imageSide = "Left";
    readonly Button _btnBack = Swatch();
    readonly Button _btnText = Swatch();
    readonly Button _thBackBtn = Swatch();
    readonly Button _thPanelBtn = Swatch();
    readonly Button _thTextBtn = Swatch();
    readonly Button _thAccentBtn = Swatch();

    // ---- About ----
    readonly ModernCheck _debug = new() { Text = "Write a debug log (helps with troubleshooting)" };

    // ------------------------------------------------------------
    //  Theme colors (derived)
    // ------------------------------------------------------------
    Color CBack => Themes.FromArgb(_thBack);
    Color CPanel => Themes.FromArgb(_thPanel);
    Color CText => Themes.FromArgb(_thText);
    Color CAccent => Themes.FromArgb(_thAccent);
    Color CInput => Themes.Blend(CPanel, CText, 0.08);
    Color CBorder => Themes.Blend(CPanel, CText, 0.14);
    Color CMuted => Themes.Blend(CText, CPanel, 0.45);
    Color CardOn(Color under) => Themes.Blend(under, CText, 0.045);

    public SettingsForm(AppSettings current, Action<AppSettings> preview, Action<AppSettings> apply, Func<Task> refreshPrices)
    {
        _preview = preview;
        _apply = apply;
        _refreshPrices = refreshPrices;
        _priceTimer.Tick += (_, _) => { if (!_updating) UpdatePriceStatus(); };
        _priceTimer.Start();
        _savedTimer.Tick += (_, _) => { _savedTimer.Stop(); _savedLbl.Visible = false; };
        StyleTips();

        Text = "Tarkov Appraiser – " + L.T("Settings");
        FormBorderStyle = FormBorderStyle.None;      // no title bar
        ClientSize = new Size(WindowW, WindowH);
        MinimumSize = new Size(WindowW, WindowH);
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = UiFont.Text(9.5f);
        DoubleBuffered = true;

        BuildSidebar();

        var general = AddPage("General", "Hotkey, startup and price data.", "gear");
        var detection = AddPage("Detection", "How Tarkov Appraiser reads the item name.", "scan");
        var overlay = AddPage("Overlay", "Where the price box appears and what it shows.", "overlay");
        var tiers = AddPage("Loot Tiers", "Color prices by value, from junk to legendary.", "gem");
        var appearance = AddPage("Appearance", "Colors of this window.", "drop");
        var history = AddPage("History", "The last items you checked, with current prices.", "history");
        var stats = AddPage("Statistics", "What you checked over all raids.", "chart");
        var faq = AddPage("FAQ", "Answers to common questions.", "help");
        var about = AddPage("About", "Version, troubleshooting and changelog.", "log");

        BuildGeneral(general);
        BuildDetection(detection);
        BuildOverlay(overlay);
        BuildTiers(tiers);
        BuildAppearance(appearance);
        BuildHistory(history);
        BuildStats(stats);
        BuildFaq(faq);
        BuildAbout(about);
        BuildSearch();
        BuildLivePanel();
        _overlayPage = 2;

        // ---------- Header (page title, drag area, window buttons) ----------
        _pageTitle.Font = UiFont.Semi(17f);
        _header.Controls.Add(_pageTitle);
        _header.Controls.Add(_pageSub);

        // window buttons drawn with the Windows icon font (Segoe Fluent Icons / MDL2 Assets)
        string glyphs = UiFont.Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
        bool hasGlyphs = glyphs != "Segoe UI";
        _closeBtn.Font = new Font(glyphs, 9f);
        _closeBtn.Text = hasGlyphs ? "\uE8BB" : "\u2715";
        _maxBtn.Font = new Font(glyphs, 9f);
        _minBtn.Font = new Font(glyphs, 9f);
        _minBtn.Text = hasGlyphs ? "\uE921" : "\u2013";
        _tips.SetToolTip(_minBtn, L.T("Minimize"));
        _tips.SetToolTip(_closeBtn, L.T("Close (Tarkov Appraiser keeps running in the tray)"));
        _minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _header.Controls.Add(_minBtn);
        _closeBtn.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _maxBtn.Click += (_, _) => ToggleMaximize();
        _header.Controls.Add(_maxBtn);
        _header.Controls.Add(_closeBtn);
        // positioned from the real header width (an Anchor would use the not-yet-docked size)
        _header.Resize += (_, _) =>
        {
            _closeBtn.Left = _header.ClientSize.Width - _closeBtn.Width - 12;
            _maxBtn.Left = _closeBtn.Left - _maxBtn.Width - 4;
            _minBtn.Left = _maxBtn.Left - _minBtn.Width - 4;
        };
        UpdateMaxButton();

        // ---------- Footer: every change is applied and saved automatically ----------
        var btnReset = MakeButton("Defaults", 100);
        btnReset.Click += (_, _) =>
        {
            if (Ask("Reset settings", "Reset all settings to their defaults? Your current settings are replaced right away.",
                    0, "Reset", "Cancel") == 0)
            {
                Populate(new AppSettings());
                ScheduleSave();
            }
        };

        // GitHub button at the bottom right: opens the project page
        var github = new Pill { Tag = "chrome", Width = 34, Height = 34, Margin = new Padding(10, 3, 0, 3), IconName = "code" };
        github.Picture = LoadEmbeddedImage("github.png");   // official GitHub mark, when added (see Assets folder)
        _tips.SetToolTip(github, L.T("Tarkov Appraiser on GitHub"));
        github.Click += (_, _) => OpenProjectPage();

        // website button left of the GitHub button
        var website = new Pill { Tag = "chrome", Width = 34, Height = 34, Margin = new Padding(10, 3, 0, 3), IconName = "globe" };
        _tips.SetToolTip(website, L.T("Tarkov Appraiser website"));
        website.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Updates.WebsiteUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Could not open the website: " + ex.Message, true); }
        };

        _bottom.Controls.Add(github);
        _bottom.Controls.Add(website);
        _bottom.Controls.Add(btnReset);
        _bottom.Controls.Add(_savedLbl);

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        // Docking: the last added control is docked first.
        // _main: header (top) and footer (bottom) over the full width, the live preview on the right
        // in between (Overlay page only), the pages fill the rest.
        _main.Controls.Add(_livePanel);
        _main.Controls.Add(_bottom);
        _main.Controls.Add(_header);
        // Form: sidebar (left, full height), everything else next to it
        Controls.Add(_main);
        Controls.Add(_side);

        // Drag the window by the sidebar, the header and the preview panel
        foreach (var area in new Control[] { _side, _header, _livePanel })
        {
            area.MouseDown += DragWindow;
            foreach (Control c in area.Controls)
                if (c is Label or PictureBox) c.MouseDown += DragWindow;
        }

        AmmoStats.Loaded += OnAmmoLoaded;
        ItemInfo.Loaded += OnAmmoLoaded;
        Populate(current);
        SelectPage(0);
        HookLive(this);
        UpdateLive();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _regionFallback = !Themes.RoundCorners(Handle, CBorder);
        UpdateRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
        UpdateMaxButton();
    }

    // borderless window: still minimizable / restorable from the taskbar
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x20000;   // WS_MINIMIZEBOX
            return cp;
        }
    }

    void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }
        // borderless windows would cover the taskbar: limit to the work area of this screen
        // (MaximizedBounds is relative to the screen's own top-left corner)
        var screen = Screen.FromHandle(Handle);
        var wa = screen.WorkingArea;
        MaximizedBounds = new Rectangle(wa.X - screen.Bounds.X, wa.Y - screen.Bounds.Y, wa.Width, wa.Height);
        WindowState = FormWindowState.Maximized;
    }

    void UpdateMaxButton()
    {
        if (WindowState == FormWindowState.Minimized) return;
        bool max = WindowState == FormWindowState.Maximized;
        bool glyphs = _maxBtn.Font.Name != "Segoe UI";
        _maxBtn.Text = glyphs ? (max ? "\uE923" : "\uE922") : (max ? "\u2750" : "\u25A1");
        _tips.SetToolTip(_maxBtn, L.T(max ? "Restore" : "Maximize"));
        _maxBtn.Invalidate();
    }

    static Image? LoadEmbeddedImage(string name)
    {
        try
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var img = Image.FromStream(stream);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }

    void OpenProjectPage()
    {
        var url = Updates.PageUrl;
        if (url == null)
        {
            Ask("GitHub", "The GitHub page is not set up in this build yet.", 0, "OK");
            return;
        }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("Could not open the GitHub page: " + ex.Message, true); }
    }

    // Windows 10 has no native rounded corners for borderless windows: clip the window ourselves
    void UpdateRegion()
    {
        if (!_regionFallback || !IsHandleCreated) return;
        if (WindowState == FormWindowState.Maximized)
        {
            var oldRegion = Region;
            Region = null;
            oldRegion?.Dispose();
            return;
        }
        using var path = Themes.RoundRect(new RectangleF(0, 0, Width, Height), 12);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    // new item facts arrived (any thread): redraw the preview
    void OnAmmoLoaded()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(UpdateLive));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AmmoStats.Loaded -= OnAmmoLoaded;
            History.Changed -= OnHistoryChanged;
            History.Changed -= OnStatsChanged;
            Updates.Found -= OnUpdateFoundHere;
            _searchTimer.Dispose();
            _flashTimer.Dispose();
            ItemInfo.Loaded -= OnAmmoLoaded;
            _logo.Image?.Dispose();
            _aboutLogo.Image?.Dispose();
            _ownIcon?.Dispose();
            _savedTimer.Dispose();
            _saveTimer.Dispose();
            _resTimer.Dispose();
            _priceTimer.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
