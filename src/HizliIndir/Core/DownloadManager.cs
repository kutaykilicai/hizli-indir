using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace HizliIndir.Core;

public sealed class DownloadManager
{
    sealed class Running
    {
        public required CancellationTokenSource Cts;
        public HttpJob? Http;
        public VideoJob? Video;
        public Task? Task;
        public long LastBytes;
        public long LastTicks = Stopwatch.GetTimestamp();
        public bool StartedByQueue;
    }

    public ObservableCollection<DownloadItem> Items { get; } = new();
    public ObservableCollection<QueueInfo> Queues { get; } = new();
    public SpeedLimiter Limiter { get; } = new();
    public UsageWindow Usage { get; } = new();

    readonly Dictionary<Guid, Running> _running = new();
    readonly Dictionary<Guid, SpeedLimiter> _ownLimits = new();
    readonly DispatcherTimer _tick;
    readonly Dispatcher _ui;
    DateTime _lastSave = DateTime.Now;
    bool _dirty;
    bool _limitWarned;

    public event Action<DownloadItem>? Completed;
    public event Action<DownloadItem>? Failed;
    public event Action<QueueInfo>? QueueFinished;
    public event Action<string>? LimitReached;

    public DownloadManager(Dispatcher ui)
    {
        _ui = ui;
        foreach (var i in Json.LoadDownloads(Paths.DownloadsFile))
        {
            if (i.IsActive) i.Status = DownloadStatus.Paused;
            if (i.Status == DownloadStatus.Completed && i.Size > 0) i.Downloaded = i.Size;
            else i.Downloaded = i.Segments.Sum(s => Math.Max(0, Math.Min(s.Done, s.End + 1) - s.Start));
            CorrectSizeAgainstDisk(i);
            Items.Add(i);
        }
        foreach (var q in Json.Load(Paths.QueuesFile, () => new List<QueueInfo>())) Queues.Add(q);
        if (!Queues.Any(q => q.Id == QueueInfo.MainId)) Queues.Insert(0, new QueueInfo { Id = QueueInfo.MainId, Name = "Ana kuyruk", MaxParallel = 2 });
        ApplySpeedLimit();
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick(), ui);
        _tick.Start();
    }

    public void ApplySpeedLimit()
    {
        var s = App.Settings;
        Limiter.BytesPerSecond = s.SpeedLimitEnabled ? s.SpeedLimitKBps * 1024L : 0;
    }

    public void SetOwnLimit(DownloadItem item, long bytesPerSecond)
    {
        if (!_ownLimits.TryGetValue(item.Id, out var l)) _ownLimits[item.Id] = l = new SpeedLimiter();
        l.BytesPerSecond = bytesPerSecond;
        if (_running.TryGetValue(item.Id, out var r) && r.Http != null) r.Http.OwnLimiter = bytesPerSecond > 0 ? l : null;
    }

    public HttpJob? JobOf(DownloadItem item) => _running.TryGetValue(item.Id, out var r) ? r.Http : null;
    public VideoJob? VideoOf(DownloadItem item) => _running.TryGetValue(item.Id, out var r) ? r.Video : null;
    public bool IsRunning(DownloadItem item) => _running.ContainsKey(item.Id);

    public void Add(DownloadItem item, bool start)
    {
        Items.Insert(0, item);
        MarkDirty();
        if (start) Start(item);
    }

    public void Start(DownloadItem item, bool byQueue = false)
    {
        if (_running.ContainsKey(item.Id) || item.Status == DownloadStatus.Completed) return;
        if (App.Settings.DownloadLimitEnabled && LimitExceeded())
        {
            item.Status = DownloadStatus.Paused;
            LimitReached?.Invoke("İndirme sınırı aşıldı. Süre dolunca indirmeler yeniden başlatılabilir.");
            return;
        }
        var r = new Running { Cts = new CancellationTokenSource(), StartedByQueue = byQueue };
        _running[item.Id] = r;
        if (item.Kind == DownloadKind.Http)
        {
            r.Http = new HttpJob(item, Limiter, Usage);
            if (_ownLimits.TryGetValue(item.Id, out var own) && own.BytesPerSecond > 0) r.Http.OwnLimiter = own;
            r.Task = Task.Run(() => r.Http.RunAsync(r.Cts.Token));
        }
        else
        {
            r.Video = new VideoJob(item);
            r.Task = Task.Run(() => r.Video.RunAsync(r.Cts.Token));
        }
        if (App.Settings.ShowProgressWindow)
            _ui.BeginInvoke(() => UI.ProgressWindow.ShowFor(item));
        r.Task.ContinueWith(t => _ui.BeginInvoke(() => Finish(item, r, t)));
    }

    void Finish(DownloadItem item, Running r, Task t)
    {
        _running.Remove(item.Id);
        item.Speed = 0; item.ActiveConnections = 0;
        if (t.IsCanceled || (t.IsFaulted && r.Cts.IsCancellationRequested))
        {
            if (item.Status != DownloadStatus.Completed) item.Status = DownloadStatus.Paused;
        }
        else if (t.IsFaulted)
        {
            var ex = t.Exception!.GetBaseException();
            item.Error = Friendly(ex);
            item.Status = DownloadStatus.Error;
            Log.Write($"{item.FileName}: hata {ex}");
            Failed?.Invoke(item);
        }
        else
        {
            var secs = item.LastTryAt.HasValue ? (DateTime.Now - item.LastTryAt.Value).TotalSeconds : 0;
            if (secs > 0 && item.Size > 0) item.AverageSpeed = item.Size / secs;
            Completed?.Invoke(item);
            PostProcess(item);
        }
        if (r.Http != null) item.Downloaded = item.Status == DownloadStatus.Completed ? item.Size : r.Http.Written;
        MarkDirty();
        Save();
        PumpQueues();
    }

    static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException h when h.StatusCode != null => $"Sunucu {(int)h.StatusCode.Value} döndürdü",
        HttpRequestException h => "Bağlantı kurulamadı: " + h.Message,
        UnauthorizedAccessException => "Klasöre yazma izni yok",
        TaskCanceledException => "Zaman aşımı",
        _ => ex.Message,
    };

    void PostProcess(DownloadItem item)
    {
        var s = App.Settings;
        if (s.VirusScanEnabled && File.Exists(s.VirusScanner))
        {
            try
            {
                Process.Start(new ProcessStartInfo(s.VirusScanner, s.VirusScannerArgs.Replace("[DOSYA]", item.FullPath)) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { Log.Write("Virüs denetimi başlatılamadı: " + ex.Message); }
        }
        if (item.OpenWhenDone) Shell.Open(item.FullPath);
    }

    public void Pause(DownloadItem item)
    {
        if (_running.TryGetValue(item.Id, out var r)) r.Cts.Cancel();
        else if (item.Status is DownloadStatus.Queued) item.Status = DownloadStatus.Paused;
    }

    public void PauseAll()
    {
        foreach (var q in Queues) q.Running = false;
        foreach (var r in _running.Values.ToList()) r.Cts.Cancel();
    }

    public void Remove(DownloadItem item, bool deleteFile)
    {
        Pause(item);
        Items.Remove(item);
        if (deleteFile)
        {
            Task.Delay(400).ContinueWith(_ =>
            {
                try { if (File.Exists(item.PartPath)) File.Delete(item.PartPath); } catch { }
                try { if (item.IsCompleted && File.Exists(item.FullPath)) File.Delete(item.FullPath); } catch { }
            });
        }
        else if (!item.IsCompleted)
        {
            Task.Delay(400).ContinueWith(_ => { try { if (File.Exists(item.PartPath)) File.Delete(item.PartPath); } catch { } });
        }
        MarkDirty();
    }

    public void Redownload(DownloadItem item)
    {
        Pause(item);
        item.Segments.Clear();
        item.Downloaded = 0;
        item.Status = DownloadStatus.Queued;
        item.CompletedAt = null;
        Start(item);
    }

    // Kuyruklar
    public void StartQueue(QueueInfo q)
    {
        q.Running = true;
        foreach (var i in Items.Where(i => i.QueueId == q.Id && i.Status is DownloadStatus.Paused or DownloadStatus.Error))
            i.Status = DownloadStatus.Queued;
        PumpQueues();
    }

    public void StopQueue(QueueInfo q)
    {
        q.Running = false;
        foreach (var i in Items.Where(i => i.QueueId == q.Id).ToList())
            if (_running.TryGetValue(i.Id, out var r) && r.StartedByQueue) r.Cts.Cancel();
    }

    public void PumpQueues()
    {
        foreach (var q in Queues.Where(q => q.Running).ToList())
        {
            var mine = Items.Where(i => i.QueueId == q.Id).ToList();
            int active = mine.Count(i => _running.ContainsKey(i.Id));
            // Kuyruk sırası listenin altından üstüne (ilk eklenen önce)
            foreach (var i in mine.Where(i => i.Status == DownloadStatus.Queued && !_running.ContainsKey(i.Id)).Reverse())
            {
                if (active >= Math.Max(1, q.MaxParallel)) break;
                Start(i, true);
                active++;
            }
            if (active == 0 && !mine.Any(i => i.Status == DownloadStatus.Queued))
            {
                q.Running = false;
                QueueFinished?.Invoke(q);
            }
        }
    }

    public bool LimitExceeded()
    {
        var s = App.Settings;
        return Usage.BytesInLast(TimeSpan.FromHours(Math.Max(1, s.DownloadLimitHours))) >= s.DownloadLimitMegabytes * 1024L * 1024L;
    }

    void Tick()
    {
        foreach (var (id, r) in _running)
        {
            if (r.Http == null) continue;
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            long now = Stopwatch.GetTimestamp();
            long bytes = r.Http.Written;
            double dt = (now - r.LastTicks) / (double)Stopwatch.Frequency;
            if (dt > 0.2)
            {
                double inst = Math.Max(0, (bytes - r.LastBytes) / dt);
                item.Speed = item.Speed <= 0 ? inst : item.Speed * 0.6 + inst * 0.4;
                r.LastBytes = bytes; r.LastTicks = now;
            }
            item.Downloaded = bytes;
            item.ActiveConnections = r.Http.ActiveConnections;
            item.RefreshComputed();
            _dirty = true;
        }
        var s = App.Settings;
        if (s.DownloadLimitEnabled && _running.Count > 0 && LimitExceeded())
        {
            if (s.DownloadLimitWarn && !_limitWarned)
            {
                _limitWarned = true;
                LimitReached?.Invoke($"Son {s.DownloadLimitHours} saatte {s.DownloadLimitMegabytes} MB sınırına ulaşıldı. İndirmeler durduruluyor.");
            }
            PauseAll();
        }
        else if (!LimitExceeded()) _limitWarned = false;

        if (_dirty && (DateTime.Now - _lastSave).TotalSeconds > 2) Save();
    }

    public void MarkDirty() => _dirty = true;

    public void Save()
    {
        try
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var i in Items.ToList())
                lock (i.Segments) arr.Add(System.Text.Json.JsonSerializer.SerializeToNode(i, Json.Options));
            var tmp = Paths.DownloadsFile + ".tmp";
            File.WriteAllText(tmp, arr.ToJsonString(Json.Options));
            File.Move(tmp, Paths.DownloadsFile, true);
            Json.Save(Paths.QueuesFile, Queues.ToList());
            _dirty = false; _lastSave = DateTime.Now;
        }
        catch (Exception ex) { Log.Write("Kaydedilemedi: " + ex.Message); }
    }

    public int RunningCount => _running.Count;

    /// <summary>Liste boyutunu diskteki gerçek dosyayla hizalar (video/HTTP tamamlananlar).</summary>
    public static void CorrectSizeAgainstDisk(DownloadItem i)
    {
        try
        {
            string path = i.Status == DownloadStatus.Completed ? i.FullPath : i.PartPath;
            if (!File.Exists(path)) return;
            long len = new FileInfo(path).Length;
            if (len <= 0) return;
            if (i.Status == DownloadStatus.Completed)
            {
                if (i.Size != len || i.Downloaded != len || i.SizeIsApprox)
                {
                    Log.Write($"{i.FileName}: boyut düzeltildi {Format.Bytes(i.Size)} → {Format.Bytes(len)}");
                    i.Size = len; i.Downloaded = len; i.SizeIsApprox = false;
                }
            }
            else if (i.Kind == DownloadKind.Http && i.Size > 0 && len > i.Size * 2)
            {
                Log.Write($"{i.FileName}: uyarı liste boyutu {Format.Bytes(i.Size)}, parça {Format.Bytes(len)}");
            }
        }
        catch { }
    }
}

public static class Shell
{
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Log.Write("Açılamadı: " + ex.Message); }
    }
    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(Path.GetDirectoryName(path))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
        }
        catch { }
    }
}
