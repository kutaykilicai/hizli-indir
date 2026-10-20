namespace HizliIndir.Core;

public static class Categorizer
{
    public static CategoryInfo? Find(string fileName, string? url)
    {
        var s = App.Settings;
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        string host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";
        foreach (var c in s.Categories)
            if (c.Sites.Count > 0 && c.Sites.Any(p => Wildcard.Match(p, host)) && (c.Extensions.Count == 0 || c.Extensions.Any(e => Wildcard.Match(e, ext))))
                return c;
        foreach (var c in s.Categories)
            if (c.Sites.Count == 0 && c.Extensions.Any(e => Wildcard.Match(e, ext)))
                return c;
        return null;
    }

    public static (string category, string folder) Resolve(string fileName, string? url)
    {
        var c = Find(fileName, url);
        if (c != null) return (c.Name, c.Folder);
        var s = App.Settings;
        return ("Genel", s.RememberLastFolder && !string.IsNullOrEmpty(s.LastFolder) ? s.LastFolder! : s.GeneralFolder);
    }

    public static string FolderOf(string category)
    {
        var c = App.Settings.Categories.FirstOrDefault(x => x.Name == category);
        return c?.Folder ?? App.Settings.GeneralFolder;
    }

    public static bool IsCaptured(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext.Length > 0 && App.Settings.CaptureExtensions.Any(e => Wildcard.Match(e, ext));
    }
}
