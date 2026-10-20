namespace HizliIndir.Core;

public static class Format
{
    static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };
    public static string Bytes(long b)
    {
        if (b < 0) return "?";
        double v = b; int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{b} B" : $"{v:0.##} {Units[u]}";
    }
    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 1) return "1 sn";
        if (t.TotalHours >= 24) return $"{(int)t.TotalDays} gün {t.Hours} sa";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} sa {t.Minutes} dk";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} dk {t.Seconds} sn";
        return $"{t.Seconds} sn";
    }
}
