using System.Diagnostics; using System.IO; using System.Net; using System.Net.Sockets;
namespace Mazesta.Hardware.Rgb;

/// <summary>Finds OpenRGB on the machine (the build puts its portable folder in <c>OpenRGB</c> beside the app) and starts it as the server this app talks to.</summary>
public static class OpenRgbHost
{
    public static string? Find(string appFolder)
    {
        string[] places =
        [
            Path.Combine(appFolder, "OpenRGB", "OpenRGB.exe"), Path.Combine(appFolder, "Redist", "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenRGB", "OpenRGB.exe"),
        ];
        return places.FirstOrDefault(File.Exists);
    }

    /// <summary>The ports a server is looked for on, in this order, and started on: the SDK's usual one first. A port can be taken by something else (Windows hands the
    /// same numbers to outgoing connections), and the server then ends at once; the next one is tried.</summary>
    public static readonly int[] Ports = [6742, 6743, 6744, 6745, 6746, 6747];

    /// <summary>The first of <see cref="Ports"/> something listens on (an OpenRGB the user or the tray started), or null.</summary>
    public static async Task<int?> FindListeningAsync(CancellationToken ct)
    {
        foreach (int p in Ports) if (await ListeningAsync(p, ct).ConfigureAwait(false)) return p;
        return null;
    }

    /// <summary>Whether a server can be started on this port (nothing holds it, as a listener or as the end of a connection).</summary>
    public static bool CanBind(int port)
    {
        try { var l = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true }; l.Start(); l.Stop(); return true; }
        catch (SocketException) { return false; }
    }

    /// <summary>Whether something already listens on the SDK port (an OpenRGB the user started, with its SDK server on).</summary>
    public static async Task<bool> ListeningAsync(int port, CancellationToken ct)
    {
        try { using var t = new TcpClient(); using var c = CancellationTokenSource.CreateLinkedTokenSource(ct); c.CancelAfter(1500); await t.ConnectAsync("127.0.0.1", port, c.Token).ConfigureAwait(false); return true; }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException) { return false; }
    }

    /// <summary>Starts OpenRGB as a server and nothing else: no window and no tray icon (its window is what asks the questions, like how long a strip is, and
    /// what the user would have to close). Its settings go in <paramref name="configDir"/>, not in the user's own OpenRGB. Waits for the port (its device
    /// scan goes on after that). Null if it did not come up in time; the process is returned so that whoever started it can close it again.</summary>
    public static async Task<Process?> StartAsync(string exe, int port, string configDir, CancellationToken ct)
    {
        Directory.CreateDirectory(configDir);
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(exe)! };
        info.ArgumentList.Add("--server"); info.ArgumentList.Add("--server-host"); info.ArgumentList.Add("127.0.0.1"); info.ArgumentList.Add("--server-port"); info.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--config"); info.ArgumentList.Add(configDir);
        var process = Process.Start(info);
        for (int i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (await ListeningAsync(port, ct).ConfigureAwait(false)) return process;
            if (process is { HasExited: true }) break;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        try { if (process is { HasExited: false }) process.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process?.Dispose(); return null;
    }
}
