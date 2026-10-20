using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class SchedulerWindow : Window
{
    static readonly (DayOfWeek d, string n)[] DayNames =
    {
        (DayOfWeek.Monday, "Pzt"), (DayOfWeek.Tuesday, "Sal"), (DayOfWeek.Wednesday, "Çar"), (DayOfWeek.Thursday, "Per"),
        (DayOfWeek.Friday, "Cum"), (DayOfWeek.Saturday, "Cmt"), (DayOfWeek.Sunday, "Paz"),
    };
    readonly Dictionary<DayOfWeek, CheckBox> _days = new();
    QueueInfo? _current;

    public SchedulerWindow()
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        foreach (var (d, n) in DayNames)
        {
            var cb = new CheckBox { Content = n, Margin = new Thickness(0, 2, 14, 2) };
            _days[d] = cb; Days.Children.Add(cb);
        }
        foreach (var a in Enum.GetValues<PostAction>()) PostBox.Items.Add(new ComboBoxItem { Content = Power.Label(a), Tag = a });
        Queues.ItemsSource = App.Manager.Queues;
        Queues.SelectedIndex = 0;
    }

    void Queue_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_current != null) Store(_current);
        _current = Queues.SelectedItem as QueueInfo;
        if (_current == null) return;
        var s = _current.Schedule;
        NameBox.Text = _current.Name;
        NameBox.IsEnabled = DeleteBtn.IsEnabled = _current.Id != QueueInfo.MainId;
        EnabledBox.IsChecked = s.Enabled;
        StartBox.Text = s.StartTime.ToString(@"hh\:mm");
        (s.OnlyOnce ? OnceRadio : DailyRadio).IsChecked = true;
        OnceDate.Text = (s.OnceDate ?? DateTime.Today).ToString("dd.MM.yyyy");
        foreach (var (d, cb) in _days) cb.IsChecked = s.Days.Contains(d);
        StopBox.IsChecked = s.StopEnabled;
        StopTimeBox.Text = s.StopTime.ToString(@"hh\:mm");
        ParallelBox.Text = _current.MaxParallel.ToString();
        RetryBox.Text = _current.RetryCount.ToString();
        PostBox.SelectedItem = PostBox.Items.Cast<ComboBoxItem>().First(i => (PostAction)i.Tag == s.PostAction);
        Files.ItemsSource = App.Manager.Items.Where(i => i.QueueId == _current.Id && !i.IsCompleted).Reverse().ToList();
    }

    void Store(QueueInfo q)
    {
        var s = q.Schedule;
        if (q.Id != QueueInfo.MainId && NameBox.Text.Trim().Length > 0) q.Name = NameBox.Text.Trim();
        s.Enabled = EnabledBox.IsChecked == true;
        if (TimeSpan.TryParseExact(StartBox.Text, @"h\:mm", CultureInfo.InvariantCulture, out var st)) s.StartTime = st;
        s.OnlyOnce = OnceRadio.IsChecked == true;
        if (DateTime.TryParseExact(OnceDate.Text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var od)) s.OnceDate = od;
        s.Days = _days.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        s.StopEnabled = StopBox.IsChecked == true;
        if (TimeSpan.TryParseExact(StopTimeBox.Text, @"h\:mm", CultureInfo.InvariantCulture, out var sp)) s.StopTime = sp;
        if (int.TryParse(ParallelBox.Text, out var p)) q.MaxParallel = Math.Clamp(p, 1, 20);
        if (int.TryParse(RetryBox.Text, out var r)) q.RetryCount = Math.Clamp(r, 0, 100);
        if (PostBox.SelectedItem is ComboBoxItem ci) s.PostAction = (PostAction)ci.Tag;
        App.Manager.MarkDirty();
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ScheduleTab == null) return;
        bool files = (string)((FrameworkElement)sender).Tag == "f";
        ScheduleTab.Visibility = files ? Visibility.Collapsed : Visibility.Visible;
        FilesTab.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
    }

    void NewQueue_Click(object sender, RoutedEventArgs e)
    {
        var d = new InputDialog("Yeni kuyruk", ("Kuyruk adı", "Gece kuyruğu")) { Owner = this };
        if (d.ShowDialog() != true || d.Values[0].Length == 0) return;
        var q = new QueueInfo { Name = d.Values[0] };
        App.Manager.Queues.Add(q);
        Queues.SelectedItem = q;
    }

    void DeleteQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || _current.Id == QueueInfo.MainId) return;
        if (!Dialogs.Confirm($"{_current.Name} silinsin mi? İçindeki dosyalar ana kuyruğa taşınır.")) return;
        foreach (var i in App.Manager.Items.Where(i => i.QueueId == _current.Id)) i.QueueId = QueueInfo.MainId;
        var q = _current; _current = null;
        App.Manager.Queues.Remove(q);
        Queues.SelectedIndex = 0;
    }

    void Move(int dir)
    {
        if (Files.SelectedItem is not DownloadItem it) return;
        var items = App.Manager.Items;
        int idx = items.IndexOf(it);
        // Liste yeniden eskiye sıralı, kuyruk ise eskiden yeniye çalışır.
        int target = idx - dir;
        while (target >= 0 && target < items.Count && items[target].QueueId != it.QueueId) target -= dir;
        if (target < 0 || target >= items.Count) return;
        items.Move(idx, target);
        Queue_Changed(null!, null!);
        Files.SelectedItem = it;
    }
    void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if (Files.SelectedItem is DownloadItem it) { it.QueueId = ""; Queue_Changed(null!, null!); }
    }

    void StartNow_Click(object sender, RoutedEventArgs e) { if (_current != null) { Store(_current); App.Manager.StartQueue(_current); } }
    void StopNow_Click(object sender, RoutedEventArgs e) { if (_current != null) App.Manager.StopQueue(_current); }
    void Apply_Click(object sender, RoutedEventArgs e) { if (_current != null) Store(_current); App.Manager.Save(); }
    void Close_Click(object sender, RoutedEventArgs e) { if (_current != null) Store(_current); App.Manager.Save(); Close(); }
}
