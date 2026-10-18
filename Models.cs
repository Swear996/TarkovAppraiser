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

record TraderPrice(string Vendor, long Price);

// BasePrice: the game's fixed base value (needed for the flea market fee)
// ImageUrl: picture address from the price list (no extra query needed)
// NoFlea: the item cannot be sold on the flea market
// StackMax: how many fit into one cell (ammo, money > 1)
record Item(
    string Name, string Short, string NormName, string NormShort,
    int Slots, long Avg24h, long LastLow, List<TraderPrice> Traders, string Id = "",
    long BasePrice = 0, string? ImageUrl = null, bool NoFlea = false, int StackMax = 1);

// Status of the price list (shown in the settings window and in the tray menu)
static class PriceState
{
    public static DateTime? Updated;     // local time of the data (download time, or cache file time)
    public static bool FromCache;        // true = tarkov.dev was unreachable, saved prices are used
    public static int Count;
    public static bool Loading;
    public static string? Error;         // last error, null after a successful load

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return L.T("just now");
        if (d.TotalMinutes < 60) return string.Format(L.T("{0} min ago"), (int)d.TotalMinutes);
        if (d.TotalHours < 24) return string.Format(L.T("{0} h ago"), (int)d.TotalHours);
        return string.Format(L.T("{0} d ago"), (int)d.TotalDays);
    }

    public static string Short() =>
        Updated == null ? L.T("Prices: not loaded yet")
        : L.T("Prices:") + $" {Updated.Value:t} ({Ago(Updated.Value)})" + (FromCache ? " - " + L.T("saved") : "");

    // Two short lines for the sidebar of the settings window
    public static string Compact()
    {
        if (Updated == null)
            return L.T(Loading ? "Loading prices ..." : Error != null ? "Could not load prices" : "Not loaded yet");
        string line1 = L.T(FromCache ? "Saved list," : "Updated") + " " + Ago(Updated.Value);
        string line2 = Loading ? L.T("Updating ...") : string.Format(L.T("{0} items"), Count.ToString("N0"));
        return line1 + "\r\n" + line2;
    }

    // State for the status dot: 2 = fresh, 1 = saved prices / last update failed, 0 = none
    public static int Health() =>
        Updated == null ? 0 : FromCache || Error != null ? 1 : 2;

    public static string Text()
    {
        if (Updated == null)
            return Loading ? L.T("Loading prices ...")
                 : Error != null ? L.T("Prices could not be loaded yet.") + "\r\n" + L.T("Last error:") + " " + Error
                 : L.T("Prices have not been loaded yet.");

        string line1 = (FromCache
                           ? L.T(Loading ? "Saved prices from" : "tarkov.dev unreachable - using saved prices from") + " "
                           : L.T("Last updated:") + " ")
                       + Updated.Value.ToString("g") + "  (" + Ago(Updated.Value) + ")";
        string line2 = Loading ? L.T("Updating ...")
                     : string.Format(L.T("{0} items"), Count.ToString("N0")) + (Error != null && !FromCache ? "  -  " + L.T("last update failed") : "");
        return line1 + "\r\n" + line2;
    }
}

// Center (X, Y) and bounding box of one recognized text line, in screenshot pixels
record OcrLineInfo(string Text, double X, double Y, double Left, double Top, double Right, double Bottom);
