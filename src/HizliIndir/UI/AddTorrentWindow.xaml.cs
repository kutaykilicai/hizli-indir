using System.ComponentModel;
using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public sealed class TorrentFileRow : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public string SizeText => Format.Bytes(Size);
    bool _selected = true;
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>.torrent dosyası ya da magnet bağlantısından indirme ekler: dosya seçimi ve kayıt yeri.</summary>
public partial class AddTorrentWindow : Window
{
    readonly CancellationTokenSource _cts = new();
    readonly string? _magnet;
    TorrentMeta? _meta;
    List<TorrentFileRow> _rows = new();

    public AddTorrentWindow(string? magnet, byte[]? data)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _magnet = magnet?.Trim();
        FolderBox.Text = Categorizer.FolderOf("Torrent");
        Closed += (_, _) => _cts.Cancel();
        Loaded += async (_, _) => await LoadAsync(data);
    }

    async Task LoadAsync(byte[]? data)
    {
        try
        {
            TorrentMeta meta;
            if (data != null) meta = TorrentMeta.Parse(data);
            else
            {
                SubText.Text = "Magnet bağlantısı: dosya bilgisi alınıyor";
                var engine = await TorrentJob.SharedAsync();
                meta = await engine.FetchMetadataAsync(_magnet!, TimeSpan.FromMinutes(3), _cts.Token);
            }
            Show(meta);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Write("Torrent açılamadı: " + ex.Message);
            Waiting.Visibility = Visibility.Collapsed;
            SubText.Text = "Torrent açılamadı";
            ErrorText.Text = ex.Message;
        }
    }

    void Show(TorrentMeta meta)
    {
        _meta = meta;
        var existing = App.Manager.Items.FirstOrDefault(i => i.InfoHash == meta.InfoHash);
        Heading.Text = meta.Name;
        _rows = meta.Torrent.Files.Select((f, i) => new TorrentFileRow { Index = i, Path = f.Path, Size = f.Length }).ToList();
        FileList.ItemsSource = _rows;
        Waiting.Visibility = Visibility.Collapsed;
        FilesPanel.Visibility = Visibility.Visible;
        UpdateSelection();
        if (existing != null)
        {
            ErrorText.Text = "Bu torrent listede zaten var.";
            StartBtn.IsEnabled = false;
        }
    }

    void UpdateSelection()
    {
        var sel = _rows.Where(r => r.Selected).ToList();
        SelText.Text = $"{sel.Count}/{_rows.Count} dosya, {Format.Bytes(sel.Sum(r => r.Size))}";
        SubText.Text = $"{_rows.Count} dosya, toplam {Format.Bytes(_rows.Sum(r => r.Size))}";
        AllBox.IsChecked = sel.Count == _rows.Count ? true : sel.Count == 0 ? false : null;
        StartBtn.IsEnabled = sel.Count > 0 && string.IsNullOrEmpty(ErrorText.Text);
    }

    void All_Click(object sender, RoutedEventArgs e)
    {
        bool on = AllBox.IsChecked != false;
        foreach (var r in _rows) r.Selected = on;
        UpdateSelection();
    }

    void File_Click(object sender, RoutedEventArgs e) => UpdateSelection();

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : App.Settings.BaseFolder };
        if (d.ShowDialog(this) == true) FolderBox.Text = d.FolderName;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_meta == null) return;
        var folder = FolderBox.Text.Trim();
        if (!Path.IsPathRooted(folder)) { ErrorText.Text = "Geçerli bir kayıt yeri seçin."; return; }
        var path = TorrentJob.FileFor(_meta.InfoHash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, _meta.Bytes);
        var sel = _rows.Where(r => r.Selected).ToList();
        var item = new DownloadItem
        {
            Kind = DownloadKind.Torrent,
            Url = _magnet ?? ("magnet:?xt=urn:btih:" + _meta.InfoHash),
            InfoHash = _meta.InfoHash,
            TorrentPath = path,
            TorrentFiles = sel.Count == _rows.Count ? null : sel.Select(r => r.Index).ToList(),
            FileName = _meta.Name,
            Folder = folder,
            Category = "Torrent",
            Size = sel.Sum(r => r.Size),
        };
        Log.Write($"Torrent eklendi: {_meta.Name} ({_meta.InfoHash}), {sel.Count}/{_rows.Count} dosya");
        App.Manager.Add(item, true);
        Close();
    }
}
