using System.Runtime.InteropServices;

namespace EchoScribe.Audio.NativeInterop;

/// <summary>
/// Windows WASAPI Process Loopback 捕获所需的原生互操作定义
/// 参考: https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/
/// </summary>
internal static class AudioClientInterop
{
    // VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK GUID
    internal const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK =
        "VAD\\Process_Loopback";

    // IIDs
    internal static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    internal static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    internal static readonly Guid IID_IActivateAudioInterfaceAsyncOperation = new("72A22D78-CDE4-431D-B8CC-843A71199B6D");

    [DllImport("Mmdevapi.dll", SetLastError = true, PreserveSig = false)]
    internal static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        ref PropVariant activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);
}

/// <summary>
/// 音频客户端激活类型
/// </summary>
internal enum AudioClientActivationType : uint
{
    Default = 0,
    ProcessLoopback = 1
}

/// <summary>
/// 进程 Loopback 模式
/// </summary>
internal enum ProcessLoopbackMode : uint
{
    /// <summary>仅包含目标进程（及其子进程）的音频</summary>
    IncludeTargetProcessTree = 0,
    /// <summary>排除目标进程（及其子进程）的音频，捕获所有其它</summary>
    ExcludeTargetProcessTree = 1
}

/// <summary>
/// AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS 结构体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProcessLoopbackParams
{
    public uint TargetProcessId;
    public ProcessLoopbackMode ProcessLoopbackMode;
}

/// <summary>
/// AUDIOCLIENT_ACTIVATION_PARAMS 结构体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public AudioClientActivationType ActivationType;
    public AudioClientProcessLoopbackParams ProcessLoopbackParams;
}

/// <summary>
/// PROPVARIANT 简化版本（仅支持 VT_BLOB）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr blobSize;
    public IntPtr blobData;

    public const ushort VT_BLOB = 0x0041;

    /// <summary>
    /// 从 AudioClientActivationParams 创建 VT_BLOB 类型的 PropVariant
    /// </summary>
    public static PropVariant CreateBlob(AudioClientActivationParams activationParams)
    {
        int size = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr ptr = Marshal.AllocCoTaskMem(size);
        Marshal.StructureToPtr(activationParams, ptr, false);

        return new PropVariant
        {
            vt = VT_BLOB,
            blobSize = (IntPtr)size,
            blobData = ptr
        };
    }

    /// <summary>
    /// 释放 blob 内存
    /// </summary>
    public void Free()
    {
        if (blobData != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(blobData);
            blobData = IntPtr.Zero;
        }
    }
}

/// <summary>
/// IActivateAudioInterfaceCompletionHandler COM 接口
/// </summary>
[ComImport]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

/// <summary>
/// IActivateAudioInterfaceAsyncOperation COM 接口
/// </summary>
[ComImport]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int activateResult,
        [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

/// <summary>
/// WAVEFORMATEX 结构体
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;

    public const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
    public const ushort WAVE_FORMAT_PCM = 0x0001;
}
