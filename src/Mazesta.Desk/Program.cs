namespace Mazesta.Desk;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\MazestaDesk", out bool first);
        if (!first && !args.Any(a => a.StartsWith("--selftest", StringComparison.Ordinal))) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault(a => a.StartsWith("--selftest", StringComparison.Ordinal))));
    }
}
