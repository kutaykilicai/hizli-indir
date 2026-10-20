using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HizliIndir.Core;

namespace HizliIndir.UI;

public sealed class CategoryRow
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public List<string> Sites { get; set; } = new();
    public string ExtText => string.Join(" ", Extensions);
}

public partial class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly ObservableCollection<ServerException> _exceptions;
    readonly ObservableCollection<CategoryRow> _cats;
    readonly ObservableCollection<SiteLogin> _logins;

    public SettingsWindow(string? page = null)
    {
        InitializeComponent();
        Dialogs.Prepare(this);
        _s = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(App.Settings, Json.Options), Json.Options)!;
        _exceptions = new(_s.ServerExceptions);
        _cats = new(_s.Categories.Select(c => new CategoryRow { Name = c.Name, Folder = c.Folder, Extensions = c.Extensions.ToList(), Sites = c.Sites.ToList() }));
        _logins = new(_s.Logins);
        Load();
        Nav.SelectedIndex = page == null ? 0 : Nav.Items.Cast<ListBoxItem>().ToList().FindIndex(i => (string)i.Tag == page);
    }

    void Load()
    {
        StartupBox.IsChecked = _s.LaunchOnStartup; TrayBox.IsChecked = _s.MinimizeToTray; ClipBox.IsChecked = _s.WatchClipboard;
        (_s.Theme switch { ThemeMode.Light => ThemeLight, ThemeMode.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        StartDlgBox.IsChecked = _s.ShowStartDialog; ProgDlgBox.IsChecked = _s.ShowProgressWindow; DoneDlgBox.IsChecked = _s.ShowCompleteDialog; NotifyBox.IsChecked = _s.Notifications;

        ExtPath.Text = NativeHost.ExtensionFolder;
        RefreshHost();
        CaptureBox.Text = string.Join(" ", _s.CaptureExtensions);
        SkipBox.Text = string.Join(" ", _s.SkipSites);
        NoPanelBox.Text = string.Join(" ", _s.NoVideoPanelSites);
        MinSizeBox.Text = (_s.MinCaptureBytes / 1024).ToString();

        BaseBox.Text = _s.BaseFolder; TempBox.Text = _s.TempFolder; RememberBox.IsChecked = _s.RememberLastFolder;
        CatList.ItemsSource = _cats;
        (_s.Duplicates == DuplicateMode.Overwrite ? DupOverwrite : DupRename).IsChecked = true;

        for (int i = 1; i <= 32; i++) MaxConnBox.Items.Add(i);
        MaxConnBox.SelectedItem = Math.Clamp(_s.MaxConnections, 1, 32);
        ExceptionList.ItemsSource = _exceptions;
        LimitBox.IsChecked = _s.DownloadLimitEnabled; LimitMb.Text = _s.DownloadLimitMegabytes.ToString(); LimitHours.Text = _s.DownloadLimitHours.ToString(); LimitWarn.IsChecked = _s.DownloadLimitWarn;
        LimitPanel.IsEnabled = _s.DownloadLimitEnabled;
        ParallelBox.Text = App.Manager.Queues.First().MaxParallel.ToString(); RetryBox.Text = _s.RetryCount.ToString(); TimeoutBox.Text = _s.TimeoutSeconds.ToString(); Http2Box.IsChecked = _s.PreferHttp2;

        (_s.Proxy switch { ProxyMode.None => ProxyNone, ProxyMode.Manual => ProxyManual, _ => ProxySystem }).IsChecked = true;
        PxHost.Text = _s.ProxyHost; PxPort.Text = _s.ProxyPort.ToString(); PxUser.Text = _s.ProxyUser; PxPass.Password = Protect.Decrypt(_s.ProxyPasswordProtected); PxBypass.Text = _s.ProxyBypass;

        LoginList.ItemsSource = _logins;

        QualityBox.ItemsSource = VideoJob.Choices;
        QualityBox.SelectedItem = VideoJob.Choices.FirstOrDefault(c => c.Key == _s.DefaultVideoQuality) ?? VideoJob.Choices[3];
        FragBox.Text = _s.VideoFragments.ToString(); YtUpdateBox.IsChecked = _s.AutoUpdateYtDlp;
        ToolsText.Text = Tools.Ready ? $"Hazır: {Paths.Tools}" : "yt-dlp, ffmpeg ve deno henüz indirilmedi. İlk video indirmesinde kendiliğinden indirilir.";

        SoundBox.IsChecked = _s.PlaySounds; SoundOk.Text = _s.SoundComplete ?? ""; SoundFail.Text = _s.SoundFailed ?? "";
        VirusBox.IsChecked = _s.VirusScanEnabled; VirusExe.Text = _s.VirusScanner; VirusArgs.Text = _s.VirusScannerArgs;
    }

    void RefreshHost()
    {
        bool ok = NativeHost.IsRegistered();
        HostText.Text = ok ? "Tarayıcı köprüsü kayıtlı (Chrome, Edge, Brave)" : "Tarayıcı köprüsü kayıtlı değil";
        HostDot.Background = (System.Windows.Media.Brush)FindResource(ok ? "Success" : "Danger");
    }

    void Nav_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem li) return;
        foreach (var p in new FrameworkElement[] { General, Browser, Save, Connection, Proxy, Sites, Video, Extra })
            p.Visibility = p.Name == (string)li.Tag ? Visibility.Visible : Visibility.Collapsed;
    }

    static List<string> Split(string t) => t.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).Distinct().ToList();
    static int Int(string t, int fallback, int min, int max) => int.TryParse(t, out var v) ? Math.Clamp(v, min, max) : fallback;

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _s.LaunchOnStartup = StartupBox.IsChecked == true; _s.MinimizeToTray = TrayBox.IsChecked == true; _s.WatchClipboard = ClipBox.IsChecked == true;
        _s.Theme = ThemeLight.IsChecked == true ? ThemeMode.Light : ThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeMode.System;
        _s.ShowStartDialog = StartDlgBox.IsChecked == true; _s.ShowProgressWindow = ProgDlgBox.IsChecked == true; _s.ShowCompleteDialog = DoneDlgBox.IsChecked == true; _s.Notifications = NotifyBox.IsChecked == true;

        _s.CaptureExtensions = Split(CaptureBox.Text);
        _s.SkipSites = Split(SkipBox.Text);
        _s.NoVideoPanelSites = Split(NoPanelBox.Text);
        _s.MinCaptureBytes = Int(MinSizeBox.Text, 0, 0, int.MaxValue) * 1024L;

        _s.BaseFolder = BaseBox.Text.Trim(); _s.TempFolder = string.IsNullOrWhiteSpace(TempBox.Text) ? AppSettings.DefaultTemp() : TempBox.Text.Trim(); _s.RememberLastFolder = RememberBox.IsChecked == true;
            try { Directory.CreateDirectory(_s.TempFolder); } catch { }
        _s.Categories = _cats.Select(c => new CategoryInfo { Name = c.Name, Folder = c.Folder, Extensions = c.Extensions, Sites = c.Sites }).ToList();
        _s.Duplicates = DupOverwrite.IsChecked == true ? DuplicateMode.Overwrite : DuplicateMode.Rename;

        _s.MaxConnections = MaxConnBox.SelectedItem is int mc ? mc : 16;
        _s.ServerExceptions = _exceptions.ToList();
        _s.DownloadLimitEnabled = LimitBox.IsChecked == true;
        _s.DownloadLimitMegabytes = Int(LimitMb.Text, 200, 1, 10_000_000); _s.DownloadLimitHours = Int(LimitHours.Text, 5, 1, 720); _s.DownloadLimitWarn = LimitWarn.IsChecked == true;
        App.Manager.Queues.First().MaxParallel = Int(ParallelBox.Text, 2, 1, 20);
        _s.RetryCount = Int(RetryBox.Text, 10, 1, 1000); _s.TimeoutSeconds = Int(TimeoutBox.Text, 30, 5, 600); _s.PreferHttp2 = Http2Box.IsChecked == true;

        _s.Proxy = ProxyNone.IsChecked == true ? ProxyMode.None : ProxyManual.IsChecked == true ? ProxyMode.Manual : ProxyMode.System;
        _s.ProxyHost = PxHost.Text.Trim(); _s.ProxyPort = Int(PxPort.Text, 8080, 1, 65535); _s.ProxyUser = PxUser.Text.Trim();
        _s.ProxyPasswordProtected = Protect.Encrypt(PxPass.Password); _s.ProxyBypass = PxBypass.Text.Trim();

        _s.Logins = _logins.ToList();
        _s.DefaultVideoQuality = (QualityBox.SelectedItem as VideoChoice)?.Key ?? "1080";
        _s.VideoFragments = Int(FragBox.Text, 16, 1, 32); _s.AutoUpdateYtDlp = YtUpdateBox.IsChecked == true;

        _s.PlaySounds = SoundBox.IsChecked == true; _s.SoundComplete = NullIfEmpty(SoundOk.Text); _s.SoundFailed = NullIfEmpty(SoundFail.Text);
        _s.VirusScanEnabled = VirusBox.IsChecked == true; _s.VirusScanner = VirusExe.Text.Trim(); _s.VirusScannerArgs = VirusArgs.Text.Trim();

        CopyInto(_s, App.Settings);
        App.SaveSettings();
        try { AutoStart.Apply(App.Settings.LaunchOnStartup); } catch { }
        Net.Reset();
        App.Manager.ApplySpeedLimit();
        App.Manager.Save();
        Interop.Theme.Refresh();
        DialogResult = true;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static void CopyInto(AppSettings src, AppSettings dst)
    {
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) p.SetValue(dst, p.GetValue(src));
    }

    // Bağlantı istisnaları
    void Exception_Selection(object sender, SelectionChangedEventArgs e) => ExDelete.IsEnabled = ExEdit.IsEnabled = ExceptionList.SelectedItem != null;

    void ExNew_Click(object sender, RoutedEventArgs e)
    {
        var d = new InputDialog("Sunucu istisnası", ("Sunucu (yıldız kullanılabilir, örneğin *.ornek.com)", ""), ("Bağlantı sayısı (1-32)", "1")) { Owner = this };
        if (d.ShowDialog() == true && d.Values[0].Length > 0) _exceptions.Add(new ServerException { Host = d.Values[0], Connections = Int(d.Values[1], 1, 1, 32) });
    }

    void ExEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ExceptionList.SelectedItem is not ServerException ex) return;
        var d = new InputDialog("Sunucu istisnası", ("Sunucu", ex.Host), ("Bağlantı sayısı (1-32)", ex.Connections.ToString())) { Owner = this };
        if (d.ShowDialog() != true) return;
        var i = _exceptions.IndexOf(ex);
        _exceptions[i] = new ServerException { Host = d.Values[0], Connections = Int(d.Values[1], 1, 1, 32) };
    }

    void ExDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ExceptionList.SelectedItem is ServerException ex) _exceptions.Remove(ex);
    }

    void LimitToggle(object sender, RoutedEventArgs e) { if (LimitPanel != null) LimitPanel.IsEnabled = LimitBox.IsChecked == true; }

    // Kategoriler
    void CatNew_Click(object sender, RoutedEventArgs e)
    {
        var d = new InputDialog("Yeni kategori", ("Ad", ""), ("Klasör", BaseBox.Text), ("Uzantılar (boşlukla)", ""), ("Yalnızca bu sitelerden (isteğe bağlı)", "")) { Owner = this };
        if (d.ShowDialog() == true && d.Values[0].Length > 0)
            _cats.Add(new CategoryRow { Name = d.Values[0], Folder = d.Values[1], Extensions = Split(d.Values[2]), Sites = Split(d.Values[3]) });
    }

    void CatEdit_Click(object sender, RoutedEventArgs e)
    {
        if (CatList.SelectedItem is not CategoryRow c) return;
        var d = new InputDialog("Kategoriyi düzenle", ("Ad", c.Name), ("Klasör", c.Folder), ("Uzantılar (boşlukla)", c.ExtText), ("Yalnızca bu sitelerden (isteğe bağlı)", string.Join(" ", c.Sites))) { Owner = this };
        if (d.ShowDialog() != true) return;
        _cats[_cats.IndexOf(c)] = new CategoryRow { Name = d.Values[0], Folder = d.Values[1], Extensions = Split(d.Values[2]), Sites = Split(d.Values[3]) };
    }

    void CatDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CatList.SelectedItem is CategoryRow c && Dialogs.Confirm($"{c.Name} kategorisi silinsin mi?")) _cats.Remove(c);
    }

    void BaseBrowse_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(BaseBox.Text) ? BaseBox.Text : null };
        if (d.ShowDialog(this) == true) BaseBox.Text = d.FolderName;
    }

    void TempBrowse_Click(object sender, RoutedEventArgs e)
    {
        var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = TempBox.Text };
        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) TempBox.Text = d.SelectedPath;
    }


    void ImportIdm_Click(object sender, RoutedEventArgs e)
    {
        var tmp = new AppSettings { BaseFolder = _s.BaseFolder, Categories = _cats.Select(c => new CategoryInfo { Name = c.Name, Folder = c.Folder, Extensions = c.Extensions, Sites = c.Sites }).ToList() };
        if (IdmImport.TryImportFolders(tmp))
        {
            _cats.Clear();
            foreach (var c in tmp.Categories) _cats.Add(new CategoryRow { Name = c.Name, Folder = c.Folder, Extensions = c.Extensions, Sites = c.Sites });
            BaseBox.Text = tmp.BaseFolder;
            Dialogs.Info("Klasörler içe aktarıldı. Kaydet'e basınca uygulanır.");
        }
        else Dialogs.Info("İçe aktarılacak ayar bulunamadı.");
    }

    // Tarayıcı
    void ExtFolder_Click(object sender, RoutedEventArgs e) => Shell.Open(NativeHost.ExtensionFolder);
    void Register_Click(object sender, RoutedEventArgs e)
    {
        try { NativeHost.Register(); } catch (Exception ex) { Dialogs.Info("Kayıt yapılamadı: " + ex.Message); }
        RefreshHost();
    }

    // Siteler
    void LoginNew_Click(object sender, RoutedEventArgs e)
    {
        var d = new InputDialog("Site şifresi", ("Site (örneğin dosyalar.ornek.com ya da *.ornek.com)", ""), ("Kullanıcı adı", ""), ("Şifre", "")) { Owner = this };
        if (d.ShowDialog() == true && d.Values[0].Length > 0) _logins.Add(new SiteLogin { Pattern = d.Values[0], User = d.Values[1], PasswordProtected = Protect.Encrypt(d.Values[2]) });
    }
    void LoginDelete_Click(object sender, RoutedEventArgs e) { if (LoginList.SelectedItem is SiteLogin l) _logins.Remove(l); }

    async void Tools_Click(object sender, RoutedEventArgs e)
    {
        ToolsBtn.IsEnabled = false;
        try
        {
            App.Settings.LastYtDlpUpdate = null;
            await Tools.EnsureAsync(new Progress<string>(m => ToolsText.Text = m), CancellationToken.None);
            ToolsText.Text = $"Hazır: {Paths.Tools}";
        }
        catch (Exception ex) { ToolsText.Text = "Hazırlanamadı: " + ex.Message; }
        ToolsBtn.IsEnabled = true;
    }
}
