using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HizliIndir.Core;

public sealed record VideoChoice(string Key, string Label);

/// <summary>yt-dlp ile video indirme. Parçalar -N ile paralel çekilir, ses ve görüntü ffmpeg ile birleştirilir.</summary>
public sealed class VideoJob
{
    public static readonly VideoChoice[] Choices =
    {
        new("best", "En iyi kalite"), new("2160", "2160p (4K)"), new("1440", "1440p"), new("1080", "1080p"),
        new("720", "720p"), new("480", "480p"), new("360", "360p"), new("mp3", "Yalnızca ses (MP3)"), new("m4a", "Yalnızca ses (M4A)"),
    };

    readonly DownloadItem _item;
    readonly object _gate = new();
    readonly List<VideoStreamState> _streams = new();
    readonly HashSet<string> _finishedFormats = new(StringComparer.Ordinal);
    VideoStreamState? _current;
    long _bytesBeforeCurrent; // tamamlanan seçili akışların toplamı (requested_formats)
    long _expectedTotal;      // seçili requested_formats filesize toplamı
    bool _expectedApprox;
    public int FragmentWorkers { get; private set; }

    public VideoJob(DownloadItem item) { _item = item; }

    public VideoStreamState[] Snapshot()
    {
        lock (_gate) return _streams.Select(s => new VideoStreamState
        {
            Index = s.Index, Label = s.Label, FormatId = s.FormatId, Ext = s.Ext,
            Done = s.Done, Total = s.Total, FragmentIndex = s.FragmentIndex, FragmentCount = s.FragmentCount,
            Info = s.Info, Finished = s.Finished,
        }).ToArray();
    }

    public static string? CookieFileFor(DownloadItem item) =>
        item.Cookies != null && item.Cookies.StartsWith("# Netscape") ? Path.Combine(Paths.Temp, item.Id.ToString("N") + ".cookies.txt") : null;

