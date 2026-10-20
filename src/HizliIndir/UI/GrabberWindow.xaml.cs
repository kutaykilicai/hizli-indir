using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public sealed class LinkRow : Observable
{
    bool _sel = true;
    public bool Selected { get => _sel; set => Set(ref _sel, value); }
    public string Url { get; init; } = "";
    public string Name { get; init; } = "";
    public string Ext { get; init; } = "";
}

public partial class GrabberWindow : Window
{
    readonly List<LinkRow> _rows = new();
    readonly string? _referer, _cookies, _ua;
    CancellationTokenSource? _cts;

    public GrabberWindow() : this(null, null, null, null) { }

    public GrabberWindow(List<GrabbedLink>? links, string? referer, string? cookies, string? ua)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _referer = referer; _cookies = cookies; _ua = ua;
        for (int i = 0; i <= 3; i++) DepthBox.Items.Add(i);
        DepthBox.SelectedIndex = 1;
        ExtBox.Text = string.Join(" ", App.Settings.CaptureExtensions.Take(40));
        QueueBox.ItemsSource = App.Manager.Queues;
        QueueBox.SelectedIndex = 0;
        if (links != null)
        {
            Head.Text = "Bağlantıları indir";
            Sub.Text = "Sayfadaki bağlantılardan indirmek istediklerinizi seçin.";
            ScanCard.Visibility = Visibility.Collapsed;
            Fill(links);
        }
        Closed += (_, _) => _cts?.Cancel();
    }

    void Fill(IEnumerable<GrabbedLink> links)
    {
        _rows.Clear();
        _rows.AddRange(links.Select(l => new LinkRow { Url = l.Url, Name = l.Name, Ext = l.Ext }));
        Results.ItemsSource = null; Results.ItemsSource = _rows;
        Status.Text = $"{_rows.Count} dosya bulundu";
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var u)) { Dialogs.Info("Geçerli bir adres girin."); return; }
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        ScanBtn.IsEnabled = false;
        try
        {
            var exts = ExtBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimStart('.').ToLowerInvariant()).ToList();
            var res = await SiteGrabber.RunAsync(u.ToString(), (int)DepthBox.SelectedItem, exts, SameSite.IsChecked == true, new Progress<string>(m => Status.Text = m), _cts.Token);
            Fill(res);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = "Tarama başarısız: " + ex.Message; }
        ScanBtn.IsEnabled = true;
    }

    void All_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = true; }
    void None_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = false; }

    void AddAll(bool start)
    {
        var q = (QueueBox.SelectedItem as QueueInfo) ?? App.Manager.Queues[0];
        int n = 0;
        foreach (var r in _rows.Where(r => r.Selected))
        {
            var name = FileNames.Sanitize(r.Name);
            var (cat, folder) = Categorizer.Resolve(name, r.Url);
            var item = new DownloadItem { Url = r.Url, FileName = name, Category = cat, Folder = folder, Referer = _referer ?? UrlBox.Text, Cookies = _cookies, UserAgent = _ua, QueueId = q.Id, Status = DownloadStatus.Queued };
            App.Manager.Add(item, false);
            n++;
        }
        if (start) App.Manager.StartQueue(q);
        App.Tray?.Notify("Kuyruğa eklendi", $"{n} dosya: {q.Name}");
        Close();
    }

    void Queue_Click(object sender, RoutedEventArgs e) => AddAll(false);
    void Download_Click(object sender, RoutedEventArgs e) => AddAll(true);
}
