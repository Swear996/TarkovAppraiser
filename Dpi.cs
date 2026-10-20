using System.Runtime.InteropServices;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  The windows of the program (settings, welcome, questions) are laid out in fixed pixels. On a screen with
//  Windows scaling above 100 % (or a higher resolution through NVIDIA DSR / AMD VSR) they would look tiny.
//  So these windows are created "DPI unaware": Windows itself enlarges them to the chosen scaling
//  (Settings > Display > Scale). Everything that works with exact screen pixels - screen capture, the
//  price box, the in-game list - stays per-monitor aware as before.
// ------------------------------------------------------------
static class Dpi
{
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    static readonly IntPtr UnawareGdiScaled = new(-5);   // Windows 10 1809+: text drawn with GDI stays sharp
    static readonly IntPtr Unaware = new(-1);

    sealed class Scope(IntPtr previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous != IntPtr.Zero)
                try { SetThreadDpiAwarenessContext(previous); } catch { }
        }
    }

    // while the returned scope is open, new windows of this thread are scaled by Windows
    public static IDisposable ScaledByWindows()
    {
        try
        {
            var previous = SetThreadDpiAwarenessContext(UnawareGdiScaled);
            if (previous == IntPtr.Zero) previous = SetThreadDpiAwarenessContext(Unaware);
            return new Scope(previous);
        }
        catch
        {
            return new Scope(IntPtr.Zero);   // very old Windows: windows stay as they are
        }
    }

    // exact screen pixels again, e.g. when a button in a scaled window shows the price box at the mouse
    // (Windows hands out scaled mouse and screen coordinates while such a window handles a click)
    public static IDisposable Exact()
    {
        try { return new Scope(SetThreadDpiAwarenessContext(new IntPtr(-4))); }   // per monitor v2
        catch { return new Scope(IntPtr.Zero); }
    }

    // pop-up windows (menus) of a control: created like the window they belong to
    public static IDisposable Like(Control c)
    {
        try
        {
            var form = c.FindForm();
            if (form == null || !form.IsHandleCreated) return new Scope(IntPtr.Zero);
            var ctx = GetWindowDpiAwarenessContext(form.Handle);
            return new Scope(ctx == IntPtr.Zero ? IntPtr.Zero : SetThreadDpiAwarenessContext(ctx));
        }
        catch
        {
            return new Scope(IntPtr.Zero);
        }
    }
}
