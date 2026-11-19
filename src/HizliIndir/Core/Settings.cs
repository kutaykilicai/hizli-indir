using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HizliIndir.Core;

public enum ProxyMode { None, System, Manual }
public enum DuplicateMode { Rename, Overwrite, Ask }
public enum ThemeMode { System, Light, Dark }

public sealed class AppSettings
{
    // Genel
    public bool LaunchOnStartup { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool WatchClipboard { get; set; } = false;
    public bool ShowStartDialog { get; set; } = true;
    public bool ShowCompleteDialog { get; set; } = true;
    public bool ShowProgressWindow { get; set; } = true;
    /// <summary>İlerleme penceresinde bağlantı ayrıntılarının açık olup olmadığı.</summary>
    public bool ProgressDetailsExpanded { get; set; } = false;
    public bool Notifications { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    // Bağlantı
    public int MaxConnections { get; set; } = 16;
    public List<ServerException> ServerExceptions { get; set; } = new();
    public bool DownloadLimitEnabled { get; set; }
    public int DownloadLimitMegabytes { get; set; } = 200;
    public int DownloadLimitHours { get; set; } = 5;
    public bool DownloadLimitWarn { get; set; } = true;
    public int MaxParallelDownloads { get; set; } = 4;
    public int RetryCount { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 30;
    public bool PreferHttp2 { get; set; } = false;

    // Hız sınırı (0 = sınırsız)
    public bool SpeedLimitEnabled { get; set; }
    public int SpeedLimitKBps { get; set; } = 1024;
    public bool SpeedLimitOnStartup { get; set; }

    // Kaydetme
    public string BaseFolder { get; set; } = DefaultBase();
    /// <summary>Kısmi dosyalar (.hizli). Aynı sürücüde bitiş File.Move; IDM artıkları dokunulmaz.</summary>
    public string TempFolder { get; set; } = DefaultTemp();
    public bool RememberLastFolder { get; set; } = true;
    public string? LastFolder { get; set; }
    public DuplicateMode Duplicates { get; set; } = DuplicateMode.Rename;
    public List<CategoryInfo> Categories { get; set; } = new();

    // Dosya türleri: tarayıcıdan otomatik yakalanacak uzantılar
    public List<string> CaptureExtensions { get; set; } = DefaultCapture();
    public List<string> SkipSites { get; set; } = new() { "*.update.microsoft.com", "download.windowsupdate.com" };
    public List<string> NoVideoPanelSites { get; set; } = new() { "*.gstatic.com", "media.tenor.co" };
    public long MinCaptureBytes { get; set; } = 0;

    // Vekil sunucu
    public ProxyMode Proxy { get; set; } = ProxyMode.System;
    public string ProxyHost { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUser { get; set; } = "";
    public string ProxyPasswordProtected { get; set; } = "";
    public string ProxyBypass { get; set; } = "localhost;127.*;10.*;192.168.*";

    // Siteler
    public List<SiteLogin> Logins { get; set; } = new();

    // Video
    public string DefaultVideoQuality { get; set; } = "1080";
    public int VideoFragments { get; set; } = 16;
    public bool AutoUpdateYtDlp { get; set; } = true;
    public int TorrentPort { get; set; } = 52413;
    public bool TorrentSeedWhenDone { get; set; }
    public DateTime? LastYtDlpUpdate { get; set; }

    // Virüs denetimi
    public bool VirusScanEnabled { get; set; }
    public string VirusScanner { get; set; } = @"C:\Program Files\Windows Defender\MpCmdRun.exe";
    public string VirusScannerArgs { get; set; } = "-Scan -ScanType 3 -File \"[DOSYA]\"";

    // Sesler
    public string? SoundComplete { get; set; }
    public string? SoundFailed { get; set; }

    public bool FirstRunDone { get; set; }
    /// <summary>IDM klasör içe aktarma sürümü; artırılınca yeniden uygulanır.</summary>
    public int IdmImportVersion { get; set; }

    static string Downloads() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Varsayılan geçici klasör: %USERPROFILE%\Downloads\HizliIndir-Gecici. Ayarlardan değiştirilebilir.</summary>
    public static string DefaultTemp()
    {
        var d = Path.Combine(Downloads(), "HizliIndir-Gecici");
        try { Directory.CreateDirectory(d); return d; } catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HizliIndir", "parcalar");
    }

    /// <summary>Varsayılan indirme klasörü: %USERPROFILE%\Downloads. Ayarlardan değiştirilebilir.</summary>
    public static string DefaultBase() => Downloads();

    public static List<string> DefaultCapture() => ("3gp 7z aac ace aif apk arj asf avi bin bz2 deb dmg epub exe flac gz gzip img iso jar lzh m4a m4v mkv mov mp3 mp4 mpa mpe mpeg mpg msi msix msu ogg ogv opus pdf ppt pptx doc docx xls xlsx qt r0* r1* rar rm rmvb rpm sit sitx tar tgz tif tiff torrent wav webm wma wmv xz z zip zst").Split(' ').ToList();

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(TempFolder)) TempFolder = DefaultTemp();
        try { Directory.CreateDirectory(TempFolder); } catch { }

        if (Categories.Count == 0)
        {
            Categories = new()
            {
                new() { Name = "Sıkıştırılmış", Folder = Path.Combine(BaseFolder, "Sıkıştırılmış"), Extensions = "zip rar 7z tar gz tgz bz2 xz zst arj lzh ace cab r0* r1* sit sitx sea".Split(' ').ToList() },
                new() { Name = "Belgeler", Folder = Path.Combine(BaseFolder, "Belgeler"), Extensions = "pdf doc docx xls xlsx ppt pptx pps odt ods odp rtf txt csv epub".Split(' ').ToList() },
                new() { Name = "Müzik", Folder = Path.Combine(BaseFolder, "Müzik"), Extensions = "mp3 wav wma aac m4a flac ogg opus aif mpa ra".Split(' ').ToList() },
                new() { Name = "Programlar", Folder = Path.Combine(BaseFolder, "Programlar"), Extensions = "exe msi msix msixbundle appx apk jar deb rpm dmg iso img bin".Split(' ').ToList() },
                new() { Name = "Video", Folder = Path.Combine(BaseFolder, "Video"), Extensions = "mp4 mkv avi mov wmv webm flv m4v mpg mpeg mpe 3gp ts ogv asf rm rmvb".Split(' ').ToList() },
            };
        }
        if (!Categories.Any(c => c.Name == "Torrent"))
            Categories.Add(new() { Name = "Torrent", Folder = Path.Combine(BaseFolder, "Torrent"), Extensions = new() { "torrent" } });
    }

    public string GeneralFolder => Path.Combine(BaseFolder, "Genel");

    public int ConnectionsFor(string host)
    {
        foreach (var ex in ServerExceptions)
            if (Wildcard.Match(ex.Host, host)) return Math.Clamp(ex.Connections, 1, 32);
        return Math.Clamp(MaxConnections, 1, 32);
    }
}

public static class Wildcard
{
    public static bool Match(string pattern, string text)
    {
        pattern = pattern.Trim().ToLowerInvariant();
        text = text.ToLowerInvariant();
        if (pattern.Length == 0) return false;
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, rx);
    }
}

public static class Paths
{
    public static string Data { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HizliIndir");
    public static string Tools => Path.Combine(Data, "tools");
    public static string SettingsFile => Path.Combine(Data, "ayarlar.json");
    public static string DownloadsFile => Path.Combine(Data, "indirmeler.json");
    public static string QueuesFile => Path.Combine(Data, "kuyruklar.json");
    public static string LogFile => Path.Combine(Data, "gunluk.txt");
    public static string Temp => Path.Combine(Data, "gecici");
    static Paths() { Directory.CreateDirectory(Data); Directory.CreateDirectory(Tools); Directory.CreateDirectory(Temp); }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions TolerantOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var text = File.ReadAllText(path).TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(text)) return fallback();
            return JsonSerializer.Deserialize<T>(text, Options) ?? fallback();
        }
        catch (Exception ex) { Log.Write("Okunamadı: " + path + " " + ex.Message); }
        return fallback();
    }

    /// <summary>indirmeler.json: şema değişince göç et, asla sessizce silme.</summary>
    public static List<DownloadItem> LoadDownloads(string path)
    {
        if (!File.Exists(path)) return new();
        string text;
        try { text = File.ReadAllText(path).TrimStart('\uFEFF'); }
        catch (Exception ex) { Log.Write("Liste okunamadı: " + ex.Message); return new(); }
        if (string.IsNullOrWhiteSpace(text)) return new();

        try
        {
            var list = JsonSerializer.Deserialize<List<DownloadItem>>(text, Options);
            if (list != null) return list;
        }
        catch (Exception ex) { Log.Write("Liste strict okuma: " + ex.Message); }

        try
        {
            var bak = path + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(path, bak, true);
            Log.Write("Liste yedeklendi (göç): " + bak);
        }
        catch (Exception ex) { Log.Write("Liste yedeklenemedi: " + ex.Message); }

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("Items", out var items) || root.TryGetProperty("items", out items))
                    root = items;
                else if (root.TryGetProperty("Downloads", out var d) || root.TryGetProperty("downloads", out d))
                    root = d;
            }
            if (root.ValueKind != JsonValueKind.Array)
            {
                Log.Write("Liste kökü dizi değil; yedek korundu, boş liste");
                return new();
            }
            var list = new List<DownloadItem>();
            int skipped = 0;
            foreach (var el in root.EnumerateArray())
            {
                try
                {
                    var item = JsonSerializer.Deserialize<DownloadItem>(el.GetRawText(), TolerantOptions);
                    if (item != null)
                    {
                        if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                        if (string.IsNullOrWhiteSpace(item.FileName)) item.FileName = "indirme";
                        if (string.IsNullOrWhiteSpace(item.Folder)) item.Folder = App.Settings?.BaseFolder ?? Paths.Data;
                        list.Add(item);
                        continue;
                    }
                }
                catch { }
                skipped++;
            }
            Log.Write($"Liste göç ettirildi: {list.Count} kayıt, {skipped} atlandı");
            return list;
        }
        catch (Exception ex)
        {
            Log.Write("Liste göçü başarısız: " + ex.Message);
            return new();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, true);
    }
}

public static class Log
{
    static readonly object Gate = new();
    public static void Write(string msg)
    {
        try
        {
            lock (Gate) File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
