namespace TarkovPriceOverlay;

// History page and the search field of the sidebar (part of SettingsForm, see SettingsForm.cs)
public sealed partial class SettingsForm
{
    public const int HistoryPage = 5;   // index of the History page (tray menu "Show all …")

    // tray menu while this window is open: change the switch here, it is saved like any other change
    public bool ToggleAutoHover()
    {
        _autoHover.Checked = !_autoHover.Checked;
        return _autoHover.Checked;
    }

    public void ToggleHistoryWindow() => _histWin.Checked = !_histWin.Checked;

    // a hotkey field has the focus: the app releases its hotkeys so they can be pressed here
    public event Action<bool>? HotkeyRecording;
    bool _recordingKeys;

    void HookHotkeyRecording()
    {
        foreach (var box in new[] { _hotkey, _pauseHotkey, _stackHotkey, _histHotkey })
        {
            box.Enter += (_, _) => SetRecording(true);
            box.Leave += (_, _) => SetRecording(false);
        }
        Deactivate += (_, _) => SetRecording(false);   // back in the game: the hotkeys work again
        Activated += (_, _) =>
        {
            if (ActiveControl is TextBox t && (t == _hotkey || t == _pauseHotkey || t == _stackHotkey || t == _histHotkey))
                SetRecording(true);
        };
    }

    void SetRecording(bool on)
    {
        if (on == _recordingKeys) return;
        _recordingKeys = on;
        HotkeyRecording?.Invoke(on);
    }

    // opens the window on another page than General
    public void ShowPage(int index)
    {
        if (index >= 0 && index < _pages.Count) SelectPage(index);
    }

    // ------------------------------------------------------------
    //  History: the last checked items
    // ------------------------------------------------------------
    readonly HistoryList _history = new();
    Card? _historyCard;

    void BuildHistory(PagePanel p)
    {
        // first: the in-game list switch (the card below grows with the list)
        var g = NewCard(p, "In game");
        Switch(g, _histWin, "A small list on top of the game - drag it at the title, resize it at the edges. Hotkey: see General.");
        Switch(g, _histOnlyGame, "Hidden on the desktop and in other programs, back as soon as you are in the game");
        Row(g, "Opacity (%)", _histOpacity, 84, "Of the list only - the price box keeps its own");
        Done(p, g);

        // value of the current raid (the same as in the footer of the in-game list)
        var raid = NewCard(p, "This raid");
        StartRow(raid);
        _raidInfo.Font = UiFont.Semi(10.5f);
        _raidInfo.SetBounds(CardPad, raid.NextY + 12, CardW - 2 * CardPad - 140, 22);
        raid.Controls.Add(Stretch(_raidInfo));
        var reset = MakeButton("Reset", 120);
        reset.SetBounds(CardW - CardPad - 120, raid.NextY + 6, 120, 32);
        reset.Click += (_, _) => History.ResetRaid();
        raid.Controls.Add(AtRight(reset));
        raid.NextY += RowH;
        Note(raid, "Every item you check is counted once. Reset it when a new raid starts.", 18);
        Done(p, raid);
        UpdateRaidInfo();

        var c = NewCard(p, "Last checked items");
        _historyCard = c;
        var clear = MakeButton("Clear", 90);
        clear.Height = 30;
        clear.SetBounds(CardW - CardPad - 90, 8, 90, 30);
        clear.Click += (_, _) => History.Clear();
        c.Controls.Add(AtRight(clear));
        clear.BringToFront();   // the card title spans the whole width and would cover the button
        _historyClear = clear;

        _history.SetBounds(0, c.NextY, CardW, _history.Height);
        c.Controls.Add(Stretch(_history));
        _historyNote.Text = L.T("Prices are always the current ones. The list keeps the last 20 items, also after a restart.");
        _historyNote.Font = UiFont.Text(8.5f);
        _historyNote.SetBounds(CardPad, 0, CardW - 2 * CardPad, 18);
        c.Controls.Add(Stretch(_historyNote));
        FitHistoryCard();
        p.Y += c.Height + 14;

        _history.SizeChanged += (_, _) => FitHistoryCard();
        History.Changed += OnHistoryChanged;
    }

