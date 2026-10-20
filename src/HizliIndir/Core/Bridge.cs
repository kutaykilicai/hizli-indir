using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace HizliIndir.Core;

/// <summary>Tarayıcı köprüsünden (HizliIndir.Host) ve ikinci örneklerden gelen istekleri dinler.</summary>
public sealed class BridgeServer
{
    public static string PipeName => "HizliIndir.Kopru." + Environment.UserName;
    readonly Func<JsonObject, Task<JsonObject>> _handler;
    readonly CancellationTokenSource _cts = new();

    public BridgeServer(Func<JsonObject, Task<JsonObject>> handler) { _handler = handler; }

    public void Start()
    {
        for (int i = 0; i < 4; i++) _ = Task.Run(LoopAsync);
    }

    public void Stop() => _cts.Cancel();

    async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1 << 16, true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1 << 16, true) { AutoFlush = true };
                string? line;
                while ((line = await reader.ReadLineAsync(_cts.Token)) != null)
                {
                    JsonObject reply;
                    try
                    {
                        var msg = JsonNode.Parse(line) as JsonObject ?? new JsonObject();
                        reply = await _handler(msg);
                    }
                    catch (Exception ex) { reply = new JsonObject { ["ok"] = false, ["error"] = ex.Message }; }
                    await writer.WriteLineAsync(reply.ToJsonString());
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Write("Köprü: " + ex.Message); await Task.Delay(200); }
        }
    }

    public static bool TrySend(JsonObject msg, int timeoutMs = 1500)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            c.Connect(timeoutMs);
            using var w = new StreamWriter(c, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var r = new StreamReader(c, new UTF8Encoding(false), false, 4096, true);
            w.WriteLine(msg.ToJsonString());
            r.ReadLine();
            return true;
        }
        catch { return false; }
    }
}

/// <summary>Chrome ve Edge için native messaging kaydını HKCU altına yazar.</summary>
public static class NativeHost
{
    public const string Name = "com.kutaykilic.hizliindir";
    public const string ExtensionId = "ggkjdampbopnagbdllenngdcjbhlfpla";

    public static string HostExe => Path.Combine(AppContext.BaseDirectory, "HizliIndir.Host.exe");
    public static string ManifestPath => Path.Combine(Paths.Data, Name + ".json");
    public static string ExtensionFolder => Path.Combine(AppContext.BaseDirectory, "extension");

    public static void Register()
    {
        var manifest = new JsonObject
        {
            ["name"] = Name,
            ["description"] = "Hızlı İndir tarayıcı köprüsü",
            ["path"] = HostExe,
            ["type"] = "stdio",
            ["allowed_origins"] = new JsonArray($"chrome-extension://{ExtensionId}/"),
        };
        File.WriteAllText(ManifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        foreach (var root in new[] { @"Software\Google\Chrome\NativeMessagingHosts", @"Software\Microsoft\Edge\NativeMessagingHosts", @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts", @"Software\Chromium\NativeMessagingHosts" })
        {
            using var k = Registry.CurrentUser.CreateSubKey(root + "\\" + Name);
            k.SetValue("", ManifestPath);
        }
    }

    public static bool IsRegistered()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Google\Chrome\NativeMessagingHosts\" + Name);
        return k?.GetValue("") is string p && File.Exists(p) && File.ReadAllText(p).Contains(HostExe.Replace("\\", "\\\\"));
    }
}

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Apply(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue("HizliIndir", $"\"{Environment.ProcessPath}\" --tray");
        else k.DeleteValue("HizliIndir", false);
    }
}
