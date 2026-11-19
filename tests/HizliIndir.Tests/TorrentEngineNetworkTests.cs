using HizliIndir.Core;
using MonoTorrent.Client;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Gerçek ağda kısa motor denemesi: yasal bir Ubuntu kalıbının torrenti ve magnet bağlantısı.
/// Tam indirme yapmaz; birkaç bayt indiği görülünce durur. HIZLI_AG_TESTI=1 ile çalışır.
/// </summary>
public class TorrentEngineNetworkTests
{
    const string DefaultUrl = "https://releases.ubuntu.com/24.04/ubuntu-24.04.5-live-server-amd64.iso.torrent";
    readonly ITestOutputHelper _out;
    public TorrentEngineNetworkTests(ITestOutputHelper o) { _out = o; }

    static bool Enabled => Environment.GetEnvironmentVariable("HIZLI_AG_TESTI") == "1";
    static string Url => Environment.GetEnvironmentVariable("HIZLI_AG_TORRENT") ?? DefaultUrl;

    static string Temp() { var d = Path.Combine(Path.GetTempPath(), "hi-torrent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [SkippableFact]
    public async Task TorrentDosyasiylaIndirmeBaslarVeDurur()
    {
        Skip.IfNot(Enabled, "Ağ testi kapalı (HIZLI_AG_TESTI=1)");
        using var http = new HttpClient();
        var meta = TorrentMeta.Parse(await http.GetByteArrayAsync(Url));
        var dir = Temp();
        using var engine = new TorrentEngine(new TorrentOptions(Path.Combine(dir, "durum"), 0));
        var m = await engine.AddAsync(meta, Path.Combine(dir, "indir"), null);
        await m.StartAsync();
        var until = DateTime.UtcNow.AddSeconds(150);
        long done = 0;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(1000);
            done = m.Monitor.DataBytesReceived;
            if (done > 256 * 1024) break;
        }
        _out.WriteLine($"{meta.Name}: {done} bayt, {m.OpenConnections} bağlantı, durum {m.State}");
        await m.StopAsync();
        Assert.Equal(TorrentState.Stopped, m.State);
        Assert.True(done > 0, "Hiç veri inmedi");
        await engine.RemoveAsync(meta.InfoHash, deleteData: true);
        Assert.Null(engine.Find(meta.InfoHash));
    }

    [SkippableFact]
    public async Task MagnettenDosyaBilgisiAlinir()
    {
        Skip.IfNot(Enabled, "Ağ testi kapalı (HIZLI_AG_TESTI=1)");
        using var http = new HttpClient();
        var meta = TorrentMeta.Parse(await http.GetByteArrayAsync(Url));
        var magnet = $"magnet:?xt=urn:btih:{meta.InfoHash}&dn={Uri.EscapeDataString(meta.Name)}" +
            string.Concat(meta.Torrent.AnnounceUrls.SelectMany(t => t).Select(t => "&tr=" + Uri.EscapeDataString(t)));
        var dir = Temp();
        using var engine = new TorrentEngine(new TorrentOptions(Path.Combine(dir, "durum"), 0));
        var got = await engine.FetchMetadataAsync(magnet, TimeSpan.FromSeconds(150), CancellationToken.None);
        _out.WriteLine($"magnet: {got.Name}, {got.Torrent.Files.Count} dosya");
        Assert.Equal(meta.InfoHash, got.InfoHash);
        Assert.Equal(meta.Name, got.Name);
    }
}
