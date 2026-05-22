namespace EchoScribe.Audio;

/// <summary>
/// 音频数据事件参数
/// </summary>
public class AudioDataEventArgs : EventArgs
{
    /// <summary>
    /// 16kHz 单通道 float32 PCM 音频数据
    /// </summary>
    public float[] Samples { get; }

    /// <summary>
    /// 采样率（始终为 16000）
    /// </summary>
    public int SampleRate { get; } = 16000;

    public AudioDataEventArgs(float[] samples)
    {
        Samples = samples;
    }
}

/// <summary>
/// 统一音频捕获接口
/// </summary>
public interface IAudioCapturer : IDisposable
{
    /// <summary>
    /// 当新的音频数据可用时触发（已重采样为 16kHz 单通道 float32）
    /// </summary>
    event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

    /// <summary>
    /// 捕获过程中发生错误时触发
    /// </summary>
    event EventHandler<Exception>? CaptureError;

    /// <summary>
    /// 开始捕获指定进程的音频
    /// </summary>
    /// <param name="processId">目标进程 PID，为 0 时捕获系统全局音频</param>
    void StartCapture(int processId);

    /// <summary>
    /// 停止捕获
    /// </summary>
    void StopCapture();

    /// <summary>
    /// 是否正在捕获中
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// 当前音频电平（0.0 - 1.0）
    /// </summary>
    float CurrentLevel { get; }
}
