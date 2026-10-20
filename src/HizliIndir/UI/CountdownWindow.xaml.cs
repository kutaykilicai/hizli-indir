using System.Windows;
using System.Windows.Threading;
using HizliIndir.Core;

namespace HizliIndir.UI;

public partial class CountdownWindow : Window
{
    readonly PostAction _action;
    readonly DispatcherTimer _t;
    int _left = 60;

    public CountdownWindow(PostAction action)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _action = action;
        Head.Text = Power.Label(action);
        _t = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Step(), Dispatcher);
        _t.Start();
        Step();
    }

    void Step()
    {
        Body.Text = $"İndirmeler bitti. {_left} saniye sonra bu işlem yapılacak.";
        if (_left-- <= 0) { _t.Stop(); Close(); Power.Run(_action); }
    }

    void Now_Click(object sender, RoutedEventArgs e) { _t.Stop(); Close(); Power.Run(_action); }
    void Cancel_Click(object sender, RoutedEventArgs e) { _t.Stop(); Close(); }
}
