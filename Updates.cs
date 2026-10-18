using System.Text.Json;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  Update check: asks GitHub for the newest release of the repository below
//  and compares its tag ("v0.6" or "0.6") with Changelog.Current.
//  Leave Repo empty to switch the check off completely.
// ------------------------------------------------------------
static class Updates
{
    // GitHub repository that holds the releases, "owner/name"
    public const string Repo = "Swear996/TarkovAppraiser";

    public static bool Configured => Repo.Length > 0;

    // project page (GitHub button in the settings window), null while no repository is set
    public static string? PageUrl => Configured ? "https://github.com/" + Repo : null;

    // DownloadUrl: the TarkovAppraiser.exe attached to the release (null = only the release page)
    public sealed record Release(string Tag, string Url, string? DownloadUrl = null)
    {
        public string Version => Tag.TrimStart('v', 'V');   // "v1.5" -> "1.5" (shown to the user)
    }

    // the newer version found by the last check (null = none); Found tells the windows about it,
    // InstallRequested is answered by the main window (download, swap, restart)
    public static Release? Available { get; private set; }
    public static event Action? Found;
    public static Action? InstallRequested;

    public static void Announce(Release release)
    {
        if (Available?.Tag == release.Tag) return;
        Available = release;
        Found?.Invoke();
    }

    // Never throws; null = no newer version (or the check was not possible)
    public static async Task<Release?> CheckAsync()
    {
        if (!Configured) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var resp = await TarkovApi.Http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Write($"Update check: HTTP {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (url.Length == 0) url = $"https://github.com/{Repo}/releases/latest";
            // TarkovAppraiser.exe (the small build, needs .NET 8);
            // older releases also had TarkovAppraiser-small.exe - taken when there is no TarkovAppraiser.exe
            string? download = null, small = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!a.TryGetProperty("browser_download_url", out var d)) continue;
                    if (name.Equals("TarkovAppraiser.exe", StringComparison.OrdinalIgnoreCase)) download = d.GetString();
                    else if (name.Equals("TarkovAppraiser-small.exe", StringComparison.OrdinalIgnoreCase)) small = d.GetString();
                }
            download ??= small;

            if (!TryParse(tag, out var latest) || !TryParse(Changelog.Current, out var current))
            {
                Log.Write($"Update check: cannot compare versions '{tag}' and '{Changelog.Current}'");
                return null;
            }
            Log.Write($"Update check: newest {tag}, running {Changelog.Current}");
            return latest > current ? new Release(tag, url, download) : null;
        }
        catch (Exception ex)
        {
            Log.Write("Update check failed: " + ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------
    //  Install: download the new exe next to the running one, swap the files and start the new version.
    //  Windows allows renaming a running .exe, so: TarkovAppraiser.exe -> TarkovAppraiser.old.exe (deleted by the new
    //  version at startup), downloaded file -> TarkovAppraiser.exe. Returns null on success, otherwise the error.
    // ------------------------------------------------------------
    public static async Task<string?> InstallAsync(string downloadUrl)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return "Unknown program path";
        string dir = Path.GetDirectoryName(exe)!;
        string fresh = Path.Combine(dir, "TarkovAppraiser.new.exe");
        string old = Path.Combine(dir, "TarkovAppraiser.old.exe");
        try
        {
            using (var resp = await TarkovApi.Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(fresh);
                await src.CopyToAsync(dst);
            }
            if (new FileInfo(fresh).Length < 200_000) throw new Exception("The download is incomplete");

            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(fresh, exe);
            }
            catch
            {
                File.Move(old, exe);   // put the running version back
                throw;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
            Log.Write("Update installed, restarting", true);
            return null;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(fresh)) File.Delete(fresh); } catch { /* leftover is harmless */ }
            return ex.Message;
        }
    }

    // after an update: remove the previous version
    public static void CleanUp()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            string old = Path.Combine(Path.GetDirectoryName(exe)!, "TarkovAppraiser.old.exe");
            if (File.Exists(old)) File.Delete(old);
        }
        catch
        {
            // still locked: next start
        }
    }

    // "v0.6" / "0.6" / "0.6.1" -> Version (a single number like "1" becomes 1.0)
    static bool TryParse(string s, out Version version)
    {
        s = s.Trim().TrimStart('v', 'V');
        if (!s.Contains('.')) s += ".0";
        if (Version.TryParse(s, out var v))
        {
            version = v;
            return true;
        }
        version = new Version(0, 0);
        return false;
    }
}
