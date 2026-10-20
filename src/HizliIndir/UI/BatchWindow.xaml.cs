using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class BatchWindow : Window
{
    List<string> _urls = new();
    public BatchWindow()
    {
        InitializeComponent();
        Dialogs.Prepare(this);
    }

    void Changed(object sender, RoutedEventArgs e)
    {
        if (Preview == null) return;
        bool letters = Letters.IsChecked == true;
        int from = letters ? (FromBox.Text.Length > 0 ? FromBox.Text[0] : 'a') : (int.TryParse(FromBox.Text, out var f) ? f : 1);
        int to = letters ? (ToBox.Text.Length > 0 ? ToBox.Text[0] : 'z') : (int.TryParse(ToBox.Text, out var t) ? t : 10);
        int width = int.TryParse(WidthBox.Text, out var w) ? Math.Clamp(w, 1, 10) : 1;
        _urls = BatchPattern.Expand(PatternBox.Text.Trim(), from, to, width, letters);
        Preview.ItemsSource = _urls;
    }

    void Add(bool start)
    {
        if (_urls.Count == 0) { Dialogs.Info("Kalıpta * olmalı."); return; }
        var q = App.Manager.Queues[0];
        foreach (var u in _urls)
        {
            var name = FileNames.Sanitize(FileNames.FromUrl(u));
            var (cat, folder) = Categorizer.Resolve(name, u);
            App.Manager.Add(new DownloadItem { Url = u, FileName = name, Category = cat, Folder = folder, QueueId = q.Id, Status = DownloadStatus.Queued }, false);
        }
        if (start) App.Manager.StartQueue(q);
        DialogResult = true;
    }

    void Queue_Click(object sender, RoutedEventArgs e) => Add(false);
    void Start_Click(object sender, RoutedEventArgs e) => Add(true);
}
