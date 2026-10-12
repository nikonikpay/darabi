using System.Runtime.InteropServices; using System.Runtime.InteropServices.Marshalling;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

// The few Media Foundation interfaces the video benchmark uses, declared by hand (no package for it is referenced). A derived interface repeats nothing:
// the source generator keeps the base's vtable first. Methods the benchmark never calls are declared with loose types only to hold their slot.

[GeneratedComInterface, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
internal partial interface IMFAttributes
{
    void GetItem(in Guid key, nint value); void GetItemType(in Guid key, out int type); void CompareItem(in Guid key, nint value, out int result); void Compare(nint theirs, int matchType, out int result);
    void GetUINT32(in Guid key, out uint value); void GetUINT64(in Guid key, out ulong value); void GetDouble(in Guid key, out double value); void GetGUID(in Guid key, out Guid value);
    void GetStringLength(in Guid key, out uint length); void GetString(in Guid key, nint buffer, uint size, nint length); void GetAllocatedString(in Guid key, out nint value, out uint length);
    void GetBlobSize(in Guid key, out uint size); void GetBlob(in Guid key, nint buffer, uint size, nint written); void GetAllocatedBlob(in Guid key, out nint buffer, out uint size); void GetUnknown(in Guid key, in Guid riid, out nint unknown);
    void SetItem(in Guid key, nint value); void DeleteItem(in Guid key); void DeleteAllItems();
    void SetUINT32(in Guid key, uint value); void SetUINT64(in Guid key, ulong value); void SetDouble(in Guid key, double value); void SetGUID(in Guid key, in Guid value);
    void SetString(in Guid key, nint value); void SetBlob(in Guid key, nint buffer, uint size); void SetUnknown(in Guid key, IMFDXGIDeviceManager unknown);
    void LockStore(); void UnlockStore(); void GetCount(out uint count); void GetItemByIndex(uint index, out Guid key, nint value); void CopyAllItems(nint destination);
}

[GeneratedComInterface, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
internal partial interface IMFSample : IMFAttributes
{
    void GetSampleFlags(out uint flags); void SetSampleFlags(uint flags); void GetSampleTime(out long time); void SetSampleTime(long time); void GetSampleDuration(out long duration); void SetSampleDuration(long duration);
    void GetBufferCount(out uint count); void GetBufferByIndex(uint index, out IMFMediaBuffer buffer); void ConvertToContiguousBuffer(out IMFMediaBuffer buffer); void AddBuffer(IMFMediaBuffer buffer);
    void RemoveBufferByIndex(uint index); void RemoveAllBuffers(); void GetTotalLength(out uint length); void CopyToBuffer(IMFMediaBuffer buffer);
}

[GeneratedComInterface, Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
internal partial interface IMFMediaBuffer
{
    void Lock(out nint data, out uint maxLength, out uint currentLength); void Unlock(); void GetCurrentLength(out uint length); void SetCurrentLength(uint length); void GetMaxLength(out uint length);
}

[GeneratedComInterface, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
internal partial interface IMFSinkWriter
{
    void AddStream(IMFAttributes mediaType, out uint streamIndex); void SetInputMediaType(uint streamIndex, IMFAttributes mediaType, IMFAttributes? encodingParameters); void BeginWriting();
    void WriteSample(uint streamIndex, IMFSample sample); void SendStreamTick(uint streamIndex, long timestamp); void PlaceMarker(uint streamIndex, nint context); void NotifyEndOfSegment(uint streamIndex);
    void Flush(uint streamIndex); void FinalizeWriter();
}

[GeneratedComInterface, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
internal partial interface IMFSourceReader
{
    void GetStreamSelection(uint streamIndex, out int selected); void SetStreamSelection(uint streamIndex, int selected); void GetNativeMediaType(uint streamIndex, uint typeIndex, out IMFAttributes mediaType);
    void GetCurrentMediaType(uint streamIndex, out IMFAttributes mediaType); void SetCurrentMediaType(uint streamIndex, nint reserved, IMFAttributes mediaType); void SetCurrentPosition(in Guid timeFormat, nint position);
    void ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out nint sample);   // a raw pointer: a decoder's surfaces must go back at once, not when the garbage collector finalises a wrapper
}

/// <summary>The D3D11 device handed to the decoders and encoders, so that they work on one chosen card.</summary>
[GeneratedComInterface, Guid("EB533D5D-2DB6-40F8-97A9-494692014F07")]
internal partial interface IMFDXGIDeviceManager
{
    void CloseDeviceHandle(nint handle); void GetVideoService(nint handle, in Guid riid, out nint service); void LockDevice(nint handle, in Guid riid, out nint device, int block);
    void OpenDeviceHandle(out nint handle); void ResetDevice(nint device, uint token);
}

internal static partial class Mf
{
    public const uint FirstVideoStream = 0xFFFFFFFC, EndOfStream = 2, Version = 0x00020070;
    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e"),
        FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"),
        InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"), Video = new("73646976-0000-0010-8000-00aa00389b71"),
        H264 = new("34363248-0000-0010-8000-00aa00389b71"), Hevc = new("43564548-0000-0010-8000-00aa00389b71"), Av1 = new("31305641-0000-0010-8000-00aa00389b71"), Nv12 = new("3231564e-0000-0010-8000-00aa00389b71"),
        EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0"), SinkD3dManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269"), SourceD3dManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269"),
        FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3"), VideoEncoder = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    private const uint Hardware = 0x4, SortAndFilter = 0x40;

    [StructLayout(LayoutKind.Sequential)] private struct TypeInfo { public Guid Major, Sub; }

    [LibraryImport("mfplat.dll")] public static partial int MFStartup(uint version, uint flags);
    [LibraryImport("mfplat.dll")] public static partial int MFShutdown();
    [LibraryImport("mfplat.dll")] public static partial int MFCreateAttributes(out IMFAttributes attributes, uint size);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateMediaType(out IMFAttributes mediaType);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateSample(out IMFSample sample);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager manager);
    [LibraryImport("mfplat.dll")] private static partial int MFTEnumEx(in Guid category, uint flags, nint input, in TypeInfo output, out nint activates, out uint count);
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int MFCreateSinkWriterFromURL(string url, nint byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);
    [LibraryImport("ole32.dll")] private static partial void CoTaskMemFree(nint memory);
    [LibraryImport("d3d11.dll")] private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint levelCount, uint sdk, out nint device, out int level, out nint context);

    /// <summary>Gives a COM object back now. A decoder has a fixed pool of video surfaces and waits for the program to return them: leaving that to the garbage collector hangs it.</summary>
    public static void Release(object? com) { if (com is ComObject o) o.FinalRelease(); }

    public static void Check(int hr, string what) { if (hr < 0) throw new InvalidOperationException($"{what} failed (0x{hr:X8})."); }

    /// <summary>The hardware video encoder Media Foundation offers for a codec, by its friendly name; null when the driver has none.</summary>
    public static string? HardwareEncoder(Guid codec)
    {
        var info = new TypeInfo { Major = Video, Sub = codec };
        if (MFTEnumEx(VideoEncoder, Hardware | SortAndFilter, 0, info, out nint array, out uint count) < 0 || count == 0 || array == 0) return null;
        try
        {
            string? first = null;
            for (int i = 0; i < count; i++)
            {
                nint unknown = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (i == 0 && unknown != 0 && Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None) is IMFAttributes a)
                {
                    a.GetAllocatedString(FriendlyName, out nint text, out _);
                    first = Marshal.PtrToStringUni(text); CoTaskMemFree(text);
                }
                if (unknown != 0) Marshal.Release(unknown);
            }
            return first ?? "hardware encoder";
        }
        finally { CoTaskMemFree(array); }
    }

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>A Direct3D 11 device on the adapter (video support on), wrapped in the manager Media Foundation takes it through.</summary>
    public static unsafe IMFDXGIDeviceManager DeviceManager(nint adapter, out nint device)
    {
        Check(D3D11CreateDevice(adapter, adapter == 0 ? 1 : 0, 0, 0x800 | 0x20 /* VIDEO_SUPPORT | BGRA_SUPPORT */, 0, 0, 7, out device, out _, out nint context), "D3D11CreateDevice");
        Marshal.Release(context);
        // The device is used from Media Foundation's own threads: the runtime must serialise its calls (ID3D10Multithread::SetMultithreadProtected, slot 5).
        Guid mt = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
        if (Marshal.QueryInterface(device, in mt, out nint multithread) == 0) { ((delegate* unmanaged<nint, int, int>)((nint*)*(nint*)multithread)[5])(multithread, 1); Marshal.Release(multithread); }
        Check(MFCreateDXGIDeviceManager(out uint token, out var manager), "MFCreateDXGIDeviceManager");
        manager.ResetDevice(device, token);
        return manager;
    }

    public static IMFAttributes VideoType(Guid subtype, int width, int height, int fps, int bitrate = 0)
    {
        Check(MFCreateMediaType(out var t), "MFCreateMediaType");
        t.SetGUID(MajorType, Video); t.SetGUID(Subtype, subtype);
        t.SetUINT64(FrameSize, (ulong)width << 32 | (uint)height); t.SetUINT64(FrameRate, (ulong)fps << 32 | 1); t.SetUINT64(PixelAspect, 1UL << 32 | 1);
        t.SetUINT32(InterlaceMode, 2);
        if (bitrate > 0) t.SetUINT32(AvgBitrate, (uint)bitrate);
        return t;
    }
}
