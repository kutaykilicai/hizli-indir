using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Win32.SafeHandles;

namespace HizliIndir.Core;

sealed class RangeUnsupportedException : Exception
{
    public RangeUnsupportedException() : base("Sunucu bölümlü indirmeyi desteklemiyor") { }
}

sealed class ThrottleException : Exception
{
    public ThrottleException(string message) : base(message) { }
}

/// <summary>
/// Tek bir HTTP(S) indirmesi. Dosya baştan tam boyutta ayrılır, her bağlantı kendi bölümünü
/// doğrudan dosyadaki yerine yazar. İş bitince birleştirme yapılmaz, yalnızca ad değiştirilir.
/// </summary>

/// <summary>Oturum boyu ana bilgisayar bağlantı tavanı; 429/503 sonrası düşer.</summary>
static class HostSessionLimits
{
    sealed class State
    {
        public int Cap = int.MaxValue;
        public DateTime BlockUntil;
        public int Hits;
    }
    static readonly ConcurrentDictionary<string, State> Map = new(StringComparer.OrdinalIgnoreCase);

    public static int Clamp(string host, int wanted)
    {
        if (string.IsNullOrEmpty(host)) return wanted;
        var s = Map.GetOrAdd(host, _ => new State());
        int cap = Math.Max(1, Math.Min(wanted, s.Cap));
        return cap;
    }

    public static TimeSpan OnThrottle(string host, int status, TimeSpan? retryAfter)
    {
        var s = Map.GetOrAdd(host, _ => new State());
        s.Hits++;
        int newCap;
        lock (s)
        {
            if (s.Cap == int.MaxValue) newCap = Math.Max(1, 4); // ilk 429: en fazla 4
            else newCap = Math.Max(1, s.Cap - 1);
            if (newCap < s.Cap)
            {
                s.Cap = newCap;
                Log.Write($"Sunucu sınırı: {host} → en fazla {s.Cap} bağlantı (HTTP {status})");
            }
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, Math.Min(s.Hits, 6)), 60_000));
            var until = DateTime.UtcNow + delay;
            if (until > s.BlockUntil) s.BlockUntil = until;
            return delay;
        }
    }

    public static async Task WaitIfBlockedAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(host) || !Map.TryGetValue(host, out var s)) return;
        TimeSpan wait;
        lock (s) wait = s.BlockUntil - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }
}

public sealed class HttpJob
{
    const int BufferSize = 512 * 1024;
    const long MinSegment = 1024 * 1024;     // ilk bölme için en küçük parça
    const long MinSplit = 512 * 1024;        // dinamik bölmede kalan parçanın en az iki katı

    readonly DownloadItem _item;
    readonly SpeedLimiter _limiter;
    readonly UsageWindow _usage;
    SafeFileHandle? _handle;
    long _written;
    int _active;
    long _lastByteTicks;
    bool _unknownSize;

    public HttpJob(DownloadItem item, SpeedLimiter limiter, UsageWindow usage)
    {
        _item = item; _limiter = limiter; _usage = usage;
    }

    public long Written => Interlocked.Read(ref _written);
    public int ActiveConnections => Volatile.Read(ref _active);
    public int PeakConnections { get; private set; }
    public SpeedLimiter? OwnLimiter { get; set; }

