using SherpaOnnx;

namespace EchoScribe.Asr;

/// <summary>
/// 基于 Silero VAD 的语音活动检测
/// 通过 sherpa-onnx 内置支持
/// </summary>
public sealed class VadEngine : IDisposable
{
    private VoiceActivityDetector? _vad;
    private readonly string _modelDir;
    private bool _isInitialized;

    /// <summary>
    /// VAD 检测到的语音片段
    /// </summary>
    public class SpeechSegment
    {
        public float[] Samples { get; set; } = [];
        public float StartTime { get; set; }
        public float Duration { get; set; }
    }

    public bool IsInitialized => _isInitialized;

    public VadEngine(string modelDir)
    {
        _modelDir = modelDir;
    }

    /// <summary>
    /// 初始化 VAD 引擎
    /// </summary>
    public bool Initialize(int sampleRate = 16000)
    {
        try
        {
            var modelPath = Path.Combine(_modelDir, "silero_vad.onnx");

            if (!File.Exists(modelPath))
            {
                // VAD 模型不存在时使用简单的能量检测作为后备
                return false;
            }

            var config = new SileroVadModelConfig
            {
                Model = modelPath,
                Threshold = 0.5f,
                MinSilenceDuration = 0.5f,
                MinSpeechDuration = 0.25f,
                WindowSize = 512
            };

            var vadConfig = new VadModelConfig
            {
                SileroVad = config,
                SampleRate = sampleRate,
                NumThreads = 1,
                Provider = "cpu" // VAD 轻量，用 CPU 即可
            };

            _vad = new VoiceActivityDetector(vadConfig, 30f); // 最多缓存 30 秒
            _isInitialized = true;
            return true;
        }
        catch (Exception)
        {
            _isInitialized = false;
            return false;
        }
    }

    /// <summary>
    /// 送入音频数据并获取检测到的语音片段
    /// </summary>
    public List<SpeechSegment> Process(float[] samples, int sampleRate = 16000)
    {
        var segments = new List<SpeechSegment>();

        if (!_isInitialized || _vad == null)
        {
            // VAD 不可用时，直接返回整个音频作为一个段
            if (samples.Length > 0)
            {
                segments.Add(new SpeechSegment
                {
                    Samples = samples,
                    StartTime = 0,
                    Duration = (float)samples.Length / sampleRate
                });
            }
            return segments;
        }

        _vad.AcceptWaveform(samples);

        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();
            segments.Add(new SpeechSegment
            {
                Samples = segment.Samples,
                StartTime = segment.Start / (float)sampleRate,
                Duration = (float)segment.Samples.Length / sampleRate
            });
            _vad.Pop();
        }

        return segments;
    }

    /// <summary>
    /// 清除 VAD 内部状态
    /// </summary>
    public void Reset()
    {
        _vad?.Clear();
    }

    public void Dispose()
    {
        _vad?.Dispose();
        _vad = null;
        _isInitialized = false;
    }
}