    Button? _historyClear;
    readonly Txt _historyNote = new() { UseMnemonic = false, Tag = "hint" };

    // the card grows with the list
    void FitHistoryCard()
    {
        if (_historyCard == null) return;
        if (_historyClear != null) _historyClear.Visible = _history.Count > 0;   // nothing to clear
        _historyNote.Top = _history.Bottom + 6;
        _historyCard.Height = _historyNote.Bottom + 12;
    }

    void OnHistoryChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnHistoryChanged)); return; }
        _history.Reload();
        FitHistoryCard();
        UpdateRaidInfo();
    }

    void UpdateRaidInfo() =>
        _raidInfo.Text = TextBuilder.Money(History.RaidTotal) + "   \u00B7   " +
                         string.Format(L.T("{0} items"), History.RaidCount.ToString("N0"));

    // ------------------------------------------------------------
    //  Search: type a word, the matching setting is shown and highlighted
    // ------------------------------------------------------------
    readonly TextBox _search = new() { BorderStyle = BorderStyle.None };
    readonly Host _searchHost = new() { Tag = "search" };
    readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 1600 };
    List<(int Page, Card Card)> _hits = new();
    int _hit;
    Card? _flashed;

    void BuildSearch()
    {
        HookHotkeyRecording();
        _search.PlaceholderText = L.T("Search settings");
        _searchHost.SetBounds(12, 76, SideW - 24, 32);
        _searchHost.Controls.Add(_search);
        _side.Controls.Add(_searchHost);
        _searchHost.BringToFront();

        _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;   // no beep
            if (_searchTimer.Enabled) { _searchTimer.Stop(); RunSearch(); }
            else if (_hits.Count > 1) ShowHit(_hit + 1);   // Enter again = next match
        };
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); Unflash(); };
    }

    void RunSearch()
    {
        string q = _search.Text.Trim();
        _hits = new();
        if (q.Length >= 2)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                string pageText = _pageInfo[i].Title + " " + _pageInfo[i].Sub;
                foreach (var card in _pages[i].Body.Controls.OfType<Card>().OrderBy(c => c.Top))
                {
                    if (Contains(TextOf(card), q) || Contains(pageText, q))
                        _hits.Add((i, card));
                }
            }
            // a page title match alone should not list every card of the page: prefer cards that contain the word
            var direct = _hits.Where(h => Contains(TextOf(h.Card), q)).ToList();
            if (direct.Count > 0) _hits = direct;
        }
        _searchHost.Border = q.Length >= 2 && _hits.Count == 0 ? Color.FromArgb(229, 72, 77) : CBorder;
        _searchHost.Invalidate();
        if (_hits.Count > 0) ShowHit(0);
    }

    static bool Contains(string text, string q) => text.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    static string TextOf(Control root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Control c)
        {
            foreach (Control ch in c.Controls)
            {
                if (ch is not TextBox && ch is not ComboBox && ch is not NumericUpDown && !string.IsNullOrEmpty(ch.Text))
                    sb.Append(ch.Text).Append(' ');
                if (ch is ComboBox cb)
                    foreach (var o in cb.Items) sb.Append(o).Append(' ');
                Walk(ch);
            }
        }
        Walk(root);
        return sb.ToString();
    }

    void ShowHit(int index)
    {
        if (_hits.Count == 0) return;
        _hit = (index % _hits.Count + _hits.Count) % _hits.Count;
        var (page, card) = _hits[_hit];
        if (!_pages[page].Visible) SelectPage(page);
        _pages[page].Reveal(card);
        Unflash();
        _flashed = card;
        card.Border = CAccent;
        card.Invalidate();
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    void Unflash()
    {
        if (_flashed == null) return;
        var c = _flashed;
        _flashed = null;
        if (!c.IsDisposed) ThemeOne(c);
    }
}
