using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The plain Win32 window the visual GPU tests draw into (the way FurMark shows its test): created, pumped and destroyed on the one thread
/// that renders, so the test needs nothing from WPF or WebView2. Closing it (the X, Alt+F4 or Esc) ends the test as cancelled by the
/// technician. It is fixed-size - a resize mid-test would change the work per frame - or covers the whole screen without a border.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private const string ClassName = "MazestaGpuTestWindow";
    private static readonly WndProc Proc = WindowProc;   // kept alive: Windows calls it for as long as the class is registered
    private static readonly Lazy<ushort> ClassAtom = new(() =>
    {
        var wc = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), Style = 0x0003 /* CS_HREDRAW|CS_VREDRAW */, WndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            Instance = GetModuleHandle(null), Cursor = LoadCursor(IntPtr.Zero, 32512), ClassName = ClassName, Icon = ExtractIcon() };
        return RegisterClassEx(ref wc);
    });
    private static readonly HashSet<IntPtr> Closed = [];

    public IntPtr Handle { get; }
    public int Width { get; }
    public int Height { get; }

    public TestWindow(string title, int width, int height, bool fullScreen)
    {
        _ = ClassAtom.Value;
        int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
        uint style, exStyle = 0; int x = 0, y = 0, w = screenW, h = screenH;
        if (fullScreen) { style = 0x80000000 | 0x10000000; exStyle = 0x00000008; }   // WS_POPUP | WS_VISIBLE, WS_EX_TOPMOST
        else
        {
            style = 0x00C00000 | 0x00080000 | 0x00020000 | 0x10000000;   // WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VISIBLE
            var r = new Rect { Right = Math.Min(width, screenW), Bottom = Math.Min(height, screenH) };
            AdjustWindowRect(ref r, style, false);
            w = r.Right - r.Left; h = r.Bottom - r.Top; x = Math.Max(0, (screenW - w) / 2); y = Math.Max(0, (screenH - h) / 2);
        }
        Handle = CreateWindowEx(exStyle, ClassName, title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException($"The test window could not be created (error {Marshal.GetLastPInvokeError()}).");
        GetClientRect(Handle, out var client);
        Width = client.Right - client.Left; Height = client.Bottom - client.Top;
        SetForegroundWindow(Handle);
    }

    /// <summary>Handles every waiting message; false once the technician closed the window.</summary>
    public bool Pump()
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */)) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        lock (Closed) return !Closed.Contains(Handle);
    }

    public string Title { set => SetWindowText(Handle, value); }

    public void Dispose()
    {
        lock (Closed) Closed.Remove(Handle);
        DestroyWindow(Handle);
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        const uint WmClose = 0x0010, WmKeyDown = 0x0100, WmEraseBkgnd = 0x0014;
        if (msg == WmClose || (msg == WmKeyDown && w == 0x1B /* Esc */)) { lock (Closed) Closed.Add(hwnd); return IntPtr.Zero; }
        if (msg == WmEraseBkgnd) return 1;   // the swap chain paints every pixel; erasing first only flickers
        return DefWindowProc(hwnd, msg, w, l);
    }

    private static IntPtr ExtractIcon() { try { return ExtractIconW(GetModuleHandle(null), Environment.ProcessPath ?? "", 0); } catch (EntryPointNotFoundException) { return IntPtr.Zero; } }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClassEx
    {
        public uint Size, Style; public IntPtr WndProc; public int ClsExtra, WndExtra; public IntPtr Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public IntPtr IconSmall;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref Rect rect, uint style, bool menu);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconW")] private static extern IntPtr ExtractIconW(IntPtr instance, string path, uint index);
}
