using System.Net.Http.Json;
using System.Security.Cryptography;

namespace TarkovPriceOverlay;

// ------------------------------------------------------------
//  "12 players online" in the settings window.
//  Every 10 minutes the program sends an anonymous signal to a small counter (Cloudflare Worker):
//  only a random ID that was created once on this PC - no name, no game data. The counter keeps
//  no IP addresses; IDs that were not seen for a day are deleted.
// ------------------------------------------------------------
static class Online
{
    const string Url = "https://tarkov-appraiser-online.nicoruethrich.workers.dev";
    static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    public static int? Count { get; private set; }   // null = not known yet (offline, counter not reachable)
    public static event Action? Changed;

    static System.Threading.Timer? _timer;

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new System.Threading.Timer(_ => _ = PingAsync(), null, TimeSpan.FromSeconds(5), Every);
    }

    static string Id()
    {
        string path = Path.Combine(SettingsStore.Dir, "client-id.txt");
        try
        {
            if (File.Exists(path))
            {
                string id = File.ReadAllText(path).Trim();
                if (id.Length == 32 && id.All(Uri.IsHexDigit)) return id.ToLowerInvariant();
            }
            string fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Directory.CreateDirectory(SettingsStore.Dir);
            File.WriteAllText(path, fresh);
            return fresh;
        }
        catch
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }
    }

    sealed record Answer(int online);

    static async Task PingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await TarkovApi.Http.PostAsJsonAsync(Url + "/ping", new { id = Id() }, cts.Token);
            if (!resp.IsSuccessStatusCode) return;
            var a = await resp.Content.ReadFromJsonAsync<Answer>(cancellationToken: cts.Token);
            if (a == null) return;
            Count = Math.Max(1, a.online);   // at least this PC
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Write("Online counter: " + ex.Message);
        }
    }
}
