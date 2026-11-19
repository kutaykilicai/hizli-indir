using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MonoTorrent;
using MonoTorrent.Client;

namespace HizliIndir.Core;

public sealed record TorrentOptions(string StateFolder, int Port, long DownloadBytesPerSecond = 0, long UploadBytesPerSecond = 0);

/// <summary>
/// MonoTorrent motorunun ince sarmalayıcısı. Uygulama boyunca tek bir motor çalışır;
/// kaldığı yerden devam bilgisi ve DHT önbelleği StateFolder altında tutulur.
/// Bu sınıf uygulama ayarlarına bağlı değildir, testler doğrudan kurabilir.
/// </summary>
public sealed class TorrentEngine : IDisposable
{
    public ClientEngine Engine { get; }
    public TorrentOptions Options { get; }

    public TorrentEngine(TorrentOptions o)
    {
        Options = o;
        Directory.CreateDirectory(o.StateFolder);
        var b = new EngineSettingsBuilder
        {
            CacheDirectory = o.StateFolder,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            AutoSaveLoadDhtCache = true,
            AllowPortForwarding = false,
            MaximumDownloadRate = Rate(o.DownloadBytesPerSecond),
            MaximumUploadRate = Rate(o.UploadBytesPerSecond),
        };
        b.ListenEndPoints = new() { ["ipv4"] = new IPEndPoint(IPAddress.Any, o.Port) };
        b.DhtEndPoint = new IPEndPoint(IPAddress.Any, o.Port);
        Engine = new ClientEngine(b.ToSettings());
    }

    // MonoTorrent DHT'yi yalnızca router.bittorrent.com ile başlatıyor; o yanıt vermezse DHT hiç
    // düğüm bulamıyor ve yalnızca DHT'de eşi olan magnet bağlantıları dosya bilgisini alamıyor.
    // Bu yüzden motor ilk kez çalışmadan önce bilinen başlangıç düğümleri önbellek dosyasına eklenir.
    public static readonly (string Host, int Port)[] DhtRouters =
    {
        ("dht.transmissionbt.com", 6881), ("dht.libtorrent.org", 25401), ("router.bittorrent.com", 6881), ("router.utorrent.com", 6881),
    };
    const int NodeSize = 26; // 20 bayt kimlik + 4 bayt IPv4 + 2 bayt bağlantı noktası
    readonly object _seedGate = new();
    Task? _dhtSeed;

    Task EnsureDhtSeedAsync() { lock (_seedGate) return _dhtSeed ??= SeedDhtCacheAsync(); }

