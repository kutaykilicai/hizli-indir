using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using HizliIndir.Core;

namespace HizliIndir.UI;

public sealed class SideEntry : Observable
{
    public string Key { get; init; } = "";
    public string Label { get; set; } = "";
    public string Glyph { get; init; } = "";
    int _count;
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(CountText)); } }
    public string CountText => Count > 0 ? Count.ToString() : "";
}

public partial class MainWindow : Window
{
    readonly ICollectionView _view;
    readonly List<SideEntry> _categories = new();
    readonly List<SideEntry> _statuses = new();
    readonly System.Collections.ObjectModel.ObservableCollection<SideEntry> _queues = new();
    string _side = "cat:Tümü";
    string _filter = "all";
    bool _syncingSide;

    public MainWindow()
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _view = CollectionViewSource.GetDefaultView(App.Manager.Items);
        _view.Filter = Filter;
        List.ItemsSource = _view;

        _categories.Add(new SideEntry { Key = "cat:Tümü", Label = "Tümü", Glyph = "\uE8B7" });
        foreach (var c in App.Settings.Categories) _categories.Add(new SideEntry { Key = "cat:" + c.Name, Label = c.Name, Glyph = Converters.Glyph(c.Name) });
        _categories.Add(new SideEntry { Key = "cat:Genel", Label = "Genel", Glyph = Converters.Glyph("Genel") });
        _statuses.Add(new SideEntry { Key = "st:incomplete", Label = "Tamamlanmamış", Glyph = "\uE896" });
        _statuses.Add(new SideEntry { Key = "st:complete", Label = "Tamamlanan", Glyph = "\uE73E" });
        CategoryList.ItemsSource = _categories;
        StatusList.ItemsSource = _statuses;
        QueueList.ItemsSource = _queues;
        SyncQueues();
        App.Manager.Queues.CollectionChanged += (_, _) => SyncQueues();
        CategoryList.SelectedIndex = 0;

