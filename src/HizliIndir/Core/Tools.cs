using System.Diagnostics;
using System.IO.Compression;

namespace HizliIndir.Core;

/// <summary>yt-dlp, ffmpeg ve deno'yu resmi sürümlerinden indirip %LOCALAPPDATA%\HizliIndir\tools altında tutar.</summary>
public static class Tools
{
    public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public static string YtDlp => Path.Combine(Paths.Tools, "yt-dlp.exe");
    public static string Ffmpeg => Path.Combine(Paths.Tools, "ffmpeg.exe");
    public static string Deno => Path.Combine(Paths.Tools, "deno.exe");

    static readonly SemaphoreSlim Gate = new(1, 1);
    public static bool Ready => File.Exists(YtDlp) && File.Exists(Ffmpeg) && File.Exists(Deno);

    public static async Task EnsureAsync(IProgress<string>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(YtDlp))
            {
                progress?.Report("yt-dlp indiriliyor");
                await FetchAsync(YtDlpUrl, YtDlp, ct);
            }
            if (!File.Exists(Ffmpeg))
            {
                progress?.Report("ffmpeg indiriliyor");
                await FetchZipAsync(FfmpegUrl, new[] { "ffmpeg.exe", "ffprobe.exe" }, ct);
            }
            if (!File.Exists(Deno))
            {
                progress?.Report("deno indiriliyor");
                await FetchZipAsync(DenoUrl, new[] { "deno.exe" }, ct);
            }
            var s = App.Settings;
            if (s.AutoUpdateYtDlp && (s.LastYtDlpUpdate == null || (DateTime.Now - s.LastYtDlpUpdate.Value).TotalHours > 24))
            {
                progress?.Report("yt-dlp güncelleniyor");
                await RunQuietAsync(YtDlp, "-U", ct);
                s.LastYtDlpUpdate = DateTime.Now;
                App.SaveSettings();
            }
        }
        finally { Gate.Release(); }
    }

    static async Task FetchAsync(string url, string target, CancellationToken ct)
    {
        var tmp = target + ".indiriliyor";
        using (var resp = await Net.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst, 1 << 20, ct);
        }
        File.Move(tmp, target, true);
    }

    static async Task FetchZipAsync(string url, string[] wanted, CancellationToken ct)
    {
        var zip = Path.Combine(Paths.Temp, Guid.NewGuid().ToString("N") + ".zip");
        await FetchAsync(url, zip, ct);
        try
        {
            using var a = ZipFile.OpenRead(zip);
            foreach (var e in a.Entries)
                if (wanted.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                    e.ExtractToFile(Path.Combine(Paths.Tools, e.Name), true);
        }
        finally { File.Delete(zip); }
    }

    public static async Task<string> RunQuietAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var e = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return await o + await e;
    }
}
