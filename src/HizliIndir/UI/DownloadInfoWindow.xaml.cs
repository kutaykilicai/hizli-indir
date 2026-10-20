using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class DownloadInfoWindow : Window
{
    readonly DownloadItem _item;
    readonly bool _existing;
    readonly CancellationTokenSource _cts = new();
    bool _userFolder;

    public DownloadInfoWindow(DownloadItem item, bool probe, bool existing = false)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _item = item; _existing = existing;
        UrlBox.Text = item.Url;
        NameBox.Text = item.FileName;
        DescBox.Text = item.Description ?? "";
        CategoryBox.Items.Add("Genel");
        foreach (var c in App.Settings.Categories) CategoryBox.Items.Add(c.Name);
        ConnBox.Items.Add("Varsayılan");
        for (int i = 1; i <= 32; i++) ConnBox.Items.Add(i.ToString());
        ConnBox.SelectedIndex = item.MaxConnections ?? 0;
        QueueBox.ItemsSource = App.Manager.Queues;
        QueueBox.SelectedItem = App.Manager.Queues.FirstOrDefault(q => q.Id == item.QueueId) ?? App.Manager.Queues[0];
        OpenBox.IsChecked = item.OpenWhenDone;

        if (existing)
        {
            Title = "Özellikler";
            Heading.Text = item.FileName;
            CategoryBox.SelectedItem = item.Category;
            FolderBox.Text = item.Folder;
            _userFolder = true;
            LaterPanel.Visibility = Visibility.Collapsed;
            StartBtn.Content = "Kaydet";
            bool locked = item.IsActive || item.IsCompleted;
            NameBox.IsEnabled = FolderBox.IsEnabled = !locked;
            SizeText.Text = $"{item.SizeText}  ·  {item.StatusText}";
            var lines = new List<string> { $"Eklenme: {item.AddedAt:dd.MM.yyyy HH:mm:ss}" };
            if (item.CompletedAt != null) lines.Add($"Tamamlanma: {item.CompletedAt:dd.MM.yyyy HH:mm:ss}");
            if (item.AverageSpeed != null) lines.Add($"Ortalama hız: {Format.Bytes((long)item.AverageSpeed)}/sn");
            if (item.FinalizeMilliseconds != null) lines.Add($"Son bayttan bitişe: {item.FinalizeMilliseconds:0.##} ms (birleştirme yok)");
            if (!string.IsNullOrEmpty(item.Referer)) lines.Add("Yönlendiren: " + item.Referer);
            lines.Add("Devam desteği: " + (item.AcceptRanges ? "Var" : "Yok ya da bilinmiyor"));
            Details.Text = string.Join("\n", lines);
        }
        else
        {
            ApplyCategory();
            if (item.Size > 0) SizeText.Text = Format.Bytes(item.Size);
        }
        TypeGlyph.Text = Converters.Glyph(CategoryBox.SelectedItem as string);
        if (probe) Loaded += async (_, _) => await ProbeAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    void ApplyCategory()
    {
        var (cat, folder) = Categorizer.Resolve(NameBox.Text, _item.Url);
        CategoryBox.SelectedItem = cat;
        if (!_userFolder) FolderBox.Text = folder;
        TypeGlyph.Text = Converters.Glyph(cat);
    }

    async Task ProbeAsync()
    {
        try
        {
            var r = await Probe.RunAsync(_item, _cts.Token);
            _item.Size = r.Size;
            _item.AcceptRanges = r.Ranges;
            if (!string.IsNullOrWhiteSpace(r.FileName) && (NameBox.Text == FileNames.Sanitize(FileNames.FromUrl(_item.Url)) || string.IsNullOrWhiteSpace(NameBox.Text)))
            {
                NameBox.Text = r.FileName;
                ApplyCategory();
            }
            var conns = App.Settings.ConnectionsFor(new Uri(_item.Url).Host);
            SizeText.Text = (r.Size > 0 ? Format.Bytes(r.Size) : "Boyut bilinmiyor") + "  ·  " +
                            (r.Ranges ? $"Devam destekleniyor, {conns} bağlantı" : "Sunucu devam desteklemiyor, tek bağlantı");
            if (r.ContentType != null) Details.Text = "Tür: " + r.ContentType;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SizeText.Text = "Dosya bilgisi alınamadı: " + ex.Message;
        }
    }

    void Category_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CategoryBox.SelectedItem is string c)
        {
            TypeGlyph.Text = Converters.Glyph(c);
            if (!_existing && IsLoaded) FolderBox.Text = c == "Genel" ? App.Settings.GeneralFolder : Categorizer.FolderOf(c);
        }
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : App.Settings.BaseFolder };
        if (d.ShowDialog(this) == true) { FolderBox.Text = d.FolderName; _userFolder = true; }
    }

    bool Commit()
    {
        var name = FileNames.Sanitize(NameBox.Text);
        if (string.IsNullOrWhiteSpace(FolderBox.Text)) { Dialogs.Info("Kayıt yeri seçin."); return false; }
        _item.Url = UrlBox.Text.Trim();
        _item.FileName = name;
        _item.Folder = FolderBox.Text.Trim();
        _item.Category = CategoryBox.SelectedItem as string ?? "Genel";
        _item.Description = DescBox.Text;
        _item.MaxConnections = ConnBox.SelectedIndex > 0 ? ConnBox.SelectedIndex : null;
        _item.OpenWhenDone = OpenBox.IsChecked == true;
        if (RememberBox.IsChecked == true)
        {
            var cat = App.Settings.Categories.FirstOrDefault(c => c.Name == _item.Category);
            if (cat != null) cat.Folder = _item.Folder; else App.Settings.LastFolder = _item.Folder;
            App.SaveSettings();
        }
        return true;
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        if (_existing) { App.Manager.MarkDirty(); Close(); return; }
        _item.QueueId = QueueInfo.MainId;
        App.Manager.Add(_item, true);
        if (App.Settings.ShowProgressWindow) ProgressWindow.ShowFor(_item);
        Close();
    }

    void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        _item.QueueId = (QueueBox.SelectedItem as QueueInfo)?.Id ?? QueueInfo.MainId;
        _item.Status = DownloadStatus.Queued;
        App.Manager.Add(_item, false);
        App.Manager.PumpQueues();
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
