namespace HizliIndir.Core;

/// <summary>
/// Dışarıdan gelen (torrent içindeki) dosya yollarını denetler. Windows kurallarını her
/// platformda aynı uygular, böylece testler Linux'ta da aynı sonucu verir.
/// </summary>
public static class SafePath
{
    static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Tek bir klasör ya da dosya adı güvenli mi?</summary>
    public static bool IsSafeName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255) return false;
        if (name is "." or "..") return false;
        if (name.IndexOfAny(Invalid) >= 0 || name.Any(c => c < 32)) return false;
        if (name.EndsWith(' ') || name.EndsWith('.')) return false;
        var stem = name.Split('.')[0].TrimEnd(' ');
        return !Reserved.Contains(stem);
    }

    /// <summary>Parçalardan oluşan göreli yolu kökün altında birleştirir; kökün dışına çıkarsa null döner.</summary>
    public static string? Combine(string root, IReadOnlyList<string> parts)
    {
        if (parts.Count == 0 || parts.Any(p => !IsSafeName(p))) return null;
        var full = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
