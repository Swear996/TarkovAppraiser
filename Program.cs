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

// ============================================================
//  Tarkov Appraiser - prices for your Escape from Tarkov loot at a glance
//  - Hover an item (tooltip appears), then press the hotkey (default: F8)
//  - Screenshot around the cursor -> Windows OCR -> match against tarkov.dev -> overlay
//  - No access to the game process (no memory reading, no injection)
//  - Settings: tray icon -> right click -> Settings (or double click)
// ============================================================

static class Program
{
    public static bool JustUpdated;   // started by the updater: say so in a balloon

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception, fatal: true);

        SettingsStore.Load();   // also sets the program language

        // one instance only; right after an update the old version may still be closing: wait for it
        bool updated = args.Contains("--updated");
        JustUpdated = updated;
        using var mutex = new Mutex(false, "TarkovAppraiser_SingleInstance");
        bool owned;
        try { owned = mutex.WaitOne(updated ? 15000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            UiDialog.Show(null, ThemePalette(), L.T("Already running"),
                L.T("Tarkov Appraiser is already running (tray icon, bottom right)."), 0, "OK");
            return;
        }

        try
        {
            Updates.CleanUp();   // removes the previous version after an update
            // keeps the autostart entry pointing at this .exe (and removes entries of the old program names)
            Autostart.Set(SettingsStore.Current.StartWithWindows);
            Application.Run(new MainForm());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    // colors of the chosen theme for the own message windows
    public static UiDialog.Palette ThemePalette()
    {
        var s = SettingsStore.Current;
        return new UiDialog.Palette(Themes.FromArgb(s.ThemeBackArgb), Themes.FromArgb(s.ThemePanelArgb),
                                    Themes.FromArgb(s.ThemeTextArgb), Themes.FromArgb(s.ThemeAccentArgb));
    }

    static bool _reporting;   // one report window at a time (a broken UI can throw again and again)

    // saved when the program closes because of an error, so the next start can offer the report
    static string CrashPath => Path.Combine(SettingsStore.Dir, "crash.txt");

    // fatal = the program closes right after this (error outside the window thread)
    static void ReportCrash(Exception? ex, bool fatal)
    {
        Log.Write("CRASH: " + ex, true);
        if (fatal)
        {
            // if the window below can't be shown anymore, the next start asks instead
            try { Directory.CreateDirectory(SettingsStore.Dir); File.WriteAllText(CrashPath, ex?.ToString() ?? "unknown error"); }
            catch { }
        }
        if (_reporting) return;
        _reporting = true;
        try
        {
            string text = (fatal ? L.T("Tarkov Appraiser has to close because of an unexpected error.")
                                 : L.T("An unexpected error happened. Tarkov Appraiser keeps running."))
                          + "\n\n" + ex?.Message;
            AskToReport(L.T("Something went wrong"), text, ex?.ToString(), ex);
            if (fatal) DeleteCrashFile();
        }
        finally
        {
            _reporting = false;
        }
    }

    // next start after the program closed because of an error: offer the report once
    public static void OfferLastCrashReport()
    {
        string details;
        try
        {
            if (!File.Exists(CrashPath)) return;
            details = File.ReadAllText(CrashPath);
        }
        catch { return; }
        DeleteCrashFile();
        AskToReport(L.T("Tarkov Appraiser closed unexpectedly"),
            L.T("Last time Tarkov Appraiser closed because of an error. Send a report so it can be fixed?"),
            details, null);
    }

    static void DeleteCrashFile()
    {
        try { File.Delete(CrashPath); } catch { }
    }

    // "Send report" / "Copy details" / "Close" - nothing is sent automatically
    static void AskToReport(string title, string text, string? details, Exception? ex)
    {
        text += "\n\n" + L.T("\"Send report\" copies the details (version, Windows, error, end of the log) and opens the report page on GitHub. Nothing is sent automatically.");
        int choice;
        try
        {
            choice = UiDialog.Show(null, ThemePalette(), title, text, 0, L.T("Send report"), L.T("Copy details"), L.T("Close"));
        }
        catch (Exception dialogError)
        {
            // the own window can't be shown (broken UI): plain Windows message instead
            Log.Write("Crash window: " + dialogError.Message, true);
            choice = MessageBox.Show(text, "Tarkov Appraiser", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes ? 0 : 2;
        }
        if (choice == 0) BugReport.Send(details, ex);
        else if (choice == 1) BugReport.Copy(details);
    }
}
