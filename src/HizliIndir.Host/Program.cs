using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

// Chrome/Edge native messaging köprüsü.
// Tarayıcı bu programı başlatır, stdin üzerinden 4 bayt uzunluk + UTF-8 JSON mesaj yollar.
// Mesaj Hızlı İndir'in adlandırılmış kanalına iletilir, cevap aynı biçimde stdout'a yazılır.

var pipeName = "HizliIndir.Kopru." + Environment.UserName;
var appExe = Path.Combine(AppContext.BaseDirectory, "HizliIndir.exe");
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

while (true)
{
    var msg = ReadMessage(stdin);
    if (msg == null) break;
    string reply;
    try { reply = Forward(msg); }
    catch (Exception ex) { reply = "{\"ok\":false,\"error\":" + System.Text.Json.JsonSerializer.Serialize(ex.Message) + "}"; }
    WriteMessage(stdout, reply);
}

string Forward(string json)
{
    // Tek satırlık protokol: mesaj içindeki satır sonları JSON'da zaten kaçışlı olur.
    json = json.Replace("\r", "").Replace("\n", "");
    for (int attempt = 0; attempt < 2; attempt++)
    {
        var deadline = DateTime.UtcNow.AddSeconds(attempt == 0 ? 0.8 : 20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                c.Connect(500);
                using var w = new StreamWriter(c, new UTF8Encoding(false), 1 << 16, true) { AutoFlush = true };
                using var r = new StreamReader(c, new UTF8Encoding(false), false, 1 << 16, true);
                w.WriteLine(json);
                return r.ReadLine() ?? "{\"ok\":false,\"error\":\"cevap yok\"}";
            }
            catch (TimeoutException) { }
            catch (IOException) { Thread.Sleep(200); }
        }
        if (attempt == 0)
        {
            if (!File.Exists(appExe)) return "{\"ok\":false,\"error\":\"Hızlı İndir bulunamadı\"}";
            Launcher.Start(appExe, "--tray");
        }
    }
    return "{\"ok\":false,\"error\":\"Hızlı İndir'e bağlanılamadı\"}";
}

static string? ReadMessage(Stream s)
{
    var len = new byte[4];
    if (!ReadExact(s, len)) return null;
    int n = BitConverter.ToInt32(len, 0);
    if (n <= 0 || n > 64 * 1024 * 1024) return null;
    var buf = new byte[n];
    if (!ReadExact(s, buf)) return null;
    return Encoding.UTF8.GetString(buf);
}

static bool ReadExact(Stream s, byte[] buf)
{
    int off = 0;
    while (off < buf.Length)
    {
        int r = s.Read(buf, off, buf.Length - off);
        if (r <= 0) return false;
        off += r;
    }
    return true;
}

static void WriteMessage(Stream s, string json)
{
    var data = Encoding.UTF8.GetBytes(json);
    s.Write(BitConverter.GetBytes(data.Length));
    s.Write(data);
    s.Flush();
}

static class Launcher
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string? lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool CreateProcess(string? app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    // Tarayıcı köprüyü kapatınca uygulama da kapanmasın diye iş nesnesinin dışında başlatılır.
    public static void Start(string exe, string args)
    {
        const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000, DETACHED_PROCESS = 0x00000008, CREATE_NEW_PROCESS_GROUP = 0x00000200;
        var si = new STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf<STARTUPINFO>() };
        var cmd = new System.Text.StringBuilder($"\"{exe}\" {args}");
        if (CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_BREAKAWAY_FROM_JOB | DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out var pi))
        {
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return;
        }
        // İş nesnesi ayrılmaya izin vermiyorsa Gezgin üzerinden başlat.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false });
    }
}
