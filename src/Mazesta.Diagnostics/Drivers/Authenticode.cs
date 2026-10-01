using System.Runtime.InteropServices; using System.Security.Cryptography.X509Certificates;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>WinVerifyTrust with the Authenticode policy: the file's signature is whole and chains to a root Windows trusts; and who signed it.</summary>
public static class Authenticode
{
    private static readonly Guid Action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo { public int Size; public string Path; public IntPtr Handle; public IntPtr Subject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Data
    {
        public int Size; public IntPtr Policy, Sip; public int UiChoice, Revocation, UnionChoice; public IntPtr File; public int StateAction; public IntPtr State;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Url; public int ProvFlags, UiContext; public IntPtr Signature;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref Data data);

    public static string? Verify(string path)
    {
        var info = new FileInfo { Size = Marshal.SizeOf<FileInfo>(), Path = path };
        IntPtr pInfo = Marshal.AllocHGlobal(info.Size);
        try
        {
            Marshal.StructureToPtr(info, pInfo, false);
            // No UI, whole-chain revocation check, a file subject, verify then close the state.
            var data = new Data { Size = Marshal.SizeOf<Data>(), UiChoice = 2, Revocation = 0, UnionChoice = 1, File = pInfo, StateAction = 1 };
            int result = WinVerifyTrust(new IntPtr(-1), Action, ref data);
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), Action, ref data);
            return result == 0 ? null : $"Windows does not trust the signature (0x{result:X8})";
        }
        finally { Marshal.DestroyStructure<FileInfo>(pInfo); Marshal.FreeHGlobal(pInfo); }
    }

    /// <summary>The name on the certificate the file was signed with, or null when it carries none. Only the name: the trust is <see cref="Verify"/>'s.</summary>
    public static string? Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057   // only the signer's name is read here; the trust itself is WinVerifyTrust's answer
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