    public SegmentState[] Snapshot()
    {
        lock (_item.Segments) return _item.Segments.Select(s => new SegmentState { Start = s.Start, End = s.End, Done = s.Done, Active = s.Active, Index = s.Index, Info = s.Info }).ToArray();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _item.Status = DownloadStatus.Connecting;
        _item.Error = null;
        long startTicks = Stopwatch.GetTimestamp();
        long startBytes = _item.Downloaded;
        _item.LastTryAt = DateTime.Now;
        var settings = App.Settings;
        var host = Uri.TryCreate(_item.Url, UriKind.Absolute, out var u) ? u.Host : "";
        int connections = Math.Clamp(_item.MaxConnections ?? settings.ConnectionsFor(host), 1, 32);
        connections = HostSessionLimits.Clamp(host, connections);

        bool resume = _item.Segments.Count > 0 && File.Exists(_item.PartPath);
        Log.Write($"{_item.FileName}: bağlanılıyor ({connections} bağlantı hedefi)");
        var probe = await Probe.RunAsync(_item, ct);
        Log.Write($"{_item.FileName}: sunucu yanıtı boyut={probe.Size} bölümlü={(probe.Ranges ? "evet" : "hayır")}");
        if (resume)
        {
            bool same = probe.Size == _item.Size && probe.Size > 0 &&
                        (string.IsNullOrEmpty(_item.ETag) || string.IsNullOrEmpty(probe.ETag) || _item.ETag == probe.ETag);
            if (!same || !probe.Ranges)
            {
                Log.Write($"{_item.FileName}: sunucudaki dosya değişti veya devam desteklenmiyor, baştan başlıyor");
                resume = false;
            }
        }
        if (!resume)
        {
            _item.Segments.Clear();
            _item.Size = probe.Size;
            _item.AcceptRanges = probe.Ranges;
            _item.ETag = probe.ETag;
            _item.LastModified = probe.LastModified;
            if (string.IsNullOrWhiteSpace(_item.FileName)) _item.FileName = probe.FileName ?? "indirme";
            Directory.CreateDirectory(_item.Folder);
            if (File.Exists(_item.PartPath)) File.Delete(_item.PartPath);
            if (File.Exists(_item.FullPath) && settings.Duplicates == DuplicateMode.Rename)
                _item.FileName = FileNames.Unique(_item.Folder, _item.FileName);
            Plan(connections);
        }
        _unknownSize = _item.Size <= 0;

        var part = _item.PartPath;
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        if (!_item.PartUsesTempFolder)
            Log.Write($"{_item.FileName}: hedef farklı sürücü — kısmi dosya hedef klasörde ayrılıyor ({part})");
        else if (!resume)
            Log.Write($"{_item.FileName}: kısmi → {part}");
        var mode = resume ? FileMode.Open : FileMode.Create;
        _handle = File.OpenHandle(part, mode, FileAccess.ReadWrite, FileShare.Read,
            FileOptions.Asynchronous, _item.Size > 0 && !resume ? _item.Size : 0);
        try
        {
            if (_item.Size > 0 && RandomAccess.GetLength(_handle) != _item.Size) RandomAccess.SetLength(_handle, _item.Size);
            lock (_item.Segments)
            {
                for (int i = 0; i < _item.Segments.Count; i++)
                {
                    var s = _item.Segments[i];
                    s.Index = i; s.Reserved = s.Done; s.Active = false;
                }
                _written = _item.Segments.Sum(s => s.Done - s.Start);
            }
            _item.Status = DownloadStatus.Downloading;
            try
            {
                await RunWorkersAsync(connections, ct);
            }
            catch (RangeUnsupportedException)
            {
                Log.Write($"{_item.FileName}: bölümlü indirme reddedildi, tek bağlantıya geçiliyor");
                _item.AcceptRanges = false;
                lock (_item.Segments) { _item.Segments.Clear(); }
                connections = 1;
                Plan(1);
                _written = 0;
                await RunWorkersAsync(1, ct);
            }

            if (_unknownSize)
            {
                _item.Size = Written;
                RandomAccess.SetLength(_handle, _item.Size);
            }
        }
        finally
        {
            _handle.Dispose();
            _handle = null;
        }

        // Son bayttan "Tamamlandı"ya kadar geçen süre: yalnızca dosya adı değişikliği.
        var sw = Stopwatch.StartNew();
        var lastByte = Interlocked.Read(ref _lastByteTicks);
        if (File.Exists(_item.FullPath))
        {
            if (settings.Duplicates == DuplicateMode.Overwrite) File.Delete(_item.FullPath);
            else _item.FileName = FileNames.Unique(_item.Folder, _item.FileName);
        }
        File.Move(_item.PartPath, _item.FullPath);
        _item.Downloaded = _item.Size;
        _item.Status = DownloadStatus.Completed;
        _item.CompletedAt = DateTime.Now;
        var finalizeMs = lastByte > 0 ? (Stopwatch.GetTimestamp() - lastByte) * 1000.0 / Stopwatch.Frequency : sw.Elapsed.TotalMilliseconds;
        _item.FinalizeMilliseconds = Math.Round(finalizeMs, 2);
        var transferSec = lastByte > startTicks ? (lastByte - startTicks) / (double)Stopwatch.Frequency : 0;
        if (transferSec > 0) _item.AverageSpeed = Math.Round((_item.Size - startBytes) / transferSec);
        Log.Write($"{_item.FileName}: tamamlandı, {Format.Bytes(_item.Size)}, {PeakConnections} bağlantı (en çok), ortalama {Format.Bytes((long)(_item.AverageSpeed ?? 0))}/sn, son bayttan bitişe {finalizeMs:0.00} ms");
    }

