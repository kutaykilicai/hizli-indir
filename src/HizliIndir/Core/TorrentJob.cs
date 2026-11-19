using MonoTorrent.Client;

namespace HizliIndir.Core;

/// <summary>Bir torrent indirmesini yürütür. Ayrıntılı ilerlemeyi ilerleme penceresi Snapshot ile okur.</summary>
public sealed class TorrentJob
{
    static TorrentEngine? _shared;
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static string StateFolder => Path.Combine(Paths.Data, "torrent");
    public static string FileFor(string infoHash) => Path.Combine(StateFolder, infoHash.ToLowerInvariant() + ".torrent");

    public static async Task<TorrentEngine> SharedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return _shared ??= new TorrentEngine(new TorrentOptions(Path.Combine(StateFolder, "motor"), App.Settings.TorrentPort,
                App.Settings.SpeedLimitEnabled ? App.Settings.SpeedLimitKBps * 1024L : 0));
        }
        finally { Gate.Release(); }
    }

    public static void ApplySpeedLimit()
    {
        var e = _shared;
        if (e == null) return;
        _ = e.SetLimitsAsync(App.Settings.SpeedLimitEnabled ? App.Settings.SpeedLimitKBps * 1024L : 0, 0);
    }

    /// <summary>Uygulama kapanırken kaldığı yerden devam bilgisini yazmak için motoru durdurur.</summary>
    public static void Shutdown()
    {
        var e = _shared;
        if (e == null) return;
        try { e.StopAllAsync(TimeSpan.FromSeconds(3)).Wait(TimeSpan.FromSeconds(4)); } catch { }
    }

    /// <summary>Listeden silinen torrenti motordan çıkarır; istenirse inen dosyaları da siler.</summary>
    public static async Task ForgetAsync(DownloadItem item, bool deleteData)
    {
        if (string.IsNullOrEmpty(item.InfoHash)) return;
        try
        {
            var e = await SharedAsync();
            if (e.Find(item.InfoHash) == null && deleteData && File.Exists(item.TorrentPath))
                await e.AddAsync(TorrentMeta.Load(item.TorrentPath!), item.Folder, item.TorrentFiles);
            await e.RemoveAsync(item.InfoHash, deleteData);
        }
        catch (Exception ex) { Log.Write("Torrent kaldırılamadı: " + ex.Message); }
    }

    readonly DownloadItem _item;
    TorrentManager? _m;

    public TorrentJob(DownloadItem item) { _item = item; }

    public int Seeds => _m?.Peers.Seeds ?? 0;
    public int Leechs => _m?.Peers.Leechs ?? 0;

    /// <summary>Parça durumunu ardışık bloklar olarak verir (SegmentBar için, birim: parça).</summary>
    public (SegmentState[] runs, int pieces) Pieces()
    {
        var bf = _m?.Bitfield;
        if (bf == null || bf.Length == 0) return (Array.Empty<SegmentState>(), 0);
        var runs = new List<SegmentState>();
        int i = 0;
        while (i < bf.Length)
        {
            if (!bf[i]) { i++; continue; }
            int s = i;
            while (i < bf.Length && bf[i]) i++;
            runs.Add(new SegmentState { Start = s, End = i - 1, Done = i });
        }
        return (runs.ToArray(), bf.Length);
    }

    /// <summary>Seçili dosyalar: ad, boyut, yüzde.</summary>
    public (string name, long size, double percent)[] Files()
    {
        if (_m == null) return Array.Empty<(string, long, double)>();
        return _m.Files.Where(f => f.Priority != MonoTorrent.Priority.DoNotDownload)
            .Select(f => (f.Path, f.Length, f.BitField.Length == 0 ? 0 : f.BitField.PercentComplete)).ToArray();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _item.Status = DownloadStatus.Connecting;
        _item.Phase = "Torrent hazırlanıyor";
        _item.Error = null;
        if (string.IsNullOrEmpty(_item.TorrentPath) || !File.Exists(_item.TorrentPath))
            throw new FileNotFoundException("Torrent dosyası bulunamadı, yeniden ekleyin");
        var meta = TorrentMeta.Load(_item.TorrentPath);
        var engine = await SharedAsync();
        _m = await engine.AddAsync(meta, _item.Folder, _item.TorrentFiles);
        await _m.StartAsync();
        long last = -1;
        try
        {
            while (true)
            {
                await Task.Delay(500, ct);
                if (_m.State == TorrentState.Error) throw new IOException(_m.Error?.Exception?.Message ?? "Torrent motoru hata verdi");
                var (total, done) = TorrentEngine.Progress(_m);
                if (total > 0) _item.Size = total;
                _item.Downloaded = done;
                if (last >= 0 && done > last) App.Manager.Usage.Add(done - last);
                last = done;
                _item.Speed = _m.Monitor.DownloadRate;
                _item.ActiveConnections = _m.OpenConnections;
                _item.Status = _m.State == TorrentState.Downloading ? DownloadStatus.Downloading : DownloadStatus.Connecting;
                _item.Phase = _m.State switch
                {
                    TorrentState.Hashing => "Mevcut parçalar denetleniyor",
                    TorrentState.Metadata => "Dosya bilgisi alınıyor",
                    TorrentState.Starting => "Başlatılıyor",
                    _ => _m.OpenConnections == 0 ? "Eş aranıyor" : $"{_m.Peers.Seeds} kaynak, {_m.OpenConnections} bağlantı",
                };
                if (_m.State is TorrentState.Seeding || (total > 0 && _m.PartialProgress >= 100)) break;
            }
        }
        catch (OperationCanceledException)
        {
            await _m.StopAsync();
            throw;
        }
        if (!App.Settings.TorrentSeedWhenDone) await _m.StopAsync();
        _item.Downloaded = _item.Size;
        _item.Speed = 0;
        _item.Phase = null;
        _item.Status = DownloadStatus.Completed;
        _item.CompletedAt = DateTime.Now;
    }
}
