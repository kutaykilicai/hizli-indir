using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HizliIndir.Core;

public static class Power
{
    [DllImport("powrprof.dll", SetLastError = true)]
    static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static string Label(PostAction a) => a switch
    {
        PostAction.None => "Hiçbir şey yapma",
        PostAction.ExitApp => "Hızlı İndir'i kapat",
        PostAction.Sleep => "Bilgisayarı uyut",
        PostAction.Hibernate => "Hazırda beklet",
        PostAction.Shutdown => "Bilgisayarı kapat",
        PostAction.ForceShutdown => "Bilgisayarı kapat (zorla)",
        _ => a.ToString()
    };

    public static void Run(PostAction a)
    {
        Log.Write("Bitiş işlemi: " + a);
        switch (a)
        {
            case PostAction.Sleep: SetSuspendState(false, false, false); break;
            case PostAction.Hibernate: SetSuspendState(true, false, false); break;
            case PostAction.Shutdown: Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            case PostAction.ForceShutdown: Process.Start(new ProcessStartInfo("shutdown", "/s /f /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            case PostAction.ExitApp: System.Windows.Application.Current.Shutdown(); break;
        }
    }
}
