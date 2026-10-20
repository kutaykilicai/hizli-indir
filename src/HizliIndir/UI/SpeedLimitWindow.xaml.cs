using System.Windows;

namespace HizliIndir.UI;

public partial class SpeedLimitWindow : Window
{
    public SpeedLimitWindow()
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        var s = App.Settings;
        (s.SpeedLimitEnabled ? OnPill : OffPill).IsChecked = true;
        ValueBox.Text = s.SpeedLimitKBps.ToString();
        StartupBox.IsChecked = s.SpeedLimitOnStartup;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        s.SpeedLimitEnabled = OnPill.IsChecked == true;
        if (int.TryParse(ValueBox.Text, out var v) && v > 0) s.SpeedLimitKBps = v;
        s.SpeedLimitOnStartup = StartupBox.IsChecked == true;
        App.Manager.ApplySpeedLimit();
        App.SaveSettings();
        DialogResult = true;
    }
}
