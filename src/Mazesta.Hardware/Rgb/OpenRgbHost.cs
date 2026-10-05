using System.Diagnostics; using System.IO; using System.Net.Sockets;
namespace Mazesta.Hardware.Rgb;

/// <summary>Finds OpenRGB on the machine and starts it as the server this app talks to. It is not bundled: the owner puts its portable folder in
/// <c>OpenRGB</c> beside the app (or <c>Redist\OpenRGB</c>), or installs it, and nothing is downloaded behind the user's back.</summary>
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

    /// <summary>Whether something already listens on the SDK port (an OpenRGB the user started, with its SDK server on).</summary>
    public static async Task<bool> ListeningAsync(int port, CancellationToken ct)
    {
        try { using var t = new TcpClient(); using var c = CancellationTokenSource.CreateLinkedTokenSource(ct); c.CancelAfter(1500); await t.ConnectAsync("127.0.0.1", port, c.Token).ConfigureAwait(false); return true; }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException) { return false; }
    }

    /// <summary>Starts OpenRGB minimised with its SDK server and waits for the port (its device scan goes on after that). Null if it did not come up in time;
    /// the process is returned so that whoever started it can close it again.</summary>
    public static async Task<Process?> StartAsync(string exe, int port, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe, $"--server --server-port {port} --startminimized") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        var process = Process.Start(info);
        for (int i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (await ListeningAsync(port, ct).ConfigureAwait(false)) return process;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return null;
    }
}
