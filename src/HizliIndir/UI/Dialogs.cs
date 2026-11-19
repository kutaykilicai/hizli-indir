using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public static class Dialogs
{
    public static void Info(string text, string title = "Hızlı İndir") =>
        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string text, string title = "Hızlı İndir") =>
        MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void AddUrl(string? url = null)
    {
        var w = new AddUrlWindow(url);
        w.Owner = App.MainWin?.IsVisible == true ? App.MainWin : null;
        w.Show();
        w.Activate();
    }

    public static void AddTorrent(string? magnet, byte[]? data)
    {
        var w = new AddTorrentWindow(magnet, data);
        w.Show();
        w.Activate();
    }

    public static void AddTorrentFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > TorrentMeta.MaxBytes) { Info("Torrent dosyası çok büyük."); return; }
            var data = File.ReadAllBytes(path);
            try { TorrentMeta.Parse(data); }
            catch (InvalidDataException ex)
            {
                // Eski sürümün bölümlü indirmesiyle yarım kalmış bir .torrent ise kaynağından tek istekle yeniden al.
                var full = Path.GetFullPath(path);
                var source = App.Manager.Items.FirstOrDefault(i => i.Kind == DownloadKind.Http && string.Equals(i.FullPath, full, StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(i.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https");
                if (source == null) throw;
                Log.Write($"Torrent dosyası bozuk ({ex.Message}); kaynağından yeniden alınıyor");
                AddTorrentFromWeb(source);
                return;
            }
            AddTorrent(null, data);
        }
        catch (Exception ex) { Info("Torrent dosyası açılamadı: " + ex.Message); }
    }

    /// <summary>Web'deki .torrent dosyasını (çerez ve yönlendiren bilgisiyle) indirip torrent penceresini açar.</summary>
    public static async void AddTorrentFromWeb(DownloadItem source)
    {
        try
        {
            using var req = Net.Build(source);
            var meta = await TorrentFetch.DownloadAsync(Net.Client, req);
            AddTorrent(null, meta.Bytes);
        }
        catch (Exception ex)
        {
            Log.Write("Torrent indirilemedi: " + ex.Message);
            Info("Torrent dosyası alınamadı: " + ex.Message);
        }
    }

    public static void Prepare(Window w)
    {
        w.Style = (Style)Application.Current.FindResource("BaseWindow");
        Interop.Backdrop.Apply(w);
        w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
    }
}
