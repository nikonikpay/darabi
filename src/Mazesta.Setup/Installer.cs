using System.Diagnostics; using System.IO.Compression; using System.Reflection;
using Microsoft.Win32;
namespace Mazesta.Setup;

/// <summary>What installing does, apart from the window: checks the target, replaces an earlier copy's files, unpacks the carried zip, makes the shortcuts
/// and the entry in Installed apps - all for the machine (Program Files, the common Start menu and desktop, HKLM), so every user of the computer has the
/// program. The app keeps each user's Data in that user's profile; the <c>installed.flag</c> file beside the exe tells it so. A copy installed by the
/// earlier per-user setup is taken over: its Data is copied to the profile and its files are removed. The uninstall side is <c>Mazesta.exe --uninstall</c>.</summary>
internal static class Installer
{
    public const string AppExe = "Mazesta.exe", ShortcutName = "Mazesta Test", Publisher = "Mazesta", UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MazestaTest", Marker = "installed.flag";
    public static string Version => typeof(Installer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
    public static string DefaultFolder => Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mazesta Test");
    public static string ProfileRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mazesta Test");
    public static bool HasPayload => typeof(Installer).Assembly.GetManifestResourceStream("payload.zip") is not null;

    public enum FolderState { Bad, Free, Ours, Foreign }

    private static RegistryKey Hive(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    /// <summary>Where a setup put the app (from Installed apps' entry, the machine's first, then the earlier per-user one), if its folder is still there.</summary>
    public static string? InstalledFolder(out bool perUser)
    {
        foreach (var h in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = Hive(h); using var key = root.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is string dir && File.Exists(Path.Combine(dir, AppExe))) { perUser = h == RegistryHive.CurrentUser; return dir; }
        }
        perUser = false; return null;
    }

    /// <summary>Whether <paramref name="folder"/> can be installed into: it does not exist or is empty (Free), already holds our program (Ours: replaced), holds other
    /// things (Foreign: the window asks the user first, and only the program's files are then written), or is not a folder to install into (Bad).</summary>
    public static FolderState Check(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) return FolderState.Bad;
        string full = Path.GetFullPath(folder);
        if (Path.GetPathRoot(full)?.TrimEnd('\\') == full.TrimEnd('\\')) return FolderState.Bad;   // a drive's root
        if (!Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any()) return FolderState.Free;
        return File.Exists(Path.Combine(full, AppExe)) ? FolderState.Ours : FolderState.Foreign;
    }

    public static bool IsRunning() => Process.GetProcessesByName("Mazesta").Length + Process.GetProcessesByName("MazestaTray").Length > 0;

