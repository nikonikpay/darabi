using System.Diagnostics; using System.Runtime.InteropServices;
namespace Mazesta.Hardware.Audio;

/// <summary>A program playing (or ready to play) sound on an output device: all its sessions together, as one slider.</summary>
public sealed record AudioApp(string Key, string Name, string? Path, IReadOnlyList<int> Pids, float Volume, bool Muted, bool Playing, bool System);

/// <summary>An output device and its own (master) volume.</summary>
public sealed record AudioDevice(string Id, string Name, float Volume, bool Muted, bool Default);

/// <summary>
/// Each program's volume, as Windows' volume mixer has it, through Core Audio (the documented API: IAudioSessionManager2 and
/// ISimpleAudioVolume). A program with several sessions (a browser's tabs, several windows) is one row, set as a whole. Nothing is kept
/// between calls: each one asks Windows afresh, so a program that started or ended since is right.
/// </summary>
public static class AppVolumes
{
    private static readonly Guid Context = Guid.NewGuid();   // marks our own changes (the callbacks of other programs see who changed it)

    /// <summary>The output devices in use, the default one first.</summary>
    public static IReadOnlyList<AudioDevice> Devices()
    {
        var e = Enumerator(); var list = new List<AudioDevice>();
        string? def = Default(e)?.Id;
        Check(e.EnumAudioEndpoints(0 /* render */, 1 /* active */, out var all));
        Check(all.GetCount(out uint n));
        for (uint i = 0; i < n; i++)
        {
            if (all.Item(i, out var d) != 0) continue;
            d.GetId(out string id); var v = Endpoint(d);
            v.GetMasterVolumeLevelScalar(out float level); v.GetMute(out bool mute);
            list.Add(new AudioDevice(id, FriendlyName(d) ?? id, level, mute, id == def));
        }
        return [.. list.OrderByDescending(d => d.Default)];
    }

