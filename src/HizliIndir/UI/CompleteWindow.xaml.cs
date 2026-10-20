using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class CompleteWindow : Window
{
    readonly DownloadItem _item;
    public CompleteWindow(DownloadItem item)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _item = item;
        NameText.Text = item.FileName;
        PathText.Text = item.FullPath;
        var avg = item.AverageSpeed is double a ? $"  ·  ortalama {Format.Bytes((long)a)}/sn" : "";
        Summary.Text = item.SizeText + avg;
        Finalize.Text = item.FinalizeMilliseconds is double f ? $"Son bayttan bitişe {f:0.##} ms geçti, parça birleştirme yapılmadı." : "";
        Closed += (_, _) => { if (NeverBox.IsChecked == true) { App.Settings.ShowCompleteDialog = false; App.SaveSettings(); } };
    }
    void Open_Click(object sender, RoutedEventArgs e) { Shell.Open(_item.FullPath); Close(); }
    void Folder_Click(object sender, RoutedEventArgs e) { Shell.Reveal(_item.FullPath); Close(); }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
