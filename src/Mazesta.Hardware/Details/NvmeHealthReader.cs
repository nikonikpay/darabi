using System.Globalization; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles; using Mazesta.Core.Providers; using Mazesta.Hardware.Wmi;
namespace Mazesta.Hardware.Details;

/// <summary>
/// Reads each NVMe drive's health log (page 02h) through Windows' own NVMe pass-through for logs: IOCTL_STORAGE_QUERY_PROPERTY with a
/// protocol-specific property (the way Microsoft documents for the inbox StorNVMe driver). It only reads; it needs the app's administrator
/// rights to open the disk. A drive whose driver does not support the query is left out.
/// </summary>
public sealed class NvmeHealthReader(IWmiQuery query) : INvmeHealthSource
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    private const uint IoctlStorageQueryProperty = 0x002D1400, GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
    private const int DeviceProtocolSpecificProperty = 50, AdapterProtocolSpecificProperty = 49, ProtocolTypeNvme = 3, NvmeDataTypeLogPage = 2, HealthLogPage = 2;
    private const int Header = 8, SpecificData = 40;

    public IReadOnlyList<NvmeDriveHealth> Read()
    {
        var list = new List<NvmeDriveHealth>();
        foreach (var d in query.Query(Storage, "SELECT DeviceId,FriendlyName,SerialNumber,BusType FROM MSFT_PhysicalDisk"))
        {
            if (Convert.ToInt32(d.GetValueOrDefault("BusType"), CultureInfo.InvariantCulture) != 17) continue;   // 17 = NVMe
            if (!int.TryParse(Convert.ToString(d.GetValueOrDefault("DeviceId"), CultureInfo.InvariantCulture), out int number)) continue;
            if (ReadLog(number) is { } log) list.Add(new(number, d.GetValueOrDefault("FriendlyName") as string, (d.GetValueOrDefault("SerialNumber") as string)?.Trim(), log));
        }
        return list;
    }

    /// <summary>The health log of \\.\PhysicalDrive<paramref name="disk"/>, or null when the drive or its driver will not give it.</summary>
    public static NvmeHealthLog? ReadLog(int disk)
    {
        using var handle = CreateFile($@"\\.\PhysicalDrive{disk}", GenericRead | GenericWrite, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid) return null;
        foreach (int property in new[] { DeviceProtocolSpecificProperty, AdapterProtocolSpecificProperty })
        {
            var buffer = new byte[Header + SpecificData + NvmeHealthLog.Size];
            // STORAGE_PROPERTY_QUERY { PropertyId, QueryType = standard } followed by STORAGE_PROTOCOL_SPECIFIC_DATA.
            int[] fields = [property, 0, ProtocolTypeNvme, NvmeDataTypeLogPage, HealthLogPage, 0, SpecificData, NvmeHealthLog.Size, 0, 0, 0, 0];
            Buffer.BlockCopy(fields, 0, buffer, 0, fields.Length * 4);
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out int returned, 0) || returned < Header + SpecificData) continue;
            // The answer is a STORAGE_PROTOCOL_DATA_DESCRIPTOR { Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA }: the data sits at the offset it names.
            int offset = BitConverter.ToInt32(buffer, Header + 16), length = BitConverter.ToInt32(buffer, Header + 20);
            if (length < NvmeHealthLog.Size || Header + offset + NvmeHealthLog.Size > buffer.Length) continue;
            return NvmeHealthLog.Parse(buffer.AsSpan(Header + offset, NvmeHealthLog.Size));
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, nint overlapped);
}