    void Plan(int connections)
    {
        lock (_item.Segments)
        {
            _item.Segments.Clear();
            if (_item.Size <= 0)
            {
                _item.Segments.Add(new SegmentState { Start = 0, End = long.MaxValue - 1, Done = 0 });
                return;
            }
            int count = 1;
            if (_item.AcceptRanges && connections > 1)
                count = (int)Math.Clamp(_item.Size / MinSegment, 1, connections);
            long part = _item.Size / count;
            for (int i = 0; i < count; i++)
            {
                long start = i * part;
                long end = i == count - 1 ? _item.Size - 1 : start + part - 1;
                _item.Segments.Add(new SegmentState { Start = start, End = end, Done = start });
            }
        }
    }

    async Task RunWorkersAsync(int connections, CancellationToken ct)
    {
        var host = Uri.TryCreate(_item.Url, UriKind.Absolute, out var u) ? u.Host : "";
        connections = HostSessionLimits.Clamp(host, connections);
        var running = new List<Task>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void StartNext()
        {
            connections = HostSessionLimits.Clamp(host, connections);
            while (running.Count < connections)
            {
                var seg = NextSegment();
                if (seg == null) return;
                seg.Active = true;
                running.Add(Task.Run(() => WorkerAsync(seg, host, stop.Token)));
            }
        }
        StartNext();
        Exception? fatal = null;
        while (running.Count > 0)
        {
            var t = await Task.WhenAny(running);
            running.Remove(t);
            if (t.IsFaulted && fatal == null)
            {
                var ex = t.Exception!.GetBaseException();
                bool hard = ex is RangeUnsupportedException ||
                            (ex is HttpRequestException hx && hx.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone);
                bool throttle = ex is ThrottleException ||
                                (ex is HttpRequestException hx2 && hx2.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) ||
                                (ex is IOException && ex.Message.Contains("429", StringComparison.Ordinal));
                if (throttle)
                {
                    int cap = HostSessionLimits.Clamp(host, connections);
                    if (cap < connections)
                    {
                        connections = cap;
                        Log.Write($"{_item.FileName}: bağlantı sayısı {connections} ile sınırlandı");
                    }
                    // fatal değil — kalan iş diğer/yeniden başlayan işçilere
                }
                else if (!hard && running.Count > 0 && !ct.IsCancellationRequested)
                {
                    int limit = Math.Max(1, Math.Min(connections, running.Count + 1));
                    // soft: bırak diğerleri çalışsın; StartNext yeni işçi açabilir
                    if (ex is IOException)
                        Log.Write($"{_item.FileName}: bağlantı koptu, kalan iş paylaşılacak ({ex.Message})");
                }
                else if (hard || running.Count == 0)
                {
                    fatal = ex;
                    stop.Cancel();
                }
            }
            if (fatal == null && !ct.IsCancellationRequested) StartNext();
        }
        ct.ThrowIfCancellationRequested();
        if (fatal != null) throw fatal;
        lock (_item.Segments)
        {
            if (!_unknownSize && _item.Segments.Any(s => !s.Finished)) throw new IOException("Bazı bölümler tamamlanamadı");
        }
    }
    /// <summary>Önce boştaki bölüm; yoksa en büyük kalan veya takılı aktif bölümden çalar.</summary>
    SegmentState? NextSegment()
    {
        lock (_item.Segments)
        {
            var idle = _item.Segments.FirstOrDefault(s => !s.Active && !s.Finished);
            if (idle != null) return idle;
            if (!_item.AcceptRanges || _unknownSize) return null;

            SegmentState? victim = null;
            long best = 0;
            foreach (var s in _item.Segments)
            {
                if (s.Finished) continue;
                long rem = s.End - Math.Max(s.Reserved, s.Done) + 1;
                bool stuck = s.Active && (s.Info.StartsWith("Yeniden", StringComparison.Ordinal) || s.Info.StartsWith("Bekleniyor", StringComparison.Ordinal) || s.Info.Contains("429") || s.Info.Contains("503"));
                if (s.Active && rem >= 2 * MinSplit && (victim == null || rem > best || stuck))
                {
                    // takılıysa küçük kalsa bile çalmaya aday
                    if (stuck && rem >= MinSplit) { victim = s; best = Math.Max(rem, best); }
                    else if (rem > best) { victim = s; best = rem; }
                }
            }
            if (victim == null) return null;
            lock (victim)
            {
                long from = Math.Max(victim.Reserved, victim.Done);
                long rem = victim.End - from + 1;
                if (rem < 2 * MinSplit && !(victim.Info.StartsWith("Yeniden", StringComparison.Ordinal) && rem >= MinSplit)) return null;
                long mid = from + rem / 2;
                if (mid <= from || mid > victim.End) return null;
                var n = new SegmentState { Start = mid, End = victim.End, Done = mid, Reserved = mid, Index = _item.Segments.Count };
                victim.End = mid - 1;
                if (victim.Reserved > victim.End) victim.Reserved = victim.End + 1;
                _item.Segments.Add(n);
                return n;
            }
        }
    }