    /// <summary>Closes the app, its tray and the OpenRGB it started (their files are what the setup replaces); false if one would not close. Setup runs elevated, so the app's own elevated copy can be ended too.</summary>
    public static bool CloseRunning()
    {
        foreach (var name in new[] { "Mazesta", "MazestaTray", "OpenRGB" })
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                    try
                    {
                        if (name == "OpenRGB" && (p.MainModule?.FileName ?? "").IndexOf("Mazesta", StringComparison.OrdinalIgnoreCase) < 0) continue;   // a user's own OpenRGB stays
                        if (!p.CloseMainWindow() || !p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(5000); }
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return !IsRunning();
    }

    /// <summary>Installs; false when the WebView2 runtime the app needs is still missing afterwards.</summary>
    public static bool Install(string folder, bool desktop, bool foreign, IProgress<(int Percent, string Text)> progress)
    {
        folder = Path.GetFullPath(folder);
        using var payload = typeof(Installer).Assembly.GetManifestResourceStream("payload.zip") ?? throw new InvalidOperationException(SetupText.NoPayload);
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        string? earlier = InstalledFolder(out bool perUser);
        Directory.CreateDirectory(folder);
        progress.Report((2, SetupText.Preparing));
        // Data an earlier copy kept beside its exe goes to the profile first, before anything of that copy is touched.
        if (earlier is not null) { progress.Report((4, SetupText.Moving)); MoveData(Path.Combine(earlier, "Data")); }
        // Our own earlier files go (never a Data folder left beside them); in a folder that held other things only our known sub-folders are cleared.
        string[] ours = ["wwwroot", "Redist"];
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(e => !string.Equals(Path.GetFileName(e), "Data", StringComparison.OrdinalIgnoreCase)
                     && (!foreign || (Directory.Exists(e) && ours.Contains(Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)))))
        { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
        var files = zip.Entries.Where(e => e.Name.Length > 0).ToList(); int done = 0;
        foreach (var entry in files)
        {
            string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
            if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(entry.FullName);   // never outside the folder
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
            progress.Report((6 + 82 * ++done / files.Count, SetupText.Copying));
        }
        string exe = Path.Combine(folder, AppExe);
        if (!File.Exists(exe)) throw new InvalidDataException(AppExe);
        File.WriteAllText(Path.Combine(folder, Marker), "Mazesta Test: this copy is installed; each user's Data is in %LocalAppData%\\Mazesta Test\\Data.");
        progress.Report((91, SetupText.Shortcuts));
        string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutName);
        Directory.CreateDirectory(menu);
        Shortcut(Path.Combine(menu, ShortcutName + ".lnk"), exe, folder);
        string deskLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutName + ".lnk");
        if (desktop) Shortcut(deskLink, exe, folder); else if (File.Exists(deskLink)) File.Delete(deskLink);
        progress.Report((96, SetupText.Registering));
        using (var root = Hive(RegistryHive.LocalMachine))
        {
            using var key = root.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "Mazesta Test"); key.SetValue("DisplayVersion", Version); key.SetValue("Publisher", Publisher);
            key.SetValue("InstallLocation", folder); key.SetValue("DisplayIcon", exe);
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall"); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(files.Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (earlier is not null && perUser) RemoveEarlier(earlier, folder);
        bool web = WebViewReady() || InstallWebView(progress);
        progress.Report((100, web ? SetupText.Done : SetupText.DoneNoWebView));
        return web;
    }

    // The WebView2 runtime's client key (the same on every machine); "pv" is its version. The app needs 92.0.902.49 or later (MainWindow.MinRuntime).
    private const string WebViewKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static bool WebViewReady()
    {
        foreach (var (hive, path) in new[] { (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\" + WebViewKey), (RegistryHive.LocalMachine, @"SOFTWARE\" + WebViewKey), (RegistryHive.CurrentUser, @"Software\" + WebViewKey) })
        {
            using var root = Hive(hive); using var key = root.OpenSubKey(path);
            if (key?.GetValue("pv") is string pv && System.Version.TryParse(pv, out var v) && v >= new System.Version(92, 0, 902, 49)) return true;
        }
        return false;
    }

    /// <summary>Installs the WebView2 runtime the app draws its window with (missing on some Windows 10 PCs). Microsoft's offline installer beside this setup is
    /// used when it is there and signed by Microsoft (a shop's flash drive, or where Microsoft's servers are slow); otherwise the small bootstrapper this setup
    /// carries downloads it. A failure does not undo the install: the app then names the missing runtime itself.</summary>
    private static bool InstallWebView(IProgress<(int Percent, string Text)> progress)
    {
        progress.Report((97, SetupText.WebView));
        string here = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetEntryAssembly()!.Location))!;
        string? exe = Directory.EnumerateFiles(here, "MicrosoftEdgeWebView2RuntimeInstaller" + (Environment.Is64BitOperatingSystem ? "X64" : "X86") + ".exe").FirstOrDefault(MicrosoftSigned);
        string? temp = null;
        try
        {
            if (exe is null)
            {
                using var boot = typeof(Installer).Assembly.GetManifestResourceStream("webview2.exe");
                if (boot is null) return false;
                temp = Path.Combine(Path.GetTempPath(), "mazesta-webview2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp); exe = Path.Combine(temp, "MicrosoftEdgeWebview2Setup.exe");
                using (var f = File.Create(exe)) boot.CopyTo(f);
            }
            using var p = Process.Start(new ProcessStartInfo(exe, "/silent /install") { UseShellExecute = false, CreateNoWindow = true })!;
            if (!p.WaitForExit(15 * 60 * 1000)) { try { p.Kill(); } catch (InvalidOperationException) { } return false; }
            return WebViewReady();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
        finally { if (temp is not null) try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    /// <summary>Whether a file's Authenticode signature is intact and Microsoft's: setup runs elevated, so an exe someone left beside it is never run unchecked.</summary>
    private static bool MicrosoftSigned(string file)
    {
        if (Trust.Verify(file) != 0) return false;
        try { return System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file).Subject.Contains("O=Microsoft Corporation"); }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    /// <summary>WinVerifyTrust with the Authenticode policy: 0 when the file's signature is valid and its chain trusted.</summary>
    private static class Trust
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct FileInfo { public int Size; public string Path; public IntPtr Handle, Subject; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Data { public int Size; public IntPtr Policy, Sip; public int Ui, Revocation, Choice; public IntPtr File; public int Action; public IntPtr State; public IntPtr Url; public int Provider, Context; }
        [System.Runtime.InteropServices.DllImport("wintrust.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [System.Runtime.InteropServices.In] ref Guid action, [System.Runtime.InteropServices.In] ref Data data);
        private static readonly Guid Authenticode = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static int Verify(string file)
        {
            var fi = new FileInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<FileInfo>(), Path = file };
            IntPtr pFile = System.Runtime.InteropServices.Marshal.AllocHGlobal(fi.Size);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(fi, pFile, false);
                var d = new Data { Size = System.Runtime.InteropServices.Marshal.SizeOf<Data>(), Ui = 2, Choice = 1, File = pFile, Action = 1 };   // no UI, a file, verify
                var g = Authenticode; int r = WinVerifyTrust(IntPtr.Zero, ref g, ref d);
                d.Action = 2; WinVerifyTrust(IntPtr.Zero, ref g, ref d);   // close the state it opened
                return r;
            }
            finally { System.Runtime.InteropServices.Marshal.DestroyStructure<FileInfo>(pFile); System.Runtime.InteropServices.Marshal.FreeHGlobal(pFile); }
        }
    }

    /// <summary>Copies the Data an earlier per-user copy kept beside its exe into the profile, once: never over data already there, and without the caches.</summary>
    private static void MoveData(string oldData)
    {
        string target = Path.Combine(ProfileRoot, "Data");
        if (!Directory.Exists(oldData) || Directory.Exists(target)) return;
        foreach (var file in Directory.EnumerateFiles(oldData, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(oldData.Length).TrimStart('\\');
            if (rel.StartsWith("cache\\", StringComparison.OrdinalIgnoreCase)) continue;
            string to = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
        }
    }

    /// <summary>The earlier per-user copy's program files, shortcuts and registry entry. Its Data folder is left where it is (it was copied to the profile).</summary>
    private static void RemoveEarlier(string old, string now)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName + ".lnk")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (string.Equals(Path.GetFullPath(old).TrimEnd('\\'), now.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(old).Where(e => !string.Equals(Path.GetFileName(e), "Data", StringComparison.OrdinalIgnoreCase)))
            { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Shortcut(string path, string target, string workDir)
    {
        var shell = Type.GetTypeFromProgID("WScript.Shell") ?? throw new PlatformNotSupportedException("WScript.Shell");
        dynamic sh = Activator.CreateInstance(shell)!;
        try { dynamic link = sh.CreateShortcut(path); link.TargetPath = target; link.WorkingDirectory = workDir; link.IconLocation = target + ",0"; link.Description = "Mazesta Test"; link.Save(); }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); }
    }
}
