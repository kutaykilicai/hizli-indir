using System.Windows;
using HizliIndir.Core;

namespace HizliIndir.UI;

public static class Dialogs
{
    public static void Info(string text, string title = "Hızlı İndir") =>
        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string text, string title = "Hızlı İndir") =>
        MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void AddUrl(string? url = null)
    {
        var w = new AddUrlWindow(url);
        w.Owner = App.MainWin?.IsVisible == true ? App.MainWin : null;
        w.Show();
        w.Activate();
    }

    public static void Prepare(Window w)
    {
        w.Style = (Style)Application.Current.FindResource("BaseWindow");
        Interop.Backdrop.Apply(w);
        w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
    }
}
