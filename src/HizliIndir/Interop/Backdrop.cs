using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace HizliIndir.Interop;

/// <summary>Windows 11 Mica arka planı, koyu başlık çubuğu ve yuvarlatılmış köşeler.</summary>
public static class Backdrop
{
    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static bool MicaSupported => Environment.OSVersion.Version.Build >= 22621;

    public static void Apply(Window w, bool tabbed = false)
    {
        void Do()
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = Theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            int round = 2;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            if (!MicaSupported) return;
            var src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            w.Background = Brushes.Transparent;
            var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            int type = tabbed ? 4 : 2;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
        }
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Do();
        else w.SourceInitialized += (_, _) => Do();
    }
}

public static class Theme
{
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Init()
    {
        Refresh();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) Application.Current.Dispatcher.BeginInvoke(Refresh);
        };
    }

    public static void Refresh()
    {
        var mode = App.Settings.Theme;
        bool dark = mode == Core.ThemeMode.Dark || (mode == Core.ThemeMode.System && SystemDark());
        IsDark = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/Light") == true || d.Source?.OriginalString.Contains("Themes/Dark") == true);
        var fresh = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (old != null) dicts[dicts.IndexOf(old)] = fresh; else dicts.Insert(0, fresh);
        foreach (Window w in Application.Current.Windows) Backdrop.Apply(w);
        Changed?.Invoke();
    }

    static bool SystemDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
