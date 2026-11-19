using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace HizliIndir.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public enum DownloadStatus { Queued, Connecting, Downloading, Paused, Completed, Error, Merging }
public enum DownloadKind { Http, Video, Torrent }

/// <summary>Dosyanın bir bölümü. Start..End (dahil) aralığını tek bir bağlantı indirir.</summary>
/// <summary>yt-dlp akış/parça durumu (görüntü+ses ve -N parçaları).</summary>
public sealed class VideoStreamState
{
    public int Index { get; set; }
    public string Label { get; set; } = "";
    public string FormatId { get; set; } = "";
    public string Ext { get; set; } = "";
    public long Done { get; set; }
    public long Total { get; set; }
    public int FragmentIndex { get; set; }
    public int FragmentCount { get; set; }
    public string Info { get; set; } = "";
    public bool Finished { get; set; }
}

public sealed class SegmentState
{
    public long Start { get; set; }
    public long End { get; set; }
    /// <summary>Diske yazılmış son konum + 1. Kaldığı yerden devam bunu kullanır.</summary>
    public long Done { get; set; }
    [JsonIgnore] public long Reserved;
    [JsonIgnore] public bool Active;
    [JsonIgnore] public int Index;
    /// <summary>Bağlantı satırında gösterilen kısa durum ("Veri alınıyor…", "GET gönderiliyor…").</summary>
    [JsonIgnore] public string Info = "";
    [JsonIgnore] public long Remaining => Math.Max(0, End - Reserved + 1);
    [JsonIgnore] public bool Finished => Done > End;
}

