using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HizliIndir.Core;

/// <summary>Panoya kopyalanan indirilebilir bağlantıları yakalar.</summary>
public sealed class ClipboardWatcher
{
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    const int WM_CLIPBOARDUPDATE = 0x031D;
    readonly HwndSource _src;
    readonly Action<string> _onUrl;
    string? _last;

    public ClipboardWatcher(Action<string> onUrl)
    {
        _onUrl = onUrl;
        _src = new HwndSource(new HwndSourceParameters("HizliIndirPano") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        _src.AddHook(Hook);
        AddClipboardFormatListener(_src.Handle);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && App.Settings.WatchClipboard)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText().Trim();
                    if (t != _last && Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "ftp")
                        && Categorizer.IsCaptured(FileNames.FromUrl(t)))
                    {
                        _last = t;
                        Application.Current.Dispatcher.BeginInvoke(() => _onUrl(t));
                    }
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }
}

/// <summary>Kuyrukları zamanlamaya göre başlatır ve durdurur.</summary>
public sealed class Scheduler
{
    readonly DownloadManager _m;
    readonly DispatcherTimer _t;
    readonly HashSet<string> _byScheduler = new();
    DateTime _lastCheck = DateTime.Now;

    public Scheduler(DownloadManager m)
    {
        _m = m;
        _t = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Check(), Application.Current.Dispatcher);
        _t.Start();
    }

    void Check()
    {
        var now = DateTime.Now;
        foreach (var q in _m.Queues)
        {
            var s = q.Schedule;
            if (!s.Enabled) continue;
            if (Crossed(_lastCheck, now, s.StartTime) && DayOk(s, now) && !q.Running)
            {
                s.LastStarted = now;
                _byScheduler.Add(q.Id);
                Log.Write($"Zamanlayıcı: {q.Name} başlatıldı");
                _m.StartQueue(q);
                if (s.OnlyOnce) s.Enabled = false;
            }
            if (s.StopEnabled && Crossed(_lastCheck, now, s.StopTime) && q.Running)
            {
                s.LastStopped = now;
                Log.Write($"Zamanlayıcı: {q.Name} durduruldu");
                _m.StopQueue(q);
            }
        }
        _lastCheck = now;
        _m.PumpQueues();
    }

    static bool DayOk(QueueSchedule s, DateTime now) =>
        s.OnlyOnce ? (s.OnceDate == null || s.OnceDate.Value.Date == now.Date) : s.Days.Contains(now.DayOfWeek);

    static bool Crossed(DateTime from, DateTime to, TimeSpan at)
    {
        var target = to.Date + at;
        if (target > to) target = target.AddDays(-1);
        return target > from && target <= to;
    }

    public void OnQueueFinished(QueueInfo q)
    {
        Log.Write($"Kuyruk bitti: {q.Name}");
        App.Tray?.Notify("Kuyruk tamamlandı", q.Name);
        if (_byScheduler.Remove(q.Id) && q.Schedule.PostAction != PostAction.None)
            new UI.CountdownWindow(q.Schedule.PostAction).Show();
    }
}

public static class Sounds
{
    public static void Complete() => Play(App.Settings.SoundComplete, SystemSounds.Asterisk);
    public static void Failed() => Play(App.Settings.SoundFailed, SystemSounds.Hand);
    static void Play(string? file, SystemSound fallback)
    {
        try
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file)) new SoundPlayer(file).Play();
            else fallback.Play();
        }
        catch { }
    }
}

