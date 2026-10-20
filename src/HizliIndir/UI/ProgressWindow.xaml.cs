using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using HizliIndir.Core;

namespace HizliIndir.UI;

public sealed class ConnRow
{
    public string No { get; init; } = "";
    public string DownloadedKb { get; init; } = "";
    public string Info { get; init; } = "";
}

public partial class ProgressWindow : Window
{
    static readonly Dictionary<Guid, ProgressWindow> Open = new();
    readonly DownloadItem _item;
    readonly DispatcherTimer _timer;
    readonly ObservableCollection<ConnRow> _rows = new();
    bool _detailsExpanded;
    bool _limitBusy;
    bool _closingByCancel;

    public static void ShowFor(DownloadItem item)
    {
        try
        {
            if (Open.TryGetValue(item.Id, out var existing))
            {
                if (!existing.IsVisible) existing.Show();
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            var w = new ProgressWindow(item);
            Open[item.Id] = w;
            w.Show();
        }
        catch (Exception ex)
        {
            Log.Write("ProgressWindow.ShowFor: " + ex);
        }
    }

    public static void CloseFor(DownloadItem item)
    {
        if (!Open.TryGetValue(item.Id, out var w)) return;
        if (w.AfterOpen.IsChecked == true) Shell.Open(item.FullPath);
        if (w.AfterReveal.IsChecked == true) Shell.Reveal(item.FullPath);
        if (w.AfterExitApp.IsChecked == true) new CountdownWindow(PostAction.ExitApp).Show();
        else if (w.AfterSleep.IsChecked == true) new CountdownWindow(PostAction.Sleep).Show();
        else if (w.AfterShutdown.IsChecked == true) new CountdownWindow(PostAction.Shutdown).Show();
        w._closingByCancel = true; // allow close without treating as user X
        w.Close();
    }

    public static bool SuppressComplete(DownloadItem item) =>
        Open.TryGetValue(item.Id, out var w) && w.AfterShowDialog.IsChecked != true;

    ProgressWindow(DownloadItem item)
    {
        _item = item;
        _limitBusy = true; // ignore XAML init TextChanged/Checked until Loaded
        InitializeComponent();
        _limitBusy = false;
        Dialogs.Prepare(this);
        ShowInTaskbar = true;
        Topmost = false;
        UrlText.Text = item.PageUrl ?? item.Url;
        ConnList.ItemsSource = _rows;
        _detailsExpanded = App.Settings.ProgressDetailsExpanded;
        OwnLimitBox.IsEnabled = item.Kind == DownloadKind.Http;
        OwnLimitValue.IsEnabled = item.Kind == DownloadKind.Http;
        DetailsHeader.Text = item.Kind == DownloadKind.Video
            ? "Akışlar ve parça ilerlemesi (yt-dlp)"
            : "Bağlantıların başlangıç noktaları ve ilerlemesi";
        Segments.Visibility = item.Kind == DownloadKind.Http ? Visibility.Visible : Visibility.Collapsed;
        ApplyDetailsVisibility();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        _timer.Start();
        Closing += OnClosingWindow;
        Closed += (_, _) => { _timer.Stop(); Open.Remove(item.Id); };
        Refresh();
    }

    void OnClosingWindow(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // X / Alt+F4: pencereyi kapat, indirmeyi DURDURMA.
        // İptal düğmesi Pause + Close yapar (_closingByCancel).
        if (!_closingByCancel)
        {
            // sadece kayıttan düş; indirme devam eder
        }
    }

    void Refresh()
    {
        var i = _item;
        double pct = i.Progress;
        Title = i.Size > 0 ? $"%{pct:0} {i.FileName}" : i.FileName;
        StateText.Text = i.StatusText;
        SizeText.Text = i.Size > 0 ? ((i.SizeIsApprox ? "~" : "") + Format.Bytes(i.Size)) : "—";
        DoneText.Text = i.Size > 0
            ? $"{Format.Bytes(i.Downloaded)}  ({pct.ToString("0.00", CultureInfo.InvariantCulture)} %)"
            : Format.Bytes(i.Downloaded);
        SpeedText.Text = i.Speed > 0 ? Format.Bytes((long)i.Speed) + "/sn" : "—";
        EtaText.Text = string.IsNullOrEmpty(i.EtaText) ? "—" : i.EtaText;
        ResumeText.Text = i.Kind == DownloadKind.Video
            ? "Kısmen (yt-dlp)"
            : (i.Status == DownloadStatus.Connecting && i.Size <= 0 ? "—" : (i.AcceptRanges ? "Evet" : "Hayır"));
        Bar.Value = pct;
        PauseBtn.Content = i.IsActive ? "Duraklat" : "Devam";

        if (!_detailsExpanded || StatusTab.Visibility != Visibility.Visible) return;

        if (i.Kind == DownloadKind.Video) RefreshVideoRows();
        else RefreshHttpRows();
    }

    void RefreshHttpRows()
    {
        var job = App.Manager.JobOf(_item);
        var segs = job?.Snapshot() ?? SnapshotLocal(_item);
        long mapSize = _item.Size > 0 ? _item.Size : (segs.Length > 0 ? segs.Max(s => s.End + 1) : 1);
        Segments.Update(segs, Math.Max(1, mapSize));
        SyncRows(segs.Length, n =>
        {
            var s = segs[n];
            long got = Math.Max(0, Math.Min(s.Done, s.End + 1) - s.Start);
            string info = !string.IsNullOrEmpty(s.Info) ? s.Info
                : s.Finished ? "Tamamlandı" : s.Active ? "Veri alınıyor…" : "Bekliyor";
            return Row(n + 1, got, info);
        });
    }

    void RefreshVideoRows()
    {
        var job = App.Manager.VideoOf(_item);
        var streams = job?.Snapshot() ?? Array.Empty<VideoStreamState>();
        if (streams.Length == 0)
        {
            string info = _item.Status switch
            {
                DownloadStatus.Connecting => _item.Phase ?? "Bağlanıyor…",
                DownloadStatus.Merging => _item.Phase ?? "Birleştiriliyor…",
                DownloadStatus.Downloading => "Veri alınıyor…",
                DownloadStatus.Paused => "Duraklatıldı",
                DownloadStatus.Completed => "Tamamlandı",
                _ => _item.StatusText,
            };
            SyncRows(1, _ => Row(1, _item.Downloaded, info));
            return;
        }
        SyncRows(streams.Length, n =>
        {
            var s = streams[n];
            string info = string.IsNullOrEmpty(s.Info) ? s.Label : $"{s.Label}: {s.Info}";
            if (!string.IsNullOrEmpty(s.FormatId)) info += $" [{s.FormatId}]";
            return Row(s.Index > 0 ? s.Index : n + 1, s.Done, info);
        });
    }

    void SyncRows(int count, Func<int, ConnRow> factory)
    {
        while (_rows.Count > count) _rows.RemoveAt(_rows.Count - 1);
        for (int n = 0; n < count; n++)
        {
            var row = factory(n);
            if (n < _rows.Count) _rows[n] = row; else _rows.Add(row);
        }
    }

    static ConnRow Row(int no, long bytes, string info) => new()
    {
        No = no.ToString(),
        DownloadedKb = (bytes / 1024).ToString("N0", CultureInfo.GetCultureInfo("tr-TR")),
        Info = info,
    };

    static SegmentState[] SnapshotLocal(DownloadItem i)
    {
        lock (i.Segments)
            return i.Segments.Select(s => new SegmentState
            {
                Start = s.Start, End = s.End, Done = s.Done,
                Active = s.Active, Index = s.Index, Info = s.Info,
            }).ToArray();
    }

    void ApplyDetailsVisibility()
    {
        DetailsPanel.Visibility = _detailsExpanded ? Visibility.Visible : Visibility.Collapsed;
        ToggleBtn.Content = _detailsExpanded ? "<< Ayrıntıları gizle" : "Ayrıntılar >>";
        SizeToContent = SizeToContent.Height;
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (StatusTab == null) return;
        var t = (string)((FrameworkElement)sender).Tag;
        bool status = t == "0";
        StatusTab.Visibility = status ? Visibility.Visible : Visibility.Collapsed;
        LimitTab.Visibility = t == "1" ? Visibility.Visible : Visibility.Collapsed;
        AfterTab.Visibility = t == "2" ? Visibility.Visible : Visibility.Collapsed;
        ActionBar.Visibility = status ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = status && _detailsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    void Limit_Changed(object sender, RoutedEventArgs e)
    {
        if (_limitBusy || OwnLimitValue == null || OwnLimitBox == null || App.Manager == null) return;
        if (_item.Kind != DownloadKind.Http) return;
        _limitBusy = true;
        try
        {
            var text = OwnLimitValue.Text ?? "";
            long kb = long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (kb < 0) kb = 0;
            App.Manager.SetOwnLimit(_item, OwnLimitBox.IsChecked == true ? kb * 1024 : 0);
        }
        catch { /* init / teardown */ }
        finally { _limitBusy = false; }
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _detailsExpanded = !_detailsExpanded;
        App.Settings.ProgressDetailsExpanded = _detailsExpanded;
        App.SaveSettings();
        ApplyDetailsVisibility();
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_item.IsActive) App.Manager.Pause(_item); else App.Manager.Start(_item);
        Refresh();
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // İptal: indirmeyi durdur ve pencereyi kapat (dosyalar diskte kalır, listeden silinmez).
        App.Manager.Pause(_item);
        _closingByCancel = true;
        Close();
    }

    void Url_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_item.PageUrl ?? _item.Url) { UseShellExecute = true }); } catch { }
    }
}