        App.Manager.Items.CollectionChanged += (_, _) => UpdateCounts();
        var t = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        t.Start();
        InputBindings.Add(new KeyBinding(new Relay(() => Dialogs.AddUrl()), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => Dialogs.AddUrl(ClipboardUrl())), Key.V, ModifierKeys.Control));
        UpdateCounts();
        UpdateButtons();
        RefreshStatus();
    }

    static string? ClipboardUrl()
    {
        try { var t = Clipboard.GetText().Trim(); return Uri.TryCreate(t, UriKind.Absolute, out _) ? t : null; } catch { return null; }
    }

    void SyncQueues()
    {
        _queues.Clear();
        foreach (var q in App.Manager.Queues) _queues.Add(new SideEntry { Key = "q:" + q.Id, Label = q.Name, Glyph = q.Id == QueueInfo.MainId ? "\uE8FD" : "\uE8A4" });
        UpdateCounts();
    }

    bool Filter(object o)
    {
        var i = (DownloadItem)o;
        bool side = _side switch
        {
            "cat:Tümü" => true,
            "st:incomplete" => i.Status != DownloadStatus.Completed,
            "st:complete" => i.Status == DownloadStatus.Completed,
            var k when k.StartsWith("cat:") => i.Category == k[4..],
            var k when k.StartsWith("q:") => i.QueueId == k[2..] && i.Status != DownloadStatus.Completed,
            _ => true,
        };
        if (!side) return false;
        bool f = _filter switch
        {
            "active" => i.IsActive,
            "waiting" => i.Status is DownloadStatus.Queued or DownloadStatus.Paused,
            "error" => i.Status == DownloadStatus.Error,
            _ => true,
        };
        if (!f) return false;
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Url.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void Side_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSide) return;
        var lb = (ListBox)sender;
        if (lb.SelectedItem is not SideEntry s) return;
        _syncingSide = true;
        foreach (var other in new[] { CategoryList, StatusList, QueueList }) if (other != lb) other.SelectedIndex = -1;
        _syncingSide = false;
        _side = s.Key;
        ViewTitle.Text = s.Label;
        _view.Refresh();
        UpdateEmpty();
        UpdateButtons();
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        _filter = (string)((FrameworkElement)sender).Tag;
        if (_view != null) { _view.Refresh(); UpdateEmpty(); }
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view?.Refresh();
        UpdateEmpty();
    }

    void UpdateEmpty() => EmptyState.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    void UpdateCounts()
    {
        var items = App.Manager.Items;
        foreach (var c in _categories) c.Count = c.Key == "cat:Tümü" ? items.Count : items.Count(i => "cat:" + i.Category == c.Key);
        _statuses[0].Count = items.Count(i => i.Status != DownloadStatus.Completed);
        _statuses[1].Count = items.Count(i => i.Status == DownloadStatus.Completed);
        foreach (var q in _queues) q.Count = items.Count(i => "q:" + i.QueueId == q.Key && i.Status != DownloadStatus.Completed);
        UpdateEmpty();
    }

    int _tickN;
    void Tick()
    {
        RefreshStatus();
        if (++_tickN % 3 == 0) { UpdateCounts(); if (_filter != "all") _view.Refresh(); }
        UpdateButtons();
    }

    public void RefreshStatus()
    {
        var items = App.Manager.Items;
        double speed = items.Where(i => i.IsActive).Sum(i => i.Speed);
        int active = items.Count(i => i.IsActive);
        StatusLeft.Text = active > 0 ? $"{active} indirme etkin  ·  {Format.Bytes((long)speed)}/sn" : $"{items.Count} indirme  ·  boşta";
        var s = App.Settings;
        StatusRight.Text = (s.SpeedLimitEnabled ? $"Hız sınırı: {s.SpeedLimitKBps} KB/sn  ·  " : "") + $"Bağlantı: {s.MaxConnections}" + (s.WatchClipboard ? "  ·  Pano izleniyor" : "");
        SideSpeed.Text = active > 0 ? $"{Format.Bytes((long)speed)}/sn" : "Boşta";
        LimitBtn.Foreground = s.SpeedLimitEnabled ? (System.Windows.Media.Brush)FindResource("Accent") : (System.Windows.Media.Brush)FindResource("Text");
        App.Tray?.SetTooltip(active > 0 ? $"Hızlı İndir: {active} etkin, {Format.Bytes((long)speed)}/sn" : "Hızlı İndir");
    }

    IEnumerable<DownloadItem> Selected => List.SelectedItems.Cast<DownloadItem>().ToList();
    DownloadItem? Current => List.SelectedItem as DownloadItem;

    void UpdateButtons()
    {
        var sel = Selected.ToList();
        ResumeBtn.IsEnabled = sel.Any(i => !i.IsActive && !i.IsCompleted);
        StopBtn.IsEnabled = sel.Any(i => i.IsActive || i.Status == DownloadStatus.Queued);
        DeleteBtn.IsEnabled = sel.Count > 0;
        var q = CurrentQueue();
        QueueBtn.Content = q.Running ? "Kuyruğu durdur" : "Kuyruğu başlat";
        QueueBtn.Tag = q.Running ? "\uE71A" : "\uE8FD";
    }

    QueueInfo CurrentQueue()
    {
        var id = _side.StartsWith("q:") ? _side[2..] : QueueInfo.MainId;
        return App.Manager.Queues.FirstOrDefault(x => x.Id == id) ?? App.Manager.Queues[0];
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    void Add_Click(object sender, RoutedEventArgs e) => Dialogs.AddUrl(ClipboardUrl());

    void Resume_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in Selected.Where(i => !i.IsActive && !i.IsCompleted))
        {
            App.Manager.Start(i);
            if (App.Settings.ShowProgressWindow && Selected.Count() == 1) ProgressWindow.ShowFor(i);
        }
    }

    void Stop_Click(object sender, RoutedEventArgs e) { foreach (var i in Selected) App.Manager.Pause(i); }
    void StopAll_Click(object sender, RoutedEventArgs e) => App.Manager.PauseAll();

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected(false);
    void DeleteWithFile_Click(object sender, RoutedEventArgs e) => DeleteSelected(true);

    void DeleteSelected(bool withFile)
    {
        var sel = Selected.ToList();
        if (sel.Count == 0) return;
        var text = withFile ? $"{sel.Count} indirme ve diskteki dosyaları silinsin mi?" : $"{sel.Count} indirme listeden kaldırılsın mı? Dosyalar diskte kalır.";
        if (!Dialogs.Confirm(text)) return;
        foreach (var i in sel) App.Manager.Remove(i, withFile);
        App.Manager.Save();
    }

    void Queue_Click(object sender, RoutedEventArgs e)
    {
        var q = CurrentQueue();
        if (q.Running) App.Manager.StopQueue(q); else App.Manager.StartQueue(q);
        UpdateButtons();
    }

    void Scheduler_Click(object sender, RoutedEventArgs e) => new SchedulerWindow { Owner = this }.ShowDialog();
    void Limit_Click(object sender, RoutedEventArgs e) { new SpeedLimitWindow { Owner = this }.ShowDialog(); RefreshStatus(); }
    void Grabber_Click(object sender, RoutedEventArgs e) => new GrabberWindow { Owner = this }.Show();
    void Batch_Click(object sender, RoutedEventArgs e) => new BatchWindow { Owner = this }.ShowDialog();
    void Settings_Click(object sender, RoutedEventArgs e) { new SettingsWindow { Owner = this }.ShowDialog(); RefreshStatus(); }

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Current is not { } i) return;
        if (i.IsCompleted)
        {
            if (i.Kind == DownloadKind.Http || i.Kind == DownloadKind.Video)
            {
                if (App.Settings.ShowCompleteDialog) new CompleteWindow(i).Show();
                else if (File.Exists(i.FullPath)) Shell.Open(i.FullPath);
                else Dialogs.Info("Dosya diskte bulunamadı: " + i.FullPath);
            }
            else if (File.Exists(i.FullPath)) Shell.Open(i.FullPath);
            return;
        }
        ProgressWindow.ShowFor(i);
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) DeleteSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        else if (e.Key == Key.Enter) List_DoubleClick(sender, null!);
    }

    void List_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent("UniformResourceLocatorW") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void List_Drop(object sender, DragEventArgs e)
    {
        var text = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string;
        if (text != null && Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)) Dialogs.AddUrl(text.Trim());
    }

    void ItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        MoveMenu.Items.Clear();
        foreach (var q in App.Manager.Queues)
        {
            var mi = new MenuItem { Header = q.Name };
            mi.Click += (_, _) => { foreach (var i in Selected) i.QueueId = q.Id; App.Manager.MarkDirty(); UpdateCounts(); _view.Refresh(); };
            MoveMenu.Items.Add(mi);
        }
    }

    void Open_Click(object sender, RoutedEventArgs e) { if (Current is { IsCompleted: true } i) Shell.Open(i.FullPath); }
    void Reveal_Click(object sender, RoutedEventArgs e) { if (Current is { } i) Shell.Reveal(i.IsCompleted ? i.FullPath : i.PartPath); }
    void Progress_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } i) return;
        if (i.IsCompleted)
        {
            if (App.Settings.ShowCompleteDialog) new CompleteWindow(i).Show();
            else ProgressWindow.ShowFor(i);
            return;
        }
        ProgressWindow.ShowFor(i);
    }
    void Redownload_Click(object sender, RoutedEventArgs e) { foreach (var i in Selected) App.Manager.Redownload(i); }
    void CopyUrl_Click(object sender, RoutedEventArgs e) { if (Current is { } i) Clipboard.SetText(i.PageUrl ?? i.Url); }
    void Properties_Click(object sender, RoutedEventArgs e) { if (Current is { } i) new DownloadInfoWindow(i, probe: false, existing: true) { Owner = this }.ShowDialog(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            App.Manager.Save();
            return;
        }
        e.Cancel = true;
        App.Quit();
    }
}

public sealed class Relay : ICommand
{
    readonly Action _a;
    public Relay(Action a) { _a = a; }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _a();
}