public sealed class DownloadItem : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DownloadKind Kind { get; set; } = DownloadKind.Http;
    public string Url { get; set; } = "";
    public string? Referer { get; set; }
    public string? PageUrl { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }
    public string? PostData { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? AuthUser { get; set; }
    public string? AuthPasswordProtected { get; set; }
    public string? VideoFormat { get; set; }
    public string? VideoTitle { get; set; }
    public string QueueId { get; set; } = QueueInfo.MainId;
    public int? MaxConnections { get; set; }
    public string? Description { get; set; }
    public bool OpenWhenDone { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public bool AcceptRanges { get; set; }
    /// <summary>Boyut tahmini (filesize_approx); listede ~ ile gösterilir.</summary>
    public bool SizeIsApprox { get; set; }
    public List<SegmentState> Segments { get; set; } = new();
    /// <summary>Torrent: bilgi özeti, kayıtlı .torrent kopyası ve seçili dosya sıraları (null: hepsi).</summary>
    public string? InfoHash { get; set; }
    public string? TorrentPath { get; set; }
    public List<int>? TorrentFiles { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastTryAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? FinalizeMilliseconds { get; set; }
    public double? AverageSpeed { get; set; }

    string _fileName = "";
    public string FileName { get => _fileName; set { if (Set(ref _fileName, value)) Raise(nameof(FullPath)); } }
    string _folder = "";
    public string Folder { get => _folder; set { if (Set(ref _folder, value)) Raise(nameof(FullPath)); } }
    string _category = "Genel";
    public string Category { get => _category; set => Set(ref _category, value); }
    long _size = -1;
    public long Size { get => _size; set { if (Set(ref _size, value)) { Raise(nameof(SizeText)); Raise(nameof(Progress)); } } }
    long _downloaded;
    public long Downloaded { get => _downloaded; set { if (Set(ref _downloaded, value)) { Raise(nameof(Progress)); Raise(nameof(DownloadedText)); } } }
    DownloadStatus _status = DownloadStatus.Queued;
    public DownloadStatus Status { get => _status; set { if (Set(ref _status, value)) { Raise(nameof(StatusText)); Raise(nameof(IsActive)); Raise(nameof(IsCompleted)); } } }
    string? _error;
    public string? Error { get => _error; set { if (Set(ref _error, value)) Raise(nameof(StatusText)); } }

    double _speed;
    [JsonIgnore] public double Speed { get => _speed; set { if (Set(ref _speed, value)) { Raise(nameof(SpeedText)); Raise(nameof(EtaText)); } } }
    int _connections;
    [JsonIgnore] public int ActiveConnections { get => _connections; set => Set(ref _connections, value); }
    string? _phase;
    [JsonIgnore] public string? Phase { get => _phase; set { if (Set(ref _phase, value)) Raise(nameof(StatusText)); } }

    [JsonIgnore] public string FullPath => Path.Combine(Folder, FileName);

    /// <summary>Kısmi dosya yolu: aynı sürücüde TempFolder, değilse hedef klasörde (.hizli).</summary>
    [JsonIgnore] public string PartPath
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(FileName) ? "indirme" : FileName;
            string safe = name;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string temp = App.Settings?.TempFolder;
            if (string.IsNullOrWhiteSpace(temp)) temp = AppSettings.DefaultTemp();
            string finalRoot = Path.GetPathRoot(Path.GetFullPath(string.IsNullOrWhiteSpace(Folder) ? temp : Folder)) ?? "";
            string tempRoot = Path.GetPathRoot(Path.GetFullPath(temp)) ?? "";
            bool sameVol = finalRoot.Length > 0 && string.Equals(finalRoot, tempRoot, StringComparison.OrdinalIgnoreCase);
            if (sameVol)
            {
                try { Directory.CreateDirectory(temp); } catch { }
                return Path.Combine(temp, Id.ToString("N") + "_" + safe + ".hizli");
            }
            return FullPath + ".hizli";
        }
    }

    [JsonIgnore] public bool PartUsesTempFolder
    {
        get
        {
            string p = PartPath;
            string temp = App.Settings?.TempFolder;
            if (string.IsNullOrWhiteSpace(temp)) return false;
            return p.StartsWith(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
    [JsonIgnore] public double Progress => Size > 0 ? Math.Min(100.0, Downloaded * 100.0 / Size) : (Status == DownloadStatus.Completed ? 100 : 0);
    [JsonIgnore] public bool IsActive => Status is DownloadStatus.Downloading or DownloadStatus.Connecting or DownloadStatus.Merging;
    [JsonIgnore] public bool IsCompleted => Status == DownloadStatus.Completed;
    [JsonIgnore] public string SizeText => Size > 0 ? ((SizeIsApprox ? "~" : "") + Format.Bytes(Size)) : "Bilinmiyor";
    [JsonIgnore] public string DownloadedText => Format.Bytes(Downloaded);
    [JsonIgnore] public string SpeedText => IsActive && Speed > 0 ? Format.Bytes((long)Speed) + "/sn" : "";
    [JsonIgnore] public string EtaText => IsActive && Speed > 1 && Size > 0 ? Format.Duration(TimeSpan.FromSeconds((Size - Downloaded) / Speed)) : "";
    [JsonIgnore] public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Sırada",
        DownloadStatus.Connecting => "Bağlanıyor",
        DownloadStatus.Downloading => Phase ?? "Veri alınıyor…",
        DownloadStatus.Paused => Size > 0 && Downloaded > 0 ? $"Duraklatıldı (%{Progress:0.0})" : "Duraklatıldı",
        DownloadStatus.Completed => "Tamamlandı",
        DownloadStatus.Merging => Phase ?? "Birleştiriliyor",
        DownloadStatus.Error => "Hata: " + (Error ?? "bilinmeyen"),
        _ => ""
    };

    public void RefreshComputed()
    {
        Raise(nameof(StatusText)); Raise(nameof(SpeedText)); Raise(nameof(EtaText)); Raise(nameof(Progress));
    }
}

public sealed class QueueSchedule
{
    public bool Enabled { get; set; }
    public TimeSpan StartTime { get; set; } = new(2, 0, 0);
    public bool OnlyOnce { get; set; }
    public DateTime? OnceDate { get; set; }
    public List<DayOfWeek> Days { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public bool StopEnabled { get; set; }
    public TimeSpan StopTime { get; set; } = new(7, 0, 0);
    public PostAction PostAction { get; set; } = PostAction.None;
    public DateTime? LastStarted { get; set; }
    public DateTime? LastStopped { get; set; }
}

public enum PostAction { None, ExitApp, Sleep, Hibernate, Shutdown, ForceShutdown }

public sealed class QueueInfo : Observable
{
    public const string MainId = "main";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    string _name = "Yeni kuyruk";
    public string Name { get => _name; set => Set(ref _name, value); }
    public int MaxParallel { get; set; } = 2;
    public int RetryCount { get; set; } = 3;
    public QueueSchedule Schedule { get; set; } = new();
    bool _running;
    [JsonIgnore] public bool Running { get => _running; set => Set(ref _running, value); }
}

public sealed class CategoryInfo
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public List<string> Sites { get; set; } = new();
}

public sealed class ServerException
{
    public string Host { get; set; } = "";
    public int Connections { get; set; } = 1;
}

public sealed class SiteLogin
{
    public string Pattern { get; set; } = "";
    public string User { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
}
