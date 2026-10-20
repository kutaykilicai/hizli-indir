using System.Windows;
using System.Windows.Controls;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class VideoWindow : Window
{
    readonly DownloadItem _item;
    string _quality;

    public VideoWindow(DownloadItem item)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _item = item;
        _quality = item.VideoFormat ?? App.Settings.DefaultVideoQuality;
        TitleText.Text = item.VideoTitle ?? "Video indir";
        UrlText.Text = item.PageUrl ?? item.Url;
        FolderBox.Text = item.Folder;
        foreach (var c in VideoJob.Choices)
        {
            var rb = new RadioButton { Style = (Style)FindResource("Pill"), Content = c.Label, GroupName = "q", Tag = c.Key, IsChecked = c.Key == _quality, Margin = new Thickness(0, 1, 0, 1) };
            rb.Checked += (_, _) => _quality = c.Key;
            Qualities.Children.Add(rb);
        }
        ToolsText.Text = Tools.Ready
            ? $"Video, yt-dlp ile {App.Settings.VideoFragments} parça aynı anda çekilerek indirilir; ses ve görüntü ffmpeg ile birleştirilir."
            : "İlk video indirmesinde yt-dlp, ffmpeg ve deno resmi sayfalarından indirilecek. Bu bir kereliktir.";
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : App.Settings.BaseFolder };
        if (d.ShowDialog(this) == true) FolderBox.Text = d.FolderName;
    }

    void Commit()
    {
        _item.VideoFormat = _quality;
        _item.Folder = FolderBox.Text.Trim();
        _item.Category = _quality is "mp3" or "m4a" ? "Müzik" : "Video";
        if (_quality is "mp3" or "m4a" && _item.Folder == Categorizer.FolderOf("Video")) _item.Folder = Categorizer.FolderOf("Müzik");
        if (string.IsNullOrWhiteSpace(_item.FileName)) _item.FileName = _item.VideoTitle ?? "video";
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        App.Manager.Add(_item, true);
        Close();
    }

    void Later_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        _item.Status = DownloadStatus.Queued;
        App.Manager.Add(_item, false);
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
