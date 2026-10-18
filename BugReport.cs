using System.Diagnostics;
using System.Text;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Problem report with one click: puts the version, Windows version, the error and the end of the
//  log into the clipboard and opens a new GitHub issue (or the log folder when no repository is set).
//  Nothing is sent automatically - the user sees everything and decides.
// ------------------------------------------------------------
static class BugReport
{
    // the full report text (also used for the clipboard)
    public static string Build(Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tarkov Appraiser {Changelog.Current}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit");
        var s = SettingsStore.Current;
        sb.AppendLine($"Language: game {s.ApiLanguage}, OCR {s.OcrLanguage}, program {s.UiLanguage}; mode {s.GameMode}");
        foreach (var screen in Screen.AllScreens)
            sb.AppendLine($"Screen: {screen.Bounds.Width}x{screen.Bounds.Height}{(screen.Primary ? " (primary)" : "")}");
        if (ex != null)
        {
            sb.AppendLine();
            sb.AppendLine("Error:");
            sb.AppendLine(ex.ToString());
        }
        sb.AppendLine();
        sb.AppendLine("Log (last lines):");
        sb.AppendLine(LogTail(80));
        return sb.ToString();
    }

    static string LogTail(int lines)
    {
        try
        {
            if (!File.Exists(SettingsStore.LogPath)) return "(no log)";
            var all = File.ReadAllLines(SettingsStore.LogPath);
            return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (Exception e)
        {
            return "(log not readable: " + e.Message + ")";
        }
    }

    // copies the report and opens the place to report it
    public static void Send(Exception? ex)
    {
        string report = Build(ex);
        CopyToClipboard(report);

        try
        {
            if (Updates.PageUrl is { } page)
            {
                string title = ex != null ? "Crash: " + ex.GetType().Name + " - " + ex.Message : "Problem: ";
                if (title.Length > 120) title = title[..120];
                string body = "**What happened?**\n\n\n**What did you do before?**\n\n\n" +
                              "**Report** (Tarkov Appraiser put it into your clipboard - press Ctrl+V below this line):\n\n";
                string url = page + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                Directory.CreateDirectory(SettingsStore.Dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + SettingsStore.Dir + "\"") { UseShellExecute = true });
            }
        }
        catch (Exception e)
        {
            Log.Write("Problem report: " + e.Message, true);
        }
    }

    // the clipboard needs an STA thread (a crash may happen on any thread)
    static void CopyToClipboard(string text)
    {
        try
        {
            var t = new Thread(() =>
            {
                try { Clipboard.SetText(text); }
                catch (Exception e) { Log.Write("Clipboard: " + e.Message, true); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(3000);
        }
        catch (Exception e)
        {
            Log.Write("Clipboard: " + e.Message, true);
        }
    }
}
