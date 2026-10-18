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
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

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
            MessageBox.Show(L.T("Tarkov Appraiser is already running (tray icon, bottom right)."),
                "Tarkov Appraiser", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    static bool _reporting;   // one report window at a time (a broken UI can throw again and again)

    static void ReportCrash(Exception? ex)
    {
        Log.Write("CRASH: " + ex, true);
        if (_reporting) return;
        _reporting = true;
        try
        {
            var answer = MessageBox.Show(
                L.T("Unexpected error:") + "\n" + ex?.Message + "\n\n" +
                L.T("Report this problem? Tarkov Appraiser copies the details to the clipboard and opens the report page - nothing is sent automatically."),
                "Tarkov Appraiser", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (answer == DialogResult.Yes) BugReport.Send(ex);
        }
        finally
        {
            _reporting = false;
        }
    }
}
