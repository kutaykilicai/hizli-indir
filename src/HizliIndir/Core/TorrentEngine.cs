using System.Net;
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
