using System.Windows;
using System.Windows.Controls;

namespace HizliIndir.UI;

/// <summary>Bir ya da birkaç alan isteyen küçük pencere.</summary>
public sealed class InputDialog : Window
{
    readonly List<TextBox> _boxes = new();
    public string[] Values => _boxes.Select(b => b.Text.Trim()).ToArray();

    public InputDialog(string title, params (string label, string value)[] fields)
    {
        Title = title;
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dialogs.Prepare(this);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Title"), FontSize = 17, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var (label, value) in fields)
        {
            root.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 6, 0, 4) });
            var tb = new TextBox { Text = value };
            _boxes.Add(tb); root.Children.Add(tb);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Vazgeç", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Tamam", IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _boxes.FirstOrDefault()?.Focus(); _boxes.FirstOrDefault()?.SelectAll(); };
    }
}
