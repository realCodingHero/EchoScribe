using System.Threading.Channels;
using EchoScribe.Audio;

namespace EchoScribe.Asr;

/// <summary>
/// 转录结果事件参数
/// </summary>
public class TranscriptionResultEventArgs : EventArgs
{
    /// <summary>转录文本</summary>
    public string Text { get; }

    /// <summary>从转录开始到此片段的时间偏移</summary>
    public TimeSpan Timestamp { get; }

    /// <summary>音频片段时长</summary>
    public TimeSpan Duration { get; }

    /// <summary>情感标签（可选）</summary>
    public string? Emotion { get; }

    public TranscriptionResultEventArgs(string text, TimeSpan timestamp, TimeSpan duration, string? emotion = null)
    {
        Text = text;
        Timestamp = timestamp;
        Duration = duration;
        Emotion = emotion;
    }
}

/// <summary>
/// 转录流水线：音频 → VAD → ASR → 文字输出
/// 在后台线程运行
/// </summary>
public sealed class TranscriptionPipeline : IDisposable
{
    private readonly SherpaOnnxEngine _asrEngine;
    private readonly VadEngine _vadEngine;
    private readonly Channel<float[]> _audioChannel;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private DateTime _startTime;
    private long _totalSamplesProcessed;

    /// <summary>
    /// 新的转录结果可用时触发
    /// </summary>
    public event EventHandler<TranscriptionResultEventArgs>? TranscriptionReceived;

    /// <summary>
    /// 流水线出错时触发
    /// </summary>
    public event EventHandler<Exception>? PipelineError;

    /// <summary>
    /// 流水线是否正在运行
    /// </summary>
    public bool IsRunning => _processingTask != null && !_processingTask.IsCompleted;

    public TranscriptionPipeline(SherpaOnnxEngine asrEngine, VadEngine vadEngine)
    {
        _asrEngine = asrEngine;
        _vadEngine = vadEngine;

        // 有界 Channel，最多缓存 200 个 chunk（约 20 秒音频）
        _audioChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest // 满了就丢弃最旧的
        });
    }

    /// <summary>
    /// 启动转录流水线
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _startTime = DateTime.Now;
        _totalSamplesProcessed = 0;
        _vadEngine.Reset();

        _processingTask = Task.Run(() => ProcessLoop(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// 停止转录流水线
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _processingTask?.Wait(TimeSpan.FromSeconds(3));
        _processingTask = null;
    }

    /// <summary>
    /// 送入音频数据
    /// </summary>
    public void FeedAudio(float[] samples)
    {
        if (!IsRunning) return;
        _audioChannel.Writer.TryWrite(samples);
    }

    /// <summary>
    /// 转录处理循环
    /// </summary>
    private async Task ProcessLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    // VAD 检测语音段
                    var segments = _vadEngine.Process(chunk);

                    foreach (var segment in segments)
                    {
                        if (ct.IsCancellationRequested) break;

                        // 忽略过短的片段（<0.3秒）
                        if (segment.Samples.Length < 4800) continue;

                        // ASR 识别
                        var rawText = _asrEngine.Recognize(segment.Samples);

                        if (!string.IsNullOrWhiteSpace(rawText))
                        {
                            var elapsed = DateTime.Now - _startTime;

                            TranscriptionReceived?.Invoke(this, new TranscriptionResultEventArgs(
                                text: rawText,
                                timestamp: elapsed,
                                duration: TimeSpan.FromSeconds(segment.Duration)
                            ));
                        }
                    }

                    _totalSamplesProcessed += chunk.Length;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PipelineError?.Invoke(this, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _audioChannel.Writer.Complete();
    }
}
