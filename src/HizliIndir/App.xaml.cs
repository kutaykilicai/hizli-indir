using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using HizliIndir.Core;
using HizliIndir.Interop;
using HizliIndir.UI;

namespace HizliIndir;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static DownloadManager Manager { get; private set; } = null!;
    public static MainWindow? MainWin { get; private set; }
    public static TrayIcon? Tray { get; private set; }
    static Mutex? _single;
    BridgeServer? _bridge;
    ClipboardWatcher? _clipboard;
    Scheduler? _scheduler;

    public static void SaveSettings() => Json.Save(Paths.SettingsFile, Settings);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        _single = new Mutex(true, "HizliIndir.Tek." + Environment.UserName, out bool first);
        if (!first)
        {
            var url = args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
            var msg = url != null ? new JsonObject { ["type"] = "download", ["url"] = url } : new JsonObject { ["type"] = "show" };
            BridgeServer.TrySend(msg);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) => { Log.Write("Beklenmeyen hata: " + ex.Exception); ex.Handled = true; };
        Settings = Json.Load(Paths.SettingsFile, () => new AppSettings());
        Settings.EnsureDefaults();
        try { Directory.CreateDirectory(Settings.TempFolder); } catch (Exception ex) { Log.Write("Geçici klasör: " + ex.Message); }
        if (Settings.SpeedLimitOnStartup) Settings.SpeedLimitEnabled = true;
        if (!Settings.FirstRunDone) FirstRun();
        else if (Settings.IdmImportVersion < 2)
        {
            IdmImport.TryImportFolders(Settings);
            Settings.IdmImportVersion = 2;
            SaveSettings();
        }
        else
        {
            var created = IdmImport.ApplyPreferredAndEnsureFolders(Settings);
            if (created.Count > 0) { foreach (var c in created) Log.Write("Klasör oluşturuldu: " + c); SaveSettings(); }
        }
        try { AutoStart.Apply(Settings.LaunchOnStartup); } catch (Exception ex) { Log.Write("Otomatik başlatma: " + ex.Message); }
        Theme.Init();
        Manager = new DownloadManager(Dispatcher);
        Manager.Completed += OnCompleted;
        Manager.Failed += OnFailed;
        Manager.LimitReached += m => Dialogs.Info(m, "İndirme sınırı");
        Manager.QueueFinished += q => _scheduler?.OnQueueFinished(q);

        try { NativeHost.Register(); } catch (Exception ex) { Log.Write("Tarayıcı kaydı yapılamadı: " + ex.Message); }

        _bridge = new BridgeServer(msg => Dispatcher.InvokeAsync(() => Bridge.Handle(msg)).Task.Unwrap());
        _bridge.Start();

        Tray = new TrayIcon();
        MainWin = new MainWindow();
        bool tray = args.Contains("--tray");
        if (!tray) MainWin.Show();

        _clipboard = new ClipboardWatcher(url => { if (Settings.WatchClipboard) Dialogs.AddUrl(url); });
        _scheduler = new Scheduler(Manager);
        _ = Task.Run(async () => { try { await Tools.EnsureAsync(null, CancellationToken.None); } catch (Exception ex) { Log.Write("Araçlar hazırlanamadı: " + ex.Message); } });

        var direct = args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (direct != null) Dialogs.AddUrl(direct);
    }

    void FirstRun()
    {
        IdmImport.TryImportFolders(Settings);
        Settings.IdmImportVersion = 2;
        Settings.FirstRunDone = true;
        try { AutoStart.Apply(Settings.LaunchOnStartup); } catch { }
        SaveSettings();
    }

    void OnCompleted(DownloadItem item)
    {
        if (Settings.PlaySounds) Sounds.Complete();
        if (Settings.Notifications) Tray?.Notify("İndirme tamamlandı", item.FileName);
        if (Settings.ShowCompleteDialog && item.Kind == DownloadKind.Http && !ProgressWindow.SuppressComplete(item)) new CompleteWindow(item).Show();
        ProgressWindow.CloseFor(item);
    }

    void OnFailed(DownloadItem item)
    {
        if (Settings.PlaySounds) Sounds.Failed();
        if (Settings.Notifications) Tray?.Notify("İndirme durdu", $"{item.FileName}: {item.Error}");
    }

    public static void ShowMain()
    {
        if (MainWin == null) return;
        MainWin.Show();
        if (MainWin.WindowState == WindowState.Minimized) MainWin.WindowState = WindowState.Normal;
        MainWin.Activate();
    }

    public static void Quit()
    {
        Manager.PauseAll();
        Manager.Save();
        SaveSettings();
        Tray?.Dispose();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bridge?.Stop();
        try { Manager?.Save(); } catch { }
        base.OnExit(e);
    }
}
