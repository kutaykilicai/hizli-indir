using System.Text.Json.Nodes;
using HizliIndir.Core;

namespace HizliIndir.UI;

/// <summary>Tarayıcı eklentisinden gelen mesajları işler.</summary>
public static class Bridge
{
    public static Task<JsonObject> Handle(JsonObject msg)
    {
        var type = (string?)msg["type"] ?? "";
        switch (type)
        {
            case "ping":
            case "config":
                return Task.FromResult(Config());
            case "show":
                App.ShowMain();
                return Ok();
            case "pause":
            case "pause-all":
                {
                    foreach (var i in App.Manager.Items.Where(x => x.IsActive).ToList())
                        App.Manager.Pause(i);
                    return Ok();
                }
            case "resume":
                {
                    var id = (string?)msg["id"];
                    if (!string.IsNullOrEmpty(id) && Guid.TryParse(id, out var g))
                    {
                        var item = App.Manager.Items.FirstOrDefault(x => x.Id == g);
                        if (item != null) App.Manager.Start(item);
                    }
                    else
                    {
                        foreach (var i in App.Manager.Items.Where(x => x.Status == DownloadStatus.Paused).Take(1).ToList())
                            App.Manager.Start(i);
                    }
                    return Ok();
                }
            case "download":
                {
                    var item = FromMessage(msg);
                    if (item == null) return Fail("Adres geçersiz");
                    bool silent = (bool?)msg["silent"] ?? false;
                    Log.Write($"Tarayıcıdan indirme: {item.Url} (çerez: {(string.IsNullOrEmpty(item.Cookies) ? "yok" : item.Cookies.Split(';').Length + " adet")}, yönlendiren: {item.Referer ?? "yok"})");
                    if (silent || !App.Settings.ShowStartDialog)
                    {
                        var (cat, folder) = Categorizer.Resolve(item.FileName, item.Url);
                        item.Category = cat;
                        item.Folder = (string?)msg["folder"] is { Length: > 0 } f && Path.IsPathRooted(f) ? f : folder;
                        App.Manager.Add(item, true);
                        if (App.Settings.ShowProgressWindow) ProgressWindow.ShowFor(item);
                    }
                    else new DownloadInfoWindow(item, probe: true).Show();
                    return Ok();
                }
            case "video":
                {
                    var page = (string?)msg["pageUrl"] ?? (string?)msg["url"];
                    if (string.IsNullOrEmpty(page)) return Fail("Adres yok");
                    var item = new DownloadItem
                    {
                        Kind = DownloadKind.Video,
                        Url = (string?)msg["url"] ?? page,
                        PageUrl = page,
                        Referer = (string?)msg["referer"],
                        UserAgent = (string?)msg["userAgent"],
                        Cookies = (string?)msg["cookiesNetscape"],
                        VideoTitle = (string?)msg["title"],
                        VideoFormat = (string?)msg["quality"],
                        FileName = FileNames.Sanitize((string?)msg["title"] ?? "video"),
                        Category = "Video",
                        Folder = Categorizer.FolderOf("Video"),
                    };
                    Log.Write($"Tarayıcıdan video: {page} kalite={item.VideoFormat}");
                    bool quick = (bool?)msg["quick"] ?? false;
                    if (quick)
                    {
                        App.Manager.Add(item, true);
                        App.Tray?.Notify("Video indiriliyor", item.VideoTitle ?? page);
                    }
                    else new VideoWindow(item).Show();
                    return Ok();
                }
            case "links":
                {
                    var arr = msg["items"] as JsonArray;
                    if (arr == null || arr.Count == 0) return Fail("Bağlantı yok");
                    var links = arr.OfType<JsonObject>().Select(o => (string?)o["url"]).Where(u => !string.IsNullOrEmpty(u))
                        .Distinct().Select(u => new GrabbedLink(u!, FileNames.FromUrl(u!), Path.GetExtension(FileNames.FromUrl(u!)).TrimStart('.'))).ToList();
                    var w = new GrabberWindow(links, (string?)msg["referer"], (string?)msg["cookies"], (string?)msg["userAgent"]);
                    w.Show();
                    return Ok();
                }
        }
        return Fail("Bilinmeyen istek: " + type);
    }

    static JsonObject Config()
    {
        var s = App.Settings;
        return new JsonObject
        {
            ["ok"] = true,
            ["app"] = "Hızlı İndir",
            ["version"] = "1.0.0",
            ["capture"] = new JsonArray(s.CaptureExtensions.Select(x => (JsonNode)x).ToArray()),
            ["skipSites"] = new JsonArray(s.SkipSites.Select(x => (JsonNode)x).ToArray()),
            ["noPanelSites"] = new JsonArray(s.NoVideoPanelSites.Select(x => (JsonNode)x).ToArray()),
            ["minBytes"] = s.MinCaptureBytes,
            ["defaultQuality"] = s.DefaultVideoQuality,
        };
    }

    static DownloadItem? FromMessage(JsonObject m)
    {
        var url = (string?)m["url"];
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _)) return null;
        var item = new DownloadItem
        {
            Url = url,
            Referer = (string?)m["referer"],
            PageUrl = (string?)m["pageUrl"],
            Cookies = (string?)m["cookies"],
            UserAgent = (string?)m["userAgent"],
            PostData = (string?)m["postData"],
            FileName = FileNames.Sanitize((string?)m["filename"] is { Length: > 0 } f ? Path.GetFileName(f) : FileNames.FromUrl(url)),
        };
        if ((long?)m["size"] is long sz && sz > 0) item.Size = sz;
        if ((int?)m["connections"] is int c && c > 0) item.MaxConnections = Math.Clamp(c, 1, 32);
        if (m["headers"] is JsonObject h)
            foreach (var kv in h) if (kv.Value != null) item.Headers[kv.Key] = kv.Value.ToString();
        return item;
    }

    static Task<JsonObject> Ok() => Task.FromResult(new JsonObject { ["ok"] = true });
    static Task<JsonObject> Fail(string e) => Task.FromResult(new JsonObject { ["ok"] = false, ["error"] = e });
}
