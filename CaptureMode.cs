using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Whether the windows of Tarkov Appraiser (price box, in-game list, notices) appear in screenshots
//  and recordings (Settings > Overlay > "Show in screenshots and recordings").
//  Hidden from capture they can never disturb the item recognition; visible, they are hidden
//  for a moment while Tarkov Appraiser takes its own screenshot.
// ------------------------------------------------------------
static class CaptureMode
{
    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;   // Windows 10 2004+

    // returns true when the window is now excluded from screen capture
    public static bool Apply(IntPtr handle, bool showInScreenshots)
    {
        try
        {
            bool ok = SetWindowDisplayAffinity(handle, showInScreenshots ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE);
            return ok && !showInScreenshots;
        }
        catch
        {
            return false;
        }
    }
}