    public async Task RunAsync(CancellationToken ct)
    {
        _item.Status = DownloadStatus.Connecting;
        _item.Phase = "Araçlar hazırlanıyor";
        _item.Error = null;
        _item.SizeIsApprox = false;
        await Tools.EnsureAsync(new Progress<string>(m => _item.Phase = m), ct);
        _item.Phase = null;
        Directory.CreateDirectory(_item.Folder);

        var args = new List<string>();
        var q = _item.VideoFormat ?? App.Settings.DefaultVideoQuality;
        if (q == "mp3") args.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0" });
        else if (q == "m4a") args.AddRange(new[] { "-f", "ba[ext=m4a]/ba/b", "-x", "--audio-format", "m4a" });
        else
        {
            args.AddRange(new[] { "-f", "bv*+ba/b", "--merge-output-format", "mp4" });
            args.AddRange(new[] { "-S", q == "best" ? "vcodec:h264,res,acodec:m4a" : $"res:{q},vcodec:h264,acodec:m4a" });
        }
        int frags = Math.Clamp(App.Settings.VideoFragments, 1, 32);
        FragmentWorkers = frags;
        args.AddRange(new[]
        {
            "-N", frags.ToString(), "--newline", "--progress", "--no-simulate", "--no-mtime", "--no-playlist",
            "--encoding", "utf-8", "--windows-filenames",
            "--ffmpeg-location", Paths.Tools,
            "--js-runtimes", "deno:" + Tools.Deno,
            // HZI: yalnızca seçilen requested_formats boyutları (tüm format listesi değil)
            "--print", "before_dl:HZI|%(requested_formats.0.filesize,0)s|%(requested_formats.0.filesize_approx,0)s|%(requested_formats.1.filesize,0)s|%(requested_formats.1.filesize_approx,0)s|%(requested_formats.2.filesize,0)s|%(requested_formats.2.filesize_approx,0)s|%(filesize,0)s|%(filesize_approx,0)s",
            "--progress-template", "download:HZP|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.status)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.format_id)s|%(info.ext)s|%(info.vcodec,none)s|%(info.acodec,none)s",
            "--print", "before_dl:HZT|%(title)s",
            "--print", "after_move:HZF|%(filepath)s",
            "-P", _item.Folder, "-o", "%(title).150B [%(id)s].%(ext)s",
        });
        if (!string.IsNullOrEmpty(_item.UserAgent)) args.AddRange(new[] { "--user-agent", _item.UserAgent });
        if (!string.IsNullOrEmpty(_item.Referer)) args.AddRange(new[] { "--referer", _item.Referer });
        var cookieFile = CookieFileFor(_item);
        if (cookieFile != null)
        {
            await File.WriteAllTextAsync(cookieFile, _item.Cookies, ct);
            args.AddRange(new[] { "--cookies", cookieFile });
        }
        if (App.Settings.SpeedLimitEnabled && App.Settings.SpeedLimitKBps > 0) args.AddRange(new[] { "-r", App.Settings.SpeedLimitKBps + "K" });
        if (App.Settings.Proxy == ProxyMode.Manual && !string.IsNullOrWhiteSpace(App.Settings.ProxyHost))
            args.AddRange(new[] { "--proxy", App.Settings.ProxyHost.Contains("://") ? App.Settings.ProxyHost : $"http://{App.Settings.ProxyHost}:{App.Settings.ProxyPort}" });
        args.Add("--");
        args.Add(_item.PageUrl ?? _item.Url);

        var psi = new ProcessStartInfo(Tools.YtDlp)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Paths.Tools,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        Log.Write("yt-dlp " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)).Replace(_item.Cookies ?? "\u0000", "***"));

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var errors = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) OnLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) { errors.AppendLine(e.Data); if (e.Data.StartsWith("ERROR")) Log.Write("yt-dlp: " + e.Data); } };
        p.Start();
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        _item.Status = DownloadStatus.Downloading;
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }
        finally
        {
            if (cookieFile != null) try { File.Delete(cookieFile); } catch { }
        }
        if (p.ExitCode != 0)
        {
            var msg = errors.ToString().Split('\n').LastOrDefault(l => l.StartsWith("ERROR"))?.Trim() ?? $"yt-dlp çıkış kodu {p.ExitCode}";
            throw new IOException(msg.Replace("ERROR: ", ""));
        }
        ApplyFinalFileSize();
        _item.Phase = null;
        _item.Status = DownloadStatus.Completed;
        _item.CompletedAt = DateTime.Now;
    }

    void ApplyFinalFileSize()
    {
        var finalPath = _finalPath;
        if (finalPath == null || !File.Exists(finalPath))
        {
            // after_move kaçmış olabilir: klasörde başlığa yakın en yeni mp4/mkv
            try
            {
                var cand = Directory.GetFiles(_item.Folder)
                    .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (cand != null) finalPath = cand;
            }
            catch { }
        }
        if (finalPath != null && File.Exists(finalPath))
        {
            _item.FileName = Path.GetFileName(finalPath);
            _item.Folder = Path.GetDirectoryName(finalPath)!;
            long len = new FileInfo(finalPath).Length;
            _item.Size = len;
            _item.Downloaded = len;
            _item.SizeIsApprox = false;
            Log.Write($"{_item.FileName}: video tamamlandı, gerçek boyut {Format.Bytes(len)}");
        }
    }

    string? _finalPath;
    void OnLine(string line)
    {
        if (line.StartsWith("HZT|"))
        {
            _item.VideoTitle = line[4..];
            if (string.IsNullOrEmpty(_item.FileName) || _item.FileName.StartsWith("video")) _item.FileName = FileNames.Sanitize(_item.VideoTitle);
        }
        else if (line.StartsWith("HZI|"))
        {
            // requested_formats[0..2] filesize / filesize_approx + tek format fallback
            var f = line.Split('|');
            long sum = 0; bool approx = false; bool any = false;
            for (int i = 0; i < 3; i++)
            {
                long exact = ParseLong(At(f, 1 + i * 2));
                long ap = ParseLong(At(f, 2 + i * 2));
                if (exact > 0) { sum += exact; any = true; }
                else if (ap > 0) { sum += ap; any = true; approx = true; }
            }
            if (!any)
            {
                long exact = ParseLong(At(f, 7));
                long ap = ParseLong(At(f, 8));
                if (exact > 0) { sum = exact; any = true; }
                else if (ap > 0) { sum = ap; any = true; approx = true; }
            }
            if (any && sum > 0)
            {
                _expectedTotal = sum;
                _expectedApprox = approx;
                _item.Size = sum;
                _item.SizeIsApprox = approx;
                _item.RefreshComputed();
                Log.Write($"{_item.FileName}: seçilen format boyutu {(approx ? "~" : "")}{Format.Bytes(sum)}");
            }
        }
        else if (line.StartsWith("HZF|")) _finalPath = line[4..].Trim();
        else if (line.StartsWith("HZP|"))
        {
            var f = line.Split('|');
            long done = ParseLong(At(f, 1));
            long totalExact = ParseLong(At(f, 2));
            long totalEst = ParseLong(At(f, 3));
            double speed = double.TryParse(At(f, 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) ? sp : 0;
            string status = At(f, 5).Trim();
            int fragIdx = ParseInt(At(f, 6));
            int fragCnt = ParseInt(At(f, 7));
            string formatId = At(f, 8);
            string ext = At(f, 9);
            string vcodec = At(f, 10);
            string acodec = At(f, 11);

            // Aynı format için finished tekrarlarını yok say (eski hata: her satırda boyuta ekleniyordu).
            if (status == "finished" && !string.IsNullOrEmpty(formatId) && _finishedFormats.Contains(formatId))
                return;

            lock (_gate)
            {
                bool newStream = _current == null
                    || (!string.IsNullOrEmpty(formatId) && !string.Equals(_current.FormatId, formatId, StringComparison.Ordinal) && !_current.Finished);
                if (newStream)
                {
                    // Önceki akış bitmeden format değiştiyse birikimi koru
                    if (_current != null && !_current.Finished)
                        _bytesBeforeCurrent += Math.Max(_current.Done, 0);
                    _current = new VideoStreamState
                    {
                        Index = _streams.Count + 1,
                        Label = KindLabel(vcodec, acodec, ext),
                        FormatId = formatId,
                        Ext = ext,
                    };
                    _streams.Add(_current);
                }

                _current!.Done = Math.Max(0, done);
                // Bu akışın toplamı: önce exact, yoksa estimate; beklenen toplamı aşmasın
                long streamTotal = totalExact > 0 ? totalExact : totalEst;
                if (streamTotal > 0)
                {
                    if (_expectedTotal > 0 && _bytesBeforeCurrent + streamTotal > _expectedTotal * 2)
                    {
                        // Anlamsız şişme (tüm format listesi vb.): yok say, beklenen kalana güven
                        streamTotal = Math.Max(_current.Done, _expectedTotal - _bytesBeforeCurrent);
                    }
                    _current.Total = streamTotal;
                }
                _current.FragmentIndex = fragIdx;
                _current.FragmentCount = fragCnt;
                if (fragCnt > 0)
                    _current.Info = $"Parça {Math.Max(1, fragIdx)}/{fragCnt} · {FragmentWorkers} paralel · Veri alınıyor…";
                else
                    _current.Info = status == "finished" ? "Tamamlandı" : "Veri alınıyor…";

                if (status == "finished")
                {
                    _current.Finished = true;
                    _current.Info = "Tamamlandı";
                    if (_current.Total <= 0) _current.Total = _current.Done;
                    if (!string.IsNullOrEmpty(formatId)) _finishedFormats.Add(formatId);
                    else _finishedFormats.Add("anon-" + _current.Index);
                    _bytesBeforeCurrent += _current.Total > 0 ? _current.Total : _current.Done;
                    _current = null;
                }
            }

            long curDone = 0, curTotal = 0;
            lock (_gate)
            {
                if (_current != null) { curDone = _current.Done; curTotal = _current.Total; }
            }
            long downloaded = _bytesBeforeCurrent + curDone;
            long size;
            if (_expectedTotal > 0) size = Math.Max(_expectedTotal, downloaded);
            else if (curTotal > 0) size = _bytesBeforeCurrent + curTotal;
            else size = Math.Max(_item.Size, downloaded);

            _item.Downloaded = downloaded;
            _item.Size = size;
            if (_expectedTotal > 0) _item.SizeIsApprox = _expectedApprox && downloaded < _expectedTotal;
            _item.Speed = speed;
            _item.Phase = null;
            _item.RefreshComputed();
        }
        else if (line.Contains("[Merger]") || line.Contains("[ExtractAudio]") || line.Contains("[VideoConvertor]"))
        {
            _item.Status = DownloadStatus.Merging;
            _item.Phase = line.Contains("[ExtractAudio]") ? "Ses dönüştürülüyor" : "Ses ve görüntü birleştiriliyor";
            lock (_gate)
            {
                _streams.Add(new VideoStreamState
                {
                    Index = _streams.Count + 1,
                    Label = "Birleştirme",
                    Info = _item.Phase,
                });
            }
        }
    }

    static string KindLabel(string vcodec, string acodec, string ext)
    {
        bool hasV = !string.IsNullOrEmpty(vcodec) && vcodec != "none";
        bool hasA = !string.IsNullOrEmpty(acodec) && acodec != "none";
        if (hasV && hasA) return "Görüntü+ses";
        if (hasV) return "Görüntü";
        if (hasA) return "Ses";
        return string.IsNullOrEmpty(ext) ? "Akış" : ext.ToUpperInvariant() + " akışı";
    }

    static string At(string[] f, int i) => i < f.Length ? f[i] : "";
    static long ParseLong(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s is "NA" or "None" or "null") return 0;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d : 0;
    }
    static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
