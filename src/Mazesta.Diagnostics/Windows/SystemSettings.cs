using System.Net; using System.Text; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// Hibernation and Fast Startup as the registry has them. Fast Startup is a hibernation of the kernel session, so it only works while hibernation
/// is on: <c>powercfg /hibernate off</c> turns both off (and deletes hiberfil.sys); turning hibernation back on brings Fast Startup back as it was
/// set. Null means the value is not in the registry (Windows then uses its default).
/// </summary>
public sealed record HibernateStatus(bool? Hibernate, bool? FastStartup)
{
    private const string Power = @"SYSTEM\CurrentControlSet\Control\Power", SessionPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    public static HibernateStatus Read()
    {
        static int? Dword(string path, string name) { using var key = Registry.LocalMachine.OpenSubKey(path); return key?.GetValue(name) is int v ? v : null; }
        int? hibernate = Dword(Power, "HibernateEnabled") ?? Dword(Power, "HibernateEnabledDefault");
        int? fast = Dword(SessionPower, "HiberbootEnabled");
        return new(hibernate is null ? null : hibernate != 0, fast is null ? null : fast != 0 && hibernate != 0);
    }

    /// <summary>On: <c>powercfg /hibernate on</c> and Fast Startup set on (Windows' default). Off: <c>powercfg /hibernate off</c>, which turns
    /// both off. Needs administrator rights (the app runs elevated). Returns powercfg's output when it failed, null when it worked.</summary>
    public static async Task<string?> SetAsync(ICommandRunner runner, bool on, CancellationToken ct)
    {
        var result = await runner.RunAsync("powercfg.exe", on ? "/hibernate on" : "/hibernate off", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return string.Join(Environment.NewLine, result.Output).Trim() is { Length: > 0 } o ? o : $"powercfg exit code {result.ExitCode}";
        if (on) { using var key = Registry.LocalMachine.OpenSubKey(SessionPower, writable: true); key?.SetValue("HiberbootEnabled", 1, RegistryValueKind.DWord); }
        return null;
    }
}

/// <summary>
/// Windows' hosts file: read, checked line by line, and saved with the previous version kept next to it (hosts.mazesta-backup), then the DNS
/// cache flushed so the change applies at once. A line is a comment, blank, or an address followed by one or more host names.
/// </summary>
public static class HostsFile
{
    public static string PathOf() => System.IO.Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
    public static string BackupOf(string path) => path + ".mazesta-backup";

    public sealed record Line(int Number, string Text, string Problem);

    /// <summary>The lines that are neither blank, a comment, nor "address name [name…] [# comment]": what Windows would silently ignore.</summary>
    public static IReadOnlyList<Line> Check(string text)
    {
        var bad = new List<Line>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i]; int hash = l.IndexOf('#', StringComparison.Ordinal);
            string body = (hash >= 0 ? l[..hash] : l).Trim();
            if (body.Length == 0) continue;
            var parts = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (!IPAddress.TryParse(parts[0], out _)) bad.Add(new(i + 1, l, "address"));
            else if (parts.Length < 2) bad.Add(new(i + 1, l, "name"));
        }
        return bad;
    }

    /// <summary>The number of active entries (address and name lines), for the page's summary.</summary>
    public static int Entries(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
        .Select(l => (l.IndexOf('#', StringComparison.Ordinal) is var h and >= 0 ? l[..h] : l).Trim()).Count(b => b.Length > 0);

    public static string Read(string path) => File.ReadAllText(path);

    /// <summary>Writes the new text (Windows line endings, no BOM: some tools read a BOM as part of the first address), keeping the file as it was
    /// in the backup first. A read-only hosts file is made writable for the save and read-only again after.</summary>
    public static void Save(string path, string text)
    {
        string normal = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var attributes = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;
        bool readOnly = attributes.HasFlag(FileAttributes.ReadOnly);
        if (File.Exists(path)) File.Copy(path, BackupOf(path), overwrite: true);
        if (readOnly) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        try { File.WriteAllText(path, normal, new UTF8Encoding(false)); }
        finally { if (readOnly) File.SetAttributes(path, attributes); }
    }

    public static Task<CommandResult> FlushDnsAsync(ICommandRunner runner, CancellationToken ct) => runner.RunAsync("ipconfig.exe", "/flushdns", WindowsTool.Oem, null, ct);
}
