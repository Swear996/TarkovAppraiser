using System.Drawing.Text;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Central place for the UI fonts (Fluent design).
//  "Segoe UI Variable" on Windows 11, falls back to "Segoe UI" on Windows 10.
// ------------------------------------------------------------
static class UiFont
{
    static readonly HashSet<string> Installed = LoadInstalled();

    static HashSet<string> LoadInstalled()
    {
        try
        {
            using var fonts = new InstalledFontCollection();
            return new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    public static string Pick(params string[] names) =>
        names.FirstOrDefault(Installed.Contains) ?? "Segoe UI";

    // body text and headings
    public static readonly string Body = Pick("Segoe UI Variable Text", "Segoe UI");
    public static readonly string Head = Pick("Segoe UI Variable Display Semib", "Segoe UI Semibold");

    // Normal text
    public static Font Text(float size, FontStyle style = FontStyle.Regular) => new(Body, size, style);

    // Headings / emphasized text. Static semibold faces render crisply in GDI;
    // without one, Segoe UI Bold is used.
    public static Font Semi(float size) =>
        Installed.Contains(Head) ? new Font(Head, size, FontStyle.Regular) : new Font("Segoe UI", size, FontStyle.Bold);

    // Bold numbers in the overlay
    public static Font Strong(float size) =>
        Installed.Contains("Segoe UI Semibold") ? new Font("Segoe UI Semibold", size, FontStyle.Regular)
                                                : new Font("Segoe UI", size, FontStyle.Bold);
}
