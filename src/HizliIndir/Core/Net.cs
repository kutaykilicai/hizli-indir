using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;

namespace HizliIndir.Core;

/// <summary>Tüm indirmeler için paylaşılan HttpClient. Varsayılan sınırlamalar kaldırılmış durumda.</summary>
public static class Net
{
    static HttpClient? _client;
    static readonly object Gate = new();
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";

    public static HttpClient Client
    {
        get { lock (Gate) return _client ??= Create(App.Settings); }
    }

    public static void Reset() { lock (Gate) { _client?.Dispose(); _client = null; } }

    static HttpClient Create(AppSettings s)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 256,
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 20,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(5, s.TimeoutSeconds)),
            ResponseDrainTimeout = TimeSpan.FromSeconds(1),
            InitialHttp2StreamWindowSize = 16 * 1024 * 1024,
            SslOptions = new SslClientAuthenticationOptions(),
        };
        switch (s.Proxy)
        {
            case ProxyMode.None: handler.UseProxy = false; break;
            case ProxyMode.System: handler.UseProxy = true; handler.Proxy = WebRequest.GetSystemWebProxy(); break;
            case ProxyMode.Manual when !string.IsNullOrWhiteSpace(s.ProxyHost):
                var p = new WebProxy(s.ProxyHost.Contains("://") ? s.ProxyHost : $"http://{s.ProxyHost}:{s.ProxyPort}")
                {
                    BypassList = s.ProxyBypass.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(b => "^" + System.Text.RegularExpressions.Regex.Escape(b).Replace("\\*", ".*") + "$").ToArray(),
                };
                if (!string.IsNullOrEmpty(s.ProxyUser)) p.Credentials = new NetworkCredential(s.ProxyUser, Protect.Decrypt(s.ProxyPasswordProtected));
                handler.UseProxy = true; handler.Proxy = p; break;
        }
        var c = new HttpClient(handler, true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = s.PreferHttp2 ? HttpVersion.Version20 : HttpVersion.Version11,
            // HTTP/2 tek TCP bağlantısında çoğullar; bölümlü indirmede ayrı bağlantılar daha hızlı olduğu için varsayılan HTTP/1.1.
            DefaultVersionPolicy = s.PreferHttp2 ? HttpVersionPolicy.RequestVersionOrLower : HttpVersionPolicy.RequestVersionExact,
        };
        return c;
    }

    public static HttpRequestMessage Build(DownloadItem item, long? from = null, long? to = null, HttpMethod? method = null)
    {
        var m = method ?? (string.IsNullOrEmpty(item.PostData) ? HttpMethod.Get : HttpMethod.Post);
        var req = new HttpRequestMessage(m, item.Url);
        req.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrEmpty(item.UserAgent) ? DefaultUserAgent : item.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.AcceptEncoding.Clear();
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        if (!string.IsNullOrEmpty(item.Referer)) req.Headers.TryAddWithoutValidation("Referer", item.Referer);
        if (!string.IsNullOrEmpty(item.Cookies)) req.Headers.TryAddWithoutValidation("Cookie", item.Cookies);
        foreach (var h in item.Headers)
        {
            if (h.Key.Equals("Range", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.Remove(h.Key);
            req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        var (user, pass) = Credentials(item);
        if (!string.IsNullOrEmpty(user))
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}")));
        if (from.HasValue) req.Headers.Range = new RangeHeaderValue(from, to);
        if (m == HttpMethod.Post && item.PostData != null)
            req.Content = new StringContent(item.PostData, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        return req;
    }

    static (string? user, string? pass) Credentials(DownloadItem item)
    {
        if (!string.IsNullOrEmpty(item.AuthUser)) return (item.AuthUser, Protect.Decrypt(item.AuthPasswordProtected));
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var u)) return (null, null);
        foreach (var l in App.Settings.Logins)
            if (Wildcard.Match(l.Pattern, u.Host) || Wildcard.Match(l.Pattern, u.Host + u.AbsolutePath))
                return (l.User, Protect.Decrypt(l.PasswordProtected));
        return (null, null);
    }
}

public sealed record ProbeResult(long Size, bool Ranges, string? FileName, string? ContentType, string? ETag, string? LastModified, string FinalUrl);

public static class Probe
{
    public static async Task<ProbeResult> RunAsync(DownloadItem item, CancellationToken ct)
    {
        // Range 0-0: yalnızca başlık alır, gövdeyi indirmez. Sert zaman aşımı + tek bağlantı yedek yolu.
        int sec = Math.Clamp(App.Settings.TimeoutSeconds, 8, 60);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(sec));
        try
        {
            using var req = Net.Build(item, 0, 0);
            using var resp = await Net.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                using var req2 = Net.Build(item);
                using var resp2 = await Net.Client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return Read(resp2, false);
            }
            if (resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent)
                return Read(resp, resp.StatusCode == HttpStatusCode.PartialContent);
            resp.EnsureSuccessStatusCode();
            return Read(resp, false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException or HttpRequestException or IOException)
        {
            if (ct.IsCancellationRequested) throw;
            Log.Write($"Probe zaman aşımı/bağlantı hatası, yedek HEAD denenecek: {ex.Message}");
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(sec));
            using var head = new HttpRequestMessage(HttpMethod.Head, item.Url);
            head.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrEmpty(item.UserAgent) ? Net.DefaultUserAgent : item.UserAgent);
            if (!string.IsNullOrEmpty(item.Referer)) head.Headers.TryAddWithoutValidation("Referer", item.Referer);
            if (!string.IsNullOrEmpty(item.Cookies)) head.Headers.TryAddWithoutValidation("Cookie", item.Cookies);
            using var resp = await Net.Client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
            if (!resp.IsSuccessStatusCode)
            {
                // Son çare: Range olmadan GET başlığı
                using var get = Net.Build(item);
                using var resp2 = await Net.Client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                resp2.EnsureSuccessStatusCode();
                return Read(resp2, false);
            }
            return Read(resp, false);
        }
    }

    static ProbeResult Read(HttpResponseMessage resp, bool partial)
    {
        long size = -1;
        if (partial && resp.Content.Headers.ContentRange?.Length is long total) size = total;
        else if (resp.Content.Headers.ContentLength is long len) size = len;
        var cd = resp.Content.Headers.ContentDisposition;
        string? name = cd?.FileNameStar ?? cd?.FileName;
        if (name != null) name = name.Trim('"', ' ');
        var final = resp.RequestMessage?.RequestUri?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(name)) name = FileNames.FromUrl(final);
        return new ProbeResult(size, partial && size > 0, FileNames.Sanitize(name),
            resp.Content.Headers.ContentType?.MediaType,
            resp.Headers.ETag?.Tag,
            resp.Content.Headers.LastModified?.ToString("R"), final);
    }
}

public static class FileNames
{
    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "indirme";
        var last = Uri.UnescapeDataString(u.AbsolutePath.Split('/').LastOrDefault(x => x.Length > 0) ?? "");
        return string.IsNullOrWhiteSpace(last) ? (u.Host + ".html") : last;
    }

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "indirme";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length > 200 ? name[..150] + Path.GetExtension(name) : name;
    }

    public static string Unique(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !File.Exists(path + ".hizli")) return name;
        var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name);
        for (int i = 1; i < 10000; i++)
        {
            var n = $"{stem} ({i}){ext}";
            if (!File.Exists(Path.Combine(folder, n)) && !File.Exists(Path.Combine(folder, n + ".hizli"))) return n;
        }
        return Guid.NewGuid().ToString("N")[..8] + ext;
    }
}
