using System.Windows;
using HizliIndir.Core;
using WF = System.Windows.Forms;

namespace HizliIndir.UI;

public sealed class TrayIcon : IDisposable
{
    readonly WF.NotifyIcon _icon;
    readonly WF.ToolStripMenuItem _limit, _clip;

    public TrayIcon()
    {
        var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        _icon = new WF.NotifyIcon { Icon = new System.Drawing.Icon(s), Text = "Hızlı İndir", Visible = true };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Hızlı İndir'i aç", null, (_, _) => App.ShowMain());
        menu.Items.Add("Adres ekle", null, (_, _) => Dialogs.AddUrl());
        menu.Items.Add(new WF.ToolStripSeparator());
        _limit = new WF.ToolStripMenuItem("Hız sınırı", null, (_, _) => ToggleLimit());
        _clip = new WF.ToolStripMenuItem("Panoyu izle", null, (_, _) => { App.Settings.WatchClipboard = !App.Settings.WatchClipboard; App.SaveSettings(); Sync(); });
        menu.Items.Add(_limit); menu.Items.Add(_clip);
        menu.Items.Add("Tüm indirmeleri durdur", null, (_, _) => App.Manager.PauseAll());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => App.Quit());
        menu.Opening += (_, _) => Sync();
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) App.ShowMain(); };
        _icon.BalloonTipClicked += (_, _) => App.ShowMain();
    }

    void Sync()
    {
        _limit.Checked = App.Settings.SpeedLimitEnabled;
        _clip.Checked = App.Settings.WatchClipboard;
    }

    void ToggleLimit()
    {
        App.Settings.SpeedLimitEnabled = !App.Settings.SpeedLimitEnabled;
        App.Manager.ApplySpeedLimit();
        App.SaveSettings();
        App.MainWin?.RefreshStatus();
    }

    public void Notify(string title, string text)
    {
        if (!App.Settings.Notifications) return;
        _icon.ShowBalloonTip(4000, title, text.Length > 200 ? text[..200] : text, WF.ToolTipIcon.None);
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 60 ? text[..60] : text;

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
