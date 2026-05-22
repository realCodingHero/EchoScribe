using System.Runtime.InteropServices;
using EchoScribe.Audio.NativeInterop;
using NAudio.Wave;

namespace EchoScribe.Audio;

/// <summary>
/// 基于 WASAPI Process Loopback API 的单进程音频捕获
/// 需要 Windows 10 Build 20348+ 或 Windows 11
/// </summary>
public sealed class ProcessLoopbackCapture : IAudioCapturer, IActivateAudioInterfaceCompletionHandler
{
    private readonly TaskCompletionSource<object> _activationTcs = new();
    private object? _audioClient;
    private Thread? _captureThread;
    private volatile bool _isCapturing;
    private int _targetProcessId;

    // 重采样相关
    private WaveFormat? _captureFormat;
    private readonly int _targetSampleRate = 16000;

    public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
    public event EventHandler<Exception>? CaptureError;
    public bool IsCapturing => _isCapturing;
    public float CurrentLevel { get; private set; }

    /// <summary>
    /// 检查当前系统是否支持 Process Loopback 捕获
    /// </summary>
    public static bool IsSupported()
    {
        // 需要 Windows 10 Build 20348+
        var version = Environment.OSVersion.Version;
        return version.Major >= 10 && version.Build >= 20348;
    }

    public void StartCapture(int processId)
    {
        if (_isCapturing)
            throw new InvalidOperationException("已在捕获中");

        _targetProcessId = processId;

        try
        {
            var activationParams = new AudioClientActivationParams
            {
                ActivationType = AudioClientActivationType.ProcessLoopback,
                ProcessLoopbackParams = new AudioClientProcessLoopbackParams
                {
                    TargetProcessId = (uint)processId,
                    ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree
                }
            };

            var propVariant = PropVariant.CreateBlob(activationParams);

            try
            {
                AudioClientInterop.ActivateAudioInterfaceAsync(
                    AudioClientInterop.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                    AudioClientInterop.IID_IAudioClient,
                    ref propVariant,
                    this,
                    out _);
            }
            finally
            {
                propVariant.Free();
            }

            // 等待激活完成
            _activationTcs.Task.Wait(TimeSpan.FromSeconds(5));

            // 启动捕获线程
            _isCapturing = true;
            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "EchoScribe-AudioCapture",
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            CaptureError?.Invoke(this, ex);
            throw;
        }
    }

    public void StopCapture()
    {
        _isCapturing = false;
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;

        if (_audioClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _audioClient = null;
    }

    /// <summary>
    /// IActivateAudioInterfaceCompletionHandler 回调实现
    /// </summary>
    void IActivateAudioInterfaceCompletionHandler.ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        try
        {
            activateOperation.GetActivateResult(out int hr, out object activatedInterface);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            _audioClient = activatedInterface;
            _activationTcs.TrySetResult(activatedInterface);
        }
        catch (Exception ex)
        {
            _activationTcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// 音频捕获循环（在独立线程运行）
    /// </summary>
    private void CaptureLoop()
    {
        try
        {
            // 使用 NAudio WasapiLoopbackCapture 作为后端实现
            // 实际的 Process Loopback 需要更底层的 IAudioClient 操作
            // 这里先使用简化的实现，后续可以替换为完整的原生实现
            using var capture = new WasapiLoopbackCapture();
            _captureFormat = capture.WaveFormat;

            // 配置重采样链
            var resampler = new AudioResampler(_captureFormat, _targetSampleRate);

            capture.DataAvailable += (_, e) =>
            {
                if (!_isCapturing || e.BytesRecorded == 0) return;

                try
                {
                    // 重采样到 16kHz 单通道 float32
                    var samples = resampler.Resample(e.Buffer, e.BytesRecorded);

                    if (samples.Length > 0)
                    {
                        // 计算音频电平
                        CurrentLevel = CalculateLevel(samples);

                        // 触发事件
                        AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(samples));
                    }
                }
                catch (Exception ex)
                {
                    CaptureError?.Invoke(this, ex);
                }
            };

            capture.StartRecording();

            // 等待停止信号
            while (_isCapturing)
            {
                Thread.Sleep(10);
            }

            capture.StopRecording();
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            CaptureError?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// 计算音频电平 (RMS)
    /// </summary>
    private static float CalculateLevel(float[] samples)
    {
        if (samples.Length == 0) return 0f;

        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    public void Dispose()
    {
        StopCapture();
    }
}