    /// <summary>Yeniden deneme öncesi kalan aralığı havuza bırakır; bitmiş bağlantı hemen çalabilsin.</summary>
    void ReleaseRemainderToPool(SegmentState seg)
    {
        lock (_item.Segments)
        {
            lock (seg)
            {
                if (seg.Finished) { seg.Active = false; return; }
                long from = Math.Max(seg.Done, seg.Start);
                if (from <= seg.End)
                {
                    var n = new SegmentState
                    {
                        Start = from,
                        End = seg.End,
                        Done = from,
                        Reserved = from,
                        Index = _item.Segments.Count,
                        Info = "Havuzda",
                    };
                    _item.Segments.Add(n);
                    seg.End = from - 1;
                    seg.Reserved = from;
                }
                seg.Active = false;
                if (!seg.Finished) seg.Info = "Bırakıldı";
            }
        }
    }

    static TimeSpan? ReadRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter?.Delta is TimeSpan d) return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        if (resp.Headers.RetryAfter?.Date is DateTimeOffset dto)
        {
            var w = dto - DateTimeOffset.UtcNow;
            return w < TimeSpan.Zero ? TimeSpan.Zero : w;
        }
        return null;
    }
    async Task WorkerAsync(SegmentState seg, string host, CancellationToken ct)
    {
        var live = Interlocked.Increment(ref _active);
        if (live > PeakConnections) PeakConnections = live;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        int attempts = 0;
        long attemptStart = seg.Done;
        var timeout = TimeSpan.FromSeconds(Math.Max(10, App.Settings.TimeoutSeconds));
        int maxRetry = Math.Max(1, App.Settings.RetryCount);
        try
        {
            while (!seg.Finished)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await HostSessionLimits.WaitIfBlockedAsync(host, ct);
                    long from;
                    lock (seg) { seg.Reserved = seg.Done; from = seg.Done; }
                    attemptStart = from;
                    if (!_item.AcceptRanges && from > 0)
                    {
                        Interlocked.Add(ref _written, -(from - seg.Start));
                        lock (seg) { seg.Done = seg.Start; seg.Reserved = seg.Start; }
                        from = seg.Start;
                    }
                    long? to = _unknownSize || !_item.AcceptRanges ? null : seg.End;
                    using var req = _item.AcceptRanges ? Net.Build(_item, from, to) : Net.Build(_item);
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(timeout);
                    seg.Info = "GET gönderiliyor…";
                    using var resp = await Net.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, readCts.Token);
                    if (_item.AcceptRanges && resp.StatusCode == HttpStatusCode.OK && from > 0) throw new RangeUnsupportedException();
                    if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
                        throw new HttpRequestException($"Sunucu {(int)resp.StatusCode} döndürdü", null, resp.StatusCode);
                    if (resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    {
                        int code = (int)resp.StatusCode;
                        var delay = HostSessionLimits.OnThrottle(host, code, ReadRetryAfter(resp));
                        seg.Info = $"HTTP {code}, {delay.TotalSeconds:0}sn bekleniyor…";
                        ReleaseRemainderToPool(seg);
                        await Task.Delay(delay, ct);
                        throw new ThrottleException($"Bağlantı {seg.Index + 1}: HTTP {code}");
                    }
                    resp.EnsureSuccessStatusCode();
                    await using var stream = await resp.Content.ReadAsStreamAsync(readCts.Token);
                    seg.Info = "Veri alınıyor…";
                    while (true)
                    {
                        readCts.CancelAfter(timeout);
                        int n = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), readCts.Token);
                        if (n == 0)
                        {
                            if (_unknownSize) { lock (seg) seg.End = seg.Done - 1; }
                            break;
                        }
                        long writePos; int allowed;
                        lock (seg)
                        {
                            allowed = (int)Math.Min(n, seg.End - seg.Reserved + 1);
                            writePos = seg.Reserved;
                            if (allowed > 0) seg.Reserved += allowed;
                        }
                        if (allowed <= 0) break;
                        await _limiter.WaitAsync(allowed, ct);
                        if (OwnLimiter != null) await OwnLimiter.WaitAsync(allowed, ct);
                        await RandomAccess.WriteAsync(_handle!, buffer.AsMemory(0, allowed), writePos, ct);
                        lock (seg) seg.Done = writePos + allowed;
                        Interlocked.Add(ref _written, allowed);
                        Interlocked.Exchange(ref _lastByteTicks, Stopwatch.GetTimestamp());
                        _usage.Add(allowed);
                        attempts = 0;
                        if (allowed < n || seg.Finished) break;
                    }
                    if (!seg.Finished && !_unknownSize) throw new IOException("Bağlantı erken kapandı");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (RangeUnsupportedException) { throw; }
                catch (ThrottleException)
                {
                    return;
                }
                catch (HttpRequestException hx) when (hx.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempts++;
                    seg.Info = "Yeniden deneniyor…";
                    bool extra = Volatile.Read(ref _active) > 1 && seg.Done == attemptStart;
                    if (attempts > maxRetry || (extra && attempts >= 2))
                    {
                        ReleaseRemainderToPool(seg);
                        throw new IOException($"Bağlantı {seg.Index + 1}: {ex.Message}", ex);
                    }
                    ReleaseRemainderToPool(seg);
                    var backoff = TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempts - 1), 8000));
                    await Task.Delay(backoff, ct);
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (seg.Finished) seg.Info = "Tamamlandı";
            else if (string.IsNullOrEmpty(seg.Info) || seg.Info.StartsWith("Veri") || seg.Info.StartsWith("GET")) seg.Info = "Bekliyor";
            seg.Active = false;
            Interlocked.Decrement(ref _active);
        }
    }
}
