using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Which resolution the game is drawn in. The recognition was tuned on a 768 px high screen;
//  everything that depends on the size of the tooltip is scaled by (height / 768).
//  Order: manual setting (Detection > Resolution) -> Tarkov's game window (also when the game
//  runs in a window or a lower resolution) -> the screen under the mouse.
// ------------------------------------------------------------
static class Resolution
{
    public const int TunedHeight = 768;

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    public sealed record Info(Size Screen, Size? Game, int UsedHeight, string Source)
    {
        public float Factor => Math.Clamp(UsedHeight / (float)TunedHeight, 0.8f, 3f);
    }

    static Size? _game;
    static DateTime _gameChecked = DateTime.MinValue;

    // size of the game's drawing area (null = game not running / minimized); checked at most every 3 s
    public static Size? GameSize()
    {
        if (DateTime.UtcNow - _gameChecked < TimeSpan.FromSeconds(3)) return _game;
        _gameChecked = DateTime.UtcNow;
        _game = null;
        try
        {
            foreach (var p in Process.GetProcessesByName("EscapeFromTarkov"))
            {
                using (p)
                {
                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero || IsIconic(h) || !GetClientRect(h, out var r)) continue;
                    int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                    if (w >= 640 && hh >= 480) { _game = new Size(w, hh); break; }
                }
            }
        }
        catch
        {
            // no access to the process list: use the screen
        }
        return _game;
    }

    public static Info Detect(Point at, AppSettings s)
    {
        var screen = Screen.FromPoint(at).Bounds.Size;
        var game = GameSize();
        if (s.ResolutionHeight > 0) return new Info(screen, game, s.ResolutionHeight, "manual");
        if (game is Size g) return new Info(screen, game, g.Height, "game");
        return new Info(screen, game, screen.Height, "screen");
    }
}