    /// <summary>The programs with sound on a device (the default one when none is named): those playing first, Windows' own sounds last.</summary>
    public static IReadOnlyList<AudioApp> Apps(string? deviceId = null)
    {
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var (control, volume) in Sessions(deviceId))
        {
            control.GetProcessId(out uint pid); control.GetState(out int state);
            bool system = control.IsSystemSoundsSession() == 0;
            string? path = system ? null : ExePath((int)pid);
            string key = system ? "system" : path ?? $"pid:{pid}";
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new Group(system ? "" : AppName(control, (int)pid, path), path, system);
            g.Pids.Add((int)pid); g.Volumes.Add(volume); g.Playing |= state == 1 /* active */;
        }
        var apps = new List<AudioApp>();
        foreach (var (key, g) in groups)
        {
            g.Volumes[0].GetMasterVolume(out float level); g.Volumes[0].GetMute(out bool mute);
            apps.Add(new AudioApp(key, g.Name, g.Path, [.. g.Pids.Distinct()], level, mute, g.Playing, g.System));
        }
        return [.. apps.OrderBy(a => a.System).ThenByDescending(a => a.Playing).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Every session on a device, one line each (process, state, volume, Windows' id for it): for a log or a check, not for the page.</summary>
    public static IReadOnlyList<string> Describe(string? deviceId = null)
    {
        var lines = new List<string>();
        foreach (var (c, v) in Sessions(deviceId))
        {
            c.GetProcessId(out uint pid); c.GetState(out int state); v.GetMasterVolume(out float level); v.GetMute(out bool mute); c.GetSessionInstanceIdentifier(out string? id);
            lines.Add($"pid {pid} state {state} volume {level:0.00}{(mute ? " muted" : "")} {id}");
        }
        return lines;
    }

    private sealed class Group(string name, string? path, bool system)
    {
        public string Name { get; } = name; public string? Path { get; } = path; public bool System { get; } = system; public bool Playing;
        public List<int> Pids { get; } = []; public List<ISimpleAudioVolume> Volumes { get; } = [];
    }

    /// <summary>A program's volume (0 to 1) on every one of its sessions.</summary>
    public static void SetApp(string key, float volume, string? deviceId = null) => Each(key, deviceId, v => v.SetMasterVolume(Math.Clamp(volume, 0f, 1f), Context));
    public static void MuteApp(string key, bool mute, string? deviceId = null) => Each(key, deviceId, v => v.SetMute(mute, Context));

    public static void SetDevice(string? deviceId, float volume) => Check(Endpoint(Device(deviceId)).SetMasterVolumeLevelScalar(Math.Clamp(volume, 0f, 1f), Context));
    public static void MuteDevice(string? deviceId, bool mute) => Check(Endpoint(Device(deviceId)).SetMute(mute, Context));

    private static void Each(string key, string? deviceId, Func<ISimpleAudioVolume, int> set)
    {
        foreach (var (control, volume) in Sessions(deviceId))
        {
            control.GetProcessId(out uint pid);
            string k = control.IsSystemSoundsSession() == 0 ? "system" : ExePath((int)pid) ?? $"pid:{pid}";
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) set(volume);
        }
    }

    private static IEnumerable<(IAudioSessionControl2 Control, ISimpleAudioVolume Volume)> Sessions(string? deviceId)
    {
        var device = Device(deviceId);
        var iid = typeof(IAudioSessionManager2).GUID;
        Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out object o));
        var manager = (IAudioSessionManager2)o;
        Check(manager.GetSessionEnumerator(out var sessions)); Check(sessions.GetCount(out int n));
        for (int i = 0; i < n; i++)
        {
            if (sessions.GetSession(i, out var s) != 0 || s is not IAudioSessionControl2 c2) continue;
            c2.GetState(out int state);
            if (state == 2 /* expired: the program has gone */) continue;
            yield return (c2, (ISimpleAudioVolume)s);
        }
    }

    private static IMMDevice Device(string? id)
    {
        var e = Enumerator();
        if (id is null) return Default(e) is { } d ? d.Device : throw new InvalidOperationException("No sound output device.");
        Check(e.GetDevice(id, out var device)); return device;
    }

    private static (IMMDevice Device, string Id)? Default(IMMDeviceEnumerator e)
    {
        if (e.GetDefaultAudioEndpoint(0, 1 /* multimedia */, out var d) != 0) return null;
        d.GetId(out string id); return (d, id);
    }

    private static IAudioEndpointVolume Endpoint(IMMDevice d) { var iid = typeof(IAudioEndpointVolume).GUID; Check(d.Activate(ref iid, 23, 0, out object o)); return (IAudioEndpointVolume)o; }

    private static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;

    private static string? FriendlyName(IMMDevice d)
    {
        if (d.OpenPropertyStore(0, out var store) != 0) return null;
        var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };   // PKEY_Device_FriendlyName
        if (store.GetValue(ref key, out var v) != 0) return null;
        try { return v.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(v.Pointer) : null; } finally { PropVariantClear(ref v); }
    }

    private static string? ExePath(int pid)
    {
        if (pid == 0) return null;
        try { using var p = Process.GetProcessById(pid); return p.MainModule?.FileName; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
    }

    // the session's own name when the program gave one ("@%SystemRoot%\..." resource names are not), else the program's description, else its file name
    private static string AppName(IAudioSessionControl2 c, int pid, string? path)
    {
        if (c.GetDisplayName(out string? shown) == 0 && !string.IsNullOrWhiteSpace(shown) && !shown.StartsWith('@')) return shown;
        if (path is not null)
        {
            try { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d.Trim(); } catch (FileNotFoundException) { }
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch (ArgumentException) { return $"PID {pid}"; }
    }

    private static void Check(int hr) { if (hr != 0) Marshal.ThrowExceptionForHR(hr); }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant v);

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public int Id; }
    [StructLayout(LayoutKind.Sequential)] private struct PropVariant { public ushort Type; public ushort R1, R2, R3; public nint Pointer; public nint Pad; }

    // The Core Audio interfaces, each with its methods in the order of its vtable (a base interface's first).
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float db, in Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, in Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, in Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, in Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(nint group, int flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint group, int flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);   // the IAudioSessionControl part
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, in Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, in Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(in Guid group, in Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notify);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notify);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);   // IAudioSessionControl2's own
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, in Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
