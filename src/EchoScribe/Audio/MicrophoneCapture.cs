using NAudio.Wave;

namespace EchoScribe.Audio;

/// <summary>
/// 麦克风音频捕获（可选功能）
/// 在转录过程中可随时开关
/// </summary>
public sealed class MicrophoneCapture : IAudioCapturer
{
    private WaveInEvent? _waveIn;
    private AudioResampler? _resampler;
    private volatile bool _isCapturing;
    private readonly int _targetSampleRate = 16000;

    public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
    public event EventHandler<Exception>? CaptureError;
    public bool IsCapturing => _isCapturing;
    public float CurrentLevel { get; private set; }

    /// <summary>
    /// 获取所有可用的麦克风设备
    /// </summary>
    public static List<MicrophoneDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<MicrophoneDeviceInfo>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new MicrophoneDeviceInfo
            {
                DeviceIndex = i,
                Name = caps.ProductName,
                Channels = caps.Channels
            });
        }
        return devices;
    }

    /// <summary>
    /// 开始麦克风捕获
    /// </summary>
    /// <param name="processId">此处忽略，麦克风不需要进程ID。传入设备索引请使用 StartCapture(deviceIndex) 重载</param>
    public void StartCapture(int processId)
    {
        StartCaptureFromDevice(0); // 使用默认设备
    }

    /// <summary>
    /// 从指定设备开始捕获
    /// </summary>
    public void StartCaptureFromDevice(int deviceIndex)
    {
        if (_isCapturing)
            throw new InvalidOperationException("已在捕获中");

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(_targetSampleRate, 16, 1),
                BufferMilliseconds = 100
            };

            _resampler = new AudioResampler(_waveIn.WaveFormat, _targetSampleRate);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _isCapturing = true;
            _waveIn.StartRecording();
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
        _waveIn?.StopRecording();
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
        _waveIn?.Dispose();
        _waveIn = null;
    }
}

/// <summary>
/// 麦克风设备信息
/// </summary>
public class MicrophoneDeviceInfo
{
    public int DeviceIndex { get; set; }
    public string Name { get; set; } = "";
    public int Channels { get; set; }

    public override string ToString() => Name;
}