/// <summary>IDM kuruluysa kategori klasörlerini, uzantı listelerini ve geçici klasörü içe aktarır.</summary>
public static class IdmImport
{
    static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Compressed"] = "Sıkıştırılmış", ["Documents"] = "Belgeler", ["Music"] = "Müzik",
        ["Programs"] = "Programlar", ["Video"] = "Video", ["General"] = "Genel", ["Genel"] = "Genel",
    };

    /// <summary>Taban, Genel ve kategori klasörlerinin var olduğundan emin olur; kayıtlı yolları değiştirmez.</summary>
    public static List<string> ApplyPreferredAndEnsureFolders(AppSettings s)
    {
        var created = new List<string>();
        TryCreate(s.BaseFolder, created);
        TryCreate(s.GeneralFolder, created);
        foreach (var c in s.Categories)
            if (!string.IsNullOrWhiteSpace(c.Folder)) TryCreate(c.Folder, created);
        return created;
    }

    static bool TryCreate(string path, List<string> created)
    {
        try
        {
            if (Directory.Exists(path)) return true;
            Directory.CreateDirectory(path);
            created.Add(path);
            return true;
        }
        catch { return false; }
    }

    public static bool TryImportFolders(AppSettings s)
    {
        try
        {
            using var main = Registry.CurrentUser.OpenSubKey(@"Software\DownloadManager");
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\DownloadManager\FoldersTree");
            if (root == null && main == null) return false;
            int n = 0;
            if (root != null)
            {
                foreach (var name in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(name);
                    if (k == null) continue;
                    var path = Text(k.GetValue("pathW")) ?? Text(k.GetValue("path"));
                    var mask = Text(k.GetValue("mask"));
                    var target = Map.TryGetValue(name, out var tr) ? tr : name;
                    if (string.Equals(target, "Genel", StringComparison.OrdinalIgnoreCase))
                    {
                        // Genel kategori listede olmayabilir; yolu taban klasörden türetilir.
                        n++;
                        continue;
                    }
                    var cat = s.Categories.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (cat == null)
                    {
                        cat = new CategoryInfo { Name = target, Folder = "", Extensions = new() };
                        s.Categories.Add(cat);
                    }
                    if (!string.IsNullOrWhiteSpace(path)) cat.Folder = path.TrimEnd('\\', ' ');
                    if (!string.IsNullOrWhiteSpace(mask))
                    {
                        var exts = mask.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).Where(x => x.Length > 0).ToList();
                        if (exts.Count > 0) cat.Extensions = exts;
                    }
                    n++;
                }
            }
            var basePath = Text(main?.GetValue("LocalPathW")) ?? Text(main?.GetValue("LocalPath"));
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                var dir = basePath.TrimEnd('\\', ' ');
                // LocalPathW genelde ...\Genel — üst klasör taban olur
                var parent = Path.GetDirectoryName(dir);
                if (parent != null && (Directory.Exists(parent) || Directory.Exists(dir)))
                    s.BaseFolder = Directory.Exists(parent) ? parent : dir;
            }
            var temp = Text(main?.GetValue("TempPath")) ?? Text(main?.GetValue("TempDirW")) ?? Text(main?.GetValue("TempDir"));
            if (!string.IsNullOrWhiteSpace(temp))
                Log.Write("IDM geçici klasörü (bilgi): " + temp.TrimEnd('\\', ' '));

            ApplyPreferredAndEnsureFolders(s);
            Log.Write($"IDM klasörleri içe aktarıldı: {n}");
            return n > 0;
        }
        catch (Exception ex) { Log.Write("IDM içe aktarma: " + ex.Message); return false; }
    }

    static string? Text(object? v) => v switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0', ' '),
        _ => null,
    };
}

public sealed record GrabbedLink(string Url, string Name, string Ext);

/// <summary>Bir sayfayı (ve isteğe bağlı olarak aynı sitedeki alt sayfaları) tarayıp dosya bağlantılarını toplar.</summary>
public static class SiteGrabber
{
    static readonly Regex Href = new("(?:href|src)\\s*=\\s*[\"']([^\"'#]+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<List<GrabbedLink>> RunAsync(string start, int depth, IReadOnlyCollection<string> exts, bool sameSite, IProgress<string> progress, CancellationToken ct)
    {
        var found = new Dictionary<string, GrabbedLink>();
        var seen = new HashSet<string>();
        var frontier = new List<string> { start };
        var baseHost = new Uri(start).Host;
        for (int level = 0; level <= depth && frontier.Count > 0; level++)
        {
            var next = new List<string>();
            foreach (var page in frontier)
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(page) || seen.Count > 400) continue;
                progress.Report($"Taranıyor: {page}");
                string html;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, page);
                    req.Headers.TryAddWithoutValidation("User-Agent", Net.DefaultUserAgent);
                    using var resp = await Net.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (resp.Content.Headers.ContentType?.MediaType?.Contains("html") != true) continue;
                    html = await resp.Content.ReadAsStringAsync(ct);
                }
                catch { continue; }
                foreach (Match m in Href.Matches(html))
                {
                    if (!Uri.TryCreate(new Uri(page), System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), out var u)) continue;
                    if (u.Scheme != "http" && u.Scheme != "https") continue;
                    var name = FileNames.FromUrl(u.ToString());
                    var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                    if (ext.Length > 0 && exts.Any(e => Wildcard.Match(e, ext)))
                        found.TryAdd(u.ToString(), new GrabbedLink(u.ToString(), name, ext));
                    else if (level < depth && (!sameSite || u.Host.EndsWith(baseHost)) && (ext is "" or "html" or "htm" or "php" or "asp" or "aspx"))
                        next.Add(u.GetLeftPart(UriPartial.Query));
                }
            }
            frontier = next;
        }
        return found.Values.ToList();
    }
}

public static class BatchPattern
{
    /// <summary>"https://site/img*.jpg" kalıbındaki yıldızı from..to aralığıyla doldurur.</summary>
    public static List<string> Expand(string pattern, int from, int to, int width, bool letters)
    {
        var list = new List<string>();
        if (!pattern.Contains('*')) return list;
        if (letters)
        {
            for (char c = (char)from; c <= (char)to; c++) list.Add(pattern.Replace("*", c.ToString()));
        }
        else
        {
            for (int i = from; i <= to && list.Count < 10000; i++) list.Add(pattern.Replace("*", i.ToString().PadLeft(width, '0')));
        }
        return list;
    }
}
