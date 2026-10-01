using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>A driver Windows Update offers this computer and has not installed.</summary>
public sealed record DriverUpdate(string Id, string Title, string? DriverClass, string? Model, string? Provider, DateTime? Date, long? Bytes, bool NeedsLicense);

/// <summary>What happened to one driver: Windows Update's own result code (2 succeeded, 3 succeeded with errors, 4 failed, 5 aborted).</summary>
public sealed record DriverInstallResult(string Id, string Title, int ResultCode, bool RebootRequired, string? Error)
{
    public bool Succeeded => ResultCode is 2 or 3;
}

/// <summary>
/// The drivers Windows Update has for this computer, through Windows' own Update Agent (the COM API Settings › Windows Update uses): the same
/// signed drivers Microsoft distributes, so nothing comes from a third party. The search asks Microsoft's servers (not a company's WSUS), and
/// includes the optional drivers. Installing goes one driver at a time, so a failure is told for that driver and a cancel stops between two.
/// A driver whose licence must be accepted is not installed here: the user accepts it in Windows Update.
/// </summary>
public static class WindowsDriverUpdates
{
    private const int ServerWindowsUpdate = 2;
    private const string Criteria = "IsInstalled=0 and Type='Driver' and IsHidden=0";

    public static Task<IReadOnlyList<DriverUpdate>> SearchAsync(CancellationToken ct) => Task.Run(() =>
    {
        var (_, updates) = Search();
        ct.ThrowIfCancellationRequested();
        var list = new List<DriverUpdate>();
        foreach (dynamic u in updates)
        {
            DateTime? date = null; try { date = (DateTime)u.DriverVerDate; } catch (Exception e) when (e is InvalidCastException or COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            long? bytes = null; try { bytes = (long)(decimal)u.MaxDownloadSize; } catch (Exception e) when (e is InvalidCastException or COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            list.Add(new((string)u.Identity.UpdateID, (string)u.Title, Text(() => u.DriverClass), Text(() => u.DriverModel), Text(() => u.DriverProvider), date, bytes, !(bool)u.EulaAccepted));
        }
        return (IReadOnlyList<DriverUpdate>)list;
    }, ct);

    /// <summary>Downloads and installs the drivers asked for, one by one; <paramref name="progress"/> gets each one's index and title as it starts.</summary>
    public static Task<IReadOnlyList<DriverInstallResult>> InstallAsync(IReadOnlyCollection<string> ids, IProgress<(int Index, int Count, string Title)>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var (session, updates) = Search();
        var chosen = new List<dynamic>();
        foreach (dynamic u in updates) if (ids.Contains((string)u.Identity.UpdateID)) chosen.Add(u);
        var results = new List<DriverInstallResult>();
        for (int i = 0; i < chosen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            dynamic u = chosen[i]; string id = u.Identity.UpdateID, title = u.Title;
            progress?.Report((i, chosen.Count, title));
            if (!(bool)u.EulaAccepted) { results.Add(new(id, title, 4, false, "licence")); continue; }
            try
            {
                dynamic one = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true)!)!; one.Add(u);
                dynamic downloader = session.CreateUpdateDownloader(); downloader.Updates = one; downloader.Download();
                dynamic installer = session.CreateUpdateInstaller(); installer.Updates = one;
                dynamic result = installer.Install(); dynamic r = result.GetUpdateResult(0);
                int code = (int)r.ResultCode; int hr = (int)r.HResult;
                results.Add(new(id, title, code, (bool)r.RebootRequired, code is 2 or 3 ? null : $"0x{hr:X8}"));
            }
            catch (COMException e) { results.Add(new(id, title, 4, false, $"0x{e.HResult:X8}")); }
        }
        return (IReadOnlyList<DriverInstallResult>)results;
    }, CancellationToken.None);

    private static (dynamic Session, dynamic Updates) Search()
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!)!;
        session.ClientApplicationID = "Mazesta Test";
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.ServerSelection = ServerWindowsUpdate; searcher.Online = true;
        dynamic result = searcher.Search(Criteria);
        return (session, result.Updates);
    }

    private static string? Text(Func<object?> read)
    {
        try { return read() is string s && s.Trim().Length > 0 ? s.Trim() : null; }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return null; }
    }
}
