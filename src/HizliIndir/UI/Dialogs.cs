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
            AddTorrent(null, File.ReadAllBytes(path));
        }
        catch (Exception ex) { Info("Torrent dosyası açılamadı: " + ex.Message); }
    }

    /// <summary>Web'deki .torrent dosyasını (çerez ve yönlendiren bilgisiyle) indirip torrent penceresini açar.</summary>
    public static async void AddTorrentFromWeb(DownloadItem source)
    {
        try
        {
            using var req = Net.Build(source);
            using var res = await Net.Client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            if (res.Content.Headers.ContentLength > TorrentMeta.MaxBytes) throw new InvalidDataException("Torrent dosyası çok büyük");
            var data = await res.Content.ReadAsByteArrayAsync();
            AddTorrent(null, data);
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
