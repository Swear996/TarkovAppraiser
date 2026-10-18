namespace TarkovPriceOverlay;

// Statistics page: checked value over all raids, most valuable finds, most checked items
// (part of SettingsForm, see SettingsForm.cs; the numbers come from Stats in History.cs)
public sealed partial class SettingsForm
{
    readonly Txt[] _statSummary = new Txt[3];
    readonly (Txt Name, Txt Value)[] _statTop = new (Txt, Txt)[Stats.TopCount];
    readonly (Txt Name, Txt Value)[] _statMost = new (Txt, Txt)[Stats.TopCount];

    void BuildStats(PagePanel p)
    {
        var c = NewCard(p, "All raids");
        for (int i = 0; i < _statSummary.Length; i++)
        {
            StartRow(c);
            _statSummary[i] = Lbl("", CardPad, c.NextY + 11, CardW - 2 * CardPad, "label", 10f);
            c.Controls.Add(Stretch(_statSummary[i]));
            c.NextY += 40;
        }
        Done(p, c);

        c = NewCard(p, "Most valuable finds");
        StatRows(c, _statTop);
        Done(p, c);

        c = NewCard(p, "Most checked items");
        StatRows(c, _statMost);
        Done(p, c);

        c = NewCard(p, null);
        Note(c, "Values as they were when you checked the item. A raid counts when you reset the raid total (History page or in-game list).", 32);
        var reset = MakeButton("Reset statistics", 160);
        reset.Click += (_, _) =>
        {
            if (Ask("Reset statistics", "Start the statistics from zero? This can't be undone.", 0, "Reset", "Cancel") == 0)
                Stats.Reset();
        };
        Buttons(c, reset);
        Done(p, c);

        History.Changed += OnStatsChanged;
        UpdateStats();
    }

    static void StatRows(Card c, (Txt Name, Txt Value)[] rows)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            StartRow(c);
            var name = Lbl("", CardPad, c.NextY + 10, CardW - 2 * CardPad - 160, "label", 9.5f);
            var value = Lbl("", CardW - CardPad - 150, c.NextY + 10, 150, "label", 9.5f, semi: true);
            c.Controls.Add(Stretch(name));
            c.Controls.Add(AtRight(value));
            rows[i] = (name, value);
            c.NextY += 36;
        }
    }

    void OnStatsChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnStatsChanged)); return; }
        UpdateStats();
    }

    void UpdateStats()
    {
        var d = Stats.Data;
        _statSummary[0].Text = string.Format(L.T("{0} items checked since {1}"), d.Checks.ToString("N0"), d.Since.ToString("d"));
        _statSummary[1].Text = string.Format(L.T("Value checked: {0}"), TextBuilder.Money(d.CheckedValue));
        _statSummary[2].Text = d.Raids == 0
            ? L.T("Raids: none yet")
            : string.Format(L.T("Raids: {0}  ·  best {1}  ·  average {2}"), d.Raids.ToString("N0"),
                            TextBuilder.Money(d.BestRaid), TextBuilder.Money(d.RaidsValue / d.Raids));

        static string NameOf(string id) => MainForm.ById(id) is Item i ? TextBuilder.DisplayName(i) : "?";

        for (int i = 0; i < _statTop.Length; i++)
        {
            bool has = i < d.Top.Count;
            _statTop[i].Name.Text = has ? $"{i + 1}.  {NameOf(d.Top[i].Id)}" : (i == 0 ? L.T("Nothing checked yet") : "");
            _statTop[i].Value.Text = has ? TextBuilder.Money(d.Top[i].Value) : "";
        }
        var most = Stats.MostChecked();
        for (int i = 0; i < _statMost.Length; i++)
        {
            bool has = i < most.Count;
            _statMost[i].Name.Text = has ? $"{i + 1}.  {NameOf(most[i].Id)}" : (i == 0 ? L.T("Nothing checked yet") : "");
            _statMost[i].Value.Text = has ? string.Format(L.T("{0}×"), most[i].Count.ToString("N0")) : "";
        }
    }
}
