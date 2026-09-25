using System.Diagnostics; using System.Text;
namespace Mazesta.Diagnostics.Windows;

public sealed record CommandResult(int ExitCode, IReadOnlyList<string> Output);

/// <summary>Runs a Windows command-line tool (sfc, DISM, powercfg) and hands each line of its output to the caller as it comes - the seam that
/// lets the tools' result parsing be tested without running them.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string file, string arguments, Encoding output, Action<string>? line, CancellationToken ct);
}

/// <summary>Runs the tool hidden, without a shell, from System32. Cancelling stops the tool (and anything it started); the caller decides whether
/// a tool may be cancelled at all.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string file, string arguments, Encoding output, Action<string>? line, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file), arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = output, StandardErrorEncoding = output
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start.");
        var lines = new List<string>();
        void Collect(string? text) { if (string.IsNullOrWhiteSpace(text)) return; text = text.Replace("\0", "").Trim(); if (text.Length == 0) return; lock (lines) lines.Add(text); line?.Invoke(text); }
        process.OutputDataReceived += (_, e) => Collect(e.Data); process.ErrorDataReceived += (_, e) => Collect(e.Data);
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        process.WaitForExit();   // lets the output readers drain
        lock (lines) return new(process.ExitCode, [.. lines]);
    }
}