    async Task SeedDhtCacheAsync()
    {
        try
        {
            var routers = new List<IPEndPoint>();
            foreach (var (host, port) in DhtRouters)
            {
                var lookup = Dns.GetHostAddressesAsync(host);
                if (await Task.WhenAny(lookup, Task.Delay(3000)) != lookup || lookup.IsFaulted) continue;
                routers.AddRange(lookup.Result.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => new IPEndPoint(a, port)));
            }
            var path = Path.Combine(Options.StateFolder, "dht_nodes.cache");
            var old = File.Exists(path) ? await File.ReadAllBytesAsync(path) : Array.Empty<byte>();
            await File.WriteAllBytesAsync(path, MergeDhtCache(old, routers));
        }
        catch { /* DHT önbelleği olmadan da tracker'larla çalışır */ }
    }

    /// <summary>Önbellekteki düğümleri (ham ya da "uzunluk:" önekli) korur, eksik başlangıç düğümlerini ekler.</summary>
    public static byte[] MergeDhtCache(byte[] existing, IEnumerable<IPEndPoint> routers)
    {
        var data = existing.AsSpan();
        int colon = data.IndexOf((byte)':');
        if (colon > 0 && colon < 10 && data[..colon].ToArray().All(b => b is >= (byte)'0' and <= (byte)'9')) data = data[(colon + 1)..];
        if (data.Length % NodeSize != 0) data = Span<byte>.Empty;
        var result = new List<byte>(data.ToArray());
        var have = new HashSet<string>();
        for (int i = 0; i + NodeSize <= data.Length; i += NodeSize) have.Add(Convert.ToHexString(data.Slice(i + 20, 6)));
        foreach (var ep in routers)
        {
            var addr = ep.Address.GetAddressBytes();
            if (addr.Length != 4) continue;
            var tail = addr.Concat(new[] { (byte)(ep.Port >> 8), (byte)ep.Port }).ToArray();
            if (!have.Add(Convert.ToHexString(tail))) continue;
            result.AddRange(RandomNumberGenerator.GetBytes(20));
            result.AddRange(tail);
        }
        return result.ToArray();
    }

    static int Rate(long bps) => (int)Math.Clamp(bps, 0, int.MaxValue);

    public Task SetLimitsAsync(long downBps, long upBps)
    {
        var b = new EngineSettingsBuilder(Engine.Settings) { MaximumDownloadRate = Rate(downBps), MaximumUploadRate = Rate(upBps) };
        return Engine.UpdateSettingsAsync(b.ToSettings());
    }

    /// <summary>Magnet bağlantısından .torrent içeriğini (dosya listesi) eşlerden çeker.</summary>
    public async Task<TorrentMeta> FetchMetadataAsync(string magnet, TimeSpan timeout, CancellationToken ct)
    {
        if (!MagnetLink.TryParse(magnet.Trim(), out var link) || link == null) throw new InvalidDataException("Magnet bağlantısı geçersiz");
        await EnsureDhtSeedAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var data = await Engine.DownloadMetadataAsync(link, cts.Token);
            return TorrentMeta.Parse(data.ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Dosya bilgisi alınamadı: bu bağlantı için şu an eş bulunamadı");
        }
    }

    public TorrentManager? Find(string infoHash) =>
        Engine.Torrents.FirstOrDefault(m => string.Equals(m.InfoHashes.V1OrV2.ToHex(), infoHash, StringComparison.OrdinalIgnoreCase));

    /// <summary>Torrenti motora ekler (zaten varsa olanı döner) ve yalnızca seçili dosyaları indirilecek işaretler.</summary>
    public async Task<TorrentManager> AddAsync(TorrentMeta meta, string saveFolder, IReadOnlyCollection<int>? selected)
    {
        await EnsureDhtSeedAsync();
        var m = Find(meta.InfoHash);
        if (m == null)
        {
            Directory.CreateDirectory(saveFolder);
            var ts = new TorrentSettingsBuilder { CreateContainingDirectory = true }.ToSettings();
            m = await Engine.AddAsync(meta.Torrent, saveFolder, ts);
        }
        for (int i = 0; i < m.Files.Count; i++)
        {
            var want = selected == null || selected.Contains(i) ? Priority.Normal : Priority.DoNotDownload;
            if (m.Files[i].Priority != want) await m.SetFilePriorityAsync(m.Files[i], want);
        }
        return m;
    }

    public async Task RemoveAsync(string infoHash, bool deleteData)
    {
        var m = Find(infoHash);
        if (m == null) return;
        if (m.State != TorrentState.Stopped) await m.StopAsync();
        await Engine.RemoveAsync(m, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
    }

    /// <summary>Seçili dosyaların toplam boyutu ve inen kısmı (bayt).</summary>
    public static (long total, long done) Progress(TorrentManager m)
    {
        long total = 0, done = 0;
        int pieceLen = m.Torrent?.PieceLength ?? 0;
        foreach (var f in m.Files)
        {
            if (f.Priority == Priority.DoNotDownload) continue;
            total += f.Length;
            if (f.BitField.Length == 0) continue;
            if (f.BitField.AllTrue) { done += f.Length; continue; }
            // Parça sayısından yaklaşık değer; dosyanın ilk ve son parçası paylaşımlı olabilir.
            done += Math.Min(f.Length, (long)f.BitField.TrueCount * pieceLen);
        }
        return (total, done);
    }

    public Task StopAllAsync(TimeSpan timeout) => Engine.StopAllAsync(timeout);

    public void Dispose() => Engine.Dispose();
}
