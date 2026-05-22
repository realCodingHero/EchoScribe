using NAudio.Wave;

namespace EchoScribe.Audio;

/// <summary>
/// 基于 NAudio 的系统级 Loopback 音频捕获（备选方案）
/// 捕获系统所有音频输出，不区分进程
/// </summary>
public sealed class SystemLoopbackCapture : IAudioCapturer
{
    private WasapiLoopbackCapture? _capture;
    private AudioResampler? _resampler;
    private volatile bool _isCapturing;
    private readonly int _targetSampleRate = 16000;

    public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
    public event EventHandler<Exception>? CaptureError;
    public bool IsCapturing => _isCapturing;
    public float CurrentLevel { get; private set; }

    public void StartCapture(int processId)
    {
        if (_isCapturing)
            throw new InvalidOperationException("已在捕获中");

        try
        {
            _capture = new WasapiLoopbackCapture();
            _resampler = new AudioResampler(_capture.WaveFormat, _targetSampleRate);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _isCapturing = true;
            _capture.StartRecording();
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
        _capture?.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || e.BytesRecorded == 0 || _resampler == null) return;

        try
        {
            var samples = _resampler.Resample(e.Buffer, e.BytesRecorded);

            if (samples.Length > 0)
            {
                CurrentLevel = CalculateLevel(samples);
                AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(samples));
            }
        }
        catch (Exception ex)
        {
            CaptureError?.Invoke(this, ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _isCapturing = false;
        if (e.Exception != null)
        {
            CaptureError?.Invoke(this, e.Exception);
        }
    }

    private static float CalculateLevel(float[] samples)
    {
        if (samples.Length == 0) return 0f;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public void Dispose()
    {
        StopCapture();
        _capture?.Dispose();
        _capture = null;
    }
}
