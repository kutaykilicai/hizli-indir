using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class AddUrlWindow : Window
{
    public static readonly string[] VideoHosts = { "youtube.com", "youtu.be", "vimeo.com", "dailymotion.com", "twitch.tv", "x.com", "twitter.com", "instagram.com", "tiktok.com", "facebook.com", "reddit.com", "bilibili.com", "soundcloud.com" };

    public AddUrlWindow(string? url)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        UrlBox.Text = url ?? "";
        Loaded += (_, _) => { UrlBox.Focus(); UrlBox.SelectAll(); };
        Url_Changed(null!, null!);
    }

    public static bool IsVideoPage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && VideoHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h))
        && !Categorizer.IsCaptured(FileNames.FromUrl(url));

    void Url_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var t = UrlBox.Text.Trim();
        bool magnet = TorrentMeta.IsMagnet(t);
        bool ok = magnet || (Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "ftp");
        OkBtn.IsEnabled = ok;
        Hint.Text = magnet ? "Magnet bağlantısı. Devam edince dosya listesi alınır." : ok && IsVideoPage(t) ? "Bu bir video sayfası. Devam edince kalite seçebilirsiniz." : "";
    }

    void Auth_Changed(object sender, RoutedEventArgs e) => AuthPanel.IsEnabled = AuthBox.IsChecked == true;

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (TorrentMeta.IsMagnet(url)) Dialogs.AddTorrent(url, null);
        else if (TorrentMeta.IsTorrentName(FileNames.FromUrl(url))) Dialogs.AddTorrentFromWeb(new DownloadItem { Url = url });
        else if (IsVideoPage(url))
        {
            var v = new DownloadItem { Kind = DownloadKind.Video, Url = url, PageUrl = url, FileName = "video", Category = "Video", Folder = Categorizer.FolderOf("Video") };
            new VideoWindow(v).Show();
        }
        else
        {
            var item = new DownloadItem { Url = url, FileName = FileNames.Sanitize(FileNames.FromUrl(url)) };
            if (AuthBox.IsChecked == true && UserBox.Text.Length > 0)
            {
                item.AuthUser = UserBox.Text;
                item.AuthPasswordProtected = Protect.Encrypt(PassBox.Password);
            }
            new DownloadInfoWindow(item, probe: true).Show();
        }
        Close();
    }
}
