using SherpaOnnx;

namespace EchoScribe.Asr;

/// <summary>
/// 基于 sherpa-onnx 的 SenseVoice 语音识别引擎
/// </summary>
public sealed class SherpaOnnxEngine : IDisposable
{
    private OfflineRecognizer? _recognizer;
    private readonly string _modelDir;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public string? ErrorMessage { get; private set; }

    public SherpaOnnxEngine(string modelDir)
    {
        _modelDir = modelDir;
    }

    /// <summary>
    /// 初始化 ASR 引擎，加载模型到 GPU
    /// </summary>
    /// <param name="useCuda">是否使用 CUDA GPU 加速</param>
    public bool Initialize(bool useCuda = true)
    {
        try
        {
            var modelPath = Path.Combine(_modelDir, "model.onnx");
            var tokensPath = Path.Combine(_modelDir, "tokens.txt");

            if (!File.Exists(modelPath))
            {
                ErrorMessage = $"模型文件不存在: {modelPath}";
                return false;
            }

            if (!File.Exists(tokensPath))
            {
                ErrorMessage = $"词表文件不存在: {tokensPath}";
                return false;
            }

            var config = new OfflineRecognizerConfig();
            config.ModelConfig.SenseVoice.Model = modelPath;
            config.ModelConfig.SenseVoice.Language = "zh";
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.Provider = useCuda ? "cuda" : "cpu";
            config.ModelConfig.NumThreads = useCuda ? 1 : Environment.ProcessorCount;

            _recognizer = new OfflineRecognizer(config);
            _isInitialized = true;
            ErrorMessage = null;

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"模型初始化失败: {ex.Message}";

            // 如果 CUDA 失败，尝试 CPU 回退
            if (useCuda)
            {
                ErrorMessage += " (正在尝试 CPU 模式...)";
                return Initialize(useCuda: false);
            }

            return false;
        }
    }

    /// <summary>
    /// 识别音频片段
    /// </summary>
    /// <param name="samples">16kHz 单通道 float32 PCM 音频数据</param>
    /// <param name="sampleRate">采样率（默认 16000）</param>
    /// <returns>识别结果文本</returns>
    public string Recognize(float[] samples, int sampleRate = 16000)
    {
        if (!_isInitialized || _recognizer == null)
            throw new InvalidOperationException("ASR 引擎未初始化");

        if (samples.Length == 0)
            return "";

        var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        _recognizer.Decode(stream);

        var result = stream.Result.Text;
        return TextPostProcessor.Process(result);
    }

    public void Dispose()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _isInitialized = false;
    }
}
