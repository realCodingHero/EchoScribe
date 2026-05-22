using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EchoScribe.Asr;
using EchoScribe.Audio;
using EchoScribe.Export;
using EchoScribe.Models;
using Microsoft.Win32;

namespace EchoScribe.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // === 服务 ===
    private readonly AudioProcessScanner _processScanner = new();
    private readonly ModelManager _modelManager;
    private IAudioCapturer? _appCapturer;
    private MicrophoneCapture? _micCapturer;
    private SherpaOnnxEngine? _asrEngine;
    private VadEngine? _vadEngine;
    private TranscriptionPipeline? _pipeline;
    private DispatcherTimer? _scanTimer;
    private DispatcherTimer? _levelTimer;
    private readonly Dispatcher _dispatcher;

    // === 可绑定属性 ===

    /// <summary>当前有音频输出的进程列表</summary>
    [ObservableProperty]
    private ObservableCollection<AudioProcessInfo> _audioProcesses = [];

    /// <summary>选中的目标进程</summary>
    [ObservableProperty]
    private AudioProcessInfo? _selectedProcess;

    /// <summary>转录条目列表（带时间戳）</summary>
    [ObservableProperty]
    private ObservableCollection<TranscriptionEntry> _transcriptionEntries = [];

    /// <summary>是否正在转录中</summary>
    [ObservableProperty]
    private bool _isTranscribing;

    /// <summary>状态消息</summary>
    [ObservableProperty]
    private string _statusMessage = "就绪";

    /// <summary>GPU 信息</summary>
    [ObservableProperty]
    private string _gpuInfo = "检测中...";

    /// <summary>应用音频电平（0-1）</summary>
    [ObservableProperty]
    private double _appAudioLevel;

    /// <summary>麦克风音频电平（0-1）</summary>
    [ObservableProperty]
    private double _micAudioLevel;

    /// <summary>麦克风是否启用</summary>
    [ObservableProperty]
    private bool _isMicrophoneEnabled;

    /// <summary>模型是否已加载</summary>
    [ObservableProperty]
    private bool _isModelLoaded;

    /// <summary>模型加载进度文本</summary>
    [ObservableProperty]
    private string _modelLoadingStatus = "正在加载模型...";

    /// <summary>可用的麦克风设备列表</summary>
    [ObservableProperty]
    private ObservableCollection<MicrophoneDeviceInfo> _microphoneDevices = [];

    /// <summary>选中的麦克风设备</summary>
    [ObservableProperty]
    private MicrophoneDeviceInfo? _selectedMicrophone;

    /// <summary>是否支持单进程捕获</summary>
    [ObservableProperty]
    private bool _isProcessLoopbackSupported;

    /// <summary>是否正在下载模型</summary>
    [ObservableProperty]
    private bool _isDownloadingModel;

    /// <summary>下载进度（0-100）</summary>
    [ObservableProperty]
    private int _downloadProgress;

    /// <summary>下载进度文本</summary>
    [ObservableProperty]
    private string _downloadProgressText = "";

    private CancellationTokenSource? _downloadCts;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _modelManager = new ModelManager();
        IsProcessLoopbackSupported = ProcessLoopbackCapture.IsSupported();
    }

    /// <summary>
    /// 应用启动时初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        // 扫描音频进程
        RefreshProcessList();

        // 扫描麦克风设备
        RefreshMicrophoneDevices();

        // 启动定时扫描
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _scanTimer.Tick += (_, _) => RefreshProcessList();
        _scanTimer.Start();

        // 启动音频电平刷新
        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _levelTimer.Tick += (_, _) => UpdateAudioLevels();
        _levelTimer.Start();

        // 异步加载模型（如果缺失会自动下载）
        await Task.Run(LoadModel);
    }

    /// <summary>
    /// 加载 ASR 模型（如果缺失则自动下载）
    /// </summary>
    private async Task LoadModel()
    {
        var checkResult = _modelManager.CheckModels();

        if (!checkResult.IsReady)
        {
            _dispatcher.Invoke(() =>
            {
                ModelLoadingStatus = "模型文件不存在，正在自动下载...";
                StatusMessage = "⏬ 正在下载 AI 模型（首次运行需要下载约 240MB）";
            });

            // 自动开始下载
            await DownloadAndLoadModelsAsync();
            return;
        }

        // 模型存在，直接加载
        InitializeEngines();
    }

    /// <summary>
    /// 下载模型并加载
    /// </summary>
    private async Task DownloadAndLoadModelsAsync()
    {
        _downloadCts = new CancellationTokenSource();

        _dispatcher.Invoke(() =>
        {
            IsDownloadingModel = true;
            DownloadProgress = 0;
            DownloadProgressText = "准备下载...";
        });

        try
        {
            await _modelManager.DownloadModelsAsync(progress =>
            {
                _dispatcher.InvokeAsync(() =>
                {
                    DownloadProgress = progress.OverallPercent;
                    DownloadProgressText = progress.StatusText;
                    ModelLoadingStatus = progress.StatusText;
                    StatusMessage = $"⏬ 下载中: {progress.OverallPercent}%";
                });
            }, _downloadCts.Token);

            _dispatcher.Invoke(() =>
            {
                IsDownloadingModel = false;
                ModelLoadingStatus = "下载完成，正在加载模型...";
                StatusMessage = "正在加载模型...";
            });

            // 下载完成，加载引擎
            InitializeEngines();
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Invoke(() =>
            {
                IsDownloadingModel = false;
                ModelLoadingStatus = "下载已取消";
                StatusMessage = "⚠ 模型下载已取消";
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                IsDownloadingModel = false;
                ModelLoadingStatus = $"下载失败: {ex.Message}";
                StatusMessage = "✗ 模型下载失败，请检查网络连接";
            });
        }
    }

    /// <summary>
    /// 初始化 ASR 和 VAD 引擎
    /// </summary>
    private void InitializeEngines()
    {
        _dispatcher.Invoke(() => ModelLoadingStatus = "正在加载 ASR 模型...");

        _asrEngine = new SherpaOnnxEngine(_modelManager.ModelDir);
        var asrOk = _asrEngine.Initialize(useCuda: true);

        _dispatcher.Invoke(() => ModelLoadingStatus = "正在加载 VAD 模型...");

        _vadEngine = new VadEngine(_modelManager.ModelDir);
        _vadEngine.Initialize();

        _dispatcher.Invoke(() =>
        {
            IsModelLoaded = asrOk;
            ModelLoadingStatus = asrOk ? "模型加载完成" : $"模型加载失败: {_asrEngine.ErrorMessage}";
            StatusMessage = asrOk ? "✓ 模型就绪，选择进程后即可开始转录" : "✗ 模型加载失败";
            GpuInfo = asrOk ? "SenseVoice (GPU)" : "SenseVoice (CPU)";
        });
    }

    /// <summary>
    /// 手动重新下载模型
    /// </summary>
    [RelayCommand]
    private async Task RetryDownloadModels()
    {
        if (IsDownloadingModel) return;
        await Task.Run(DownloadAndLoadModelsAsync);
    }

    /// <summary>
    /// 取消下载
    /// </summary>
    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    // === 命令 ===

    /// <summary>
    /// 开始转录
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTranscription))]
    private void StartTranscription()
    {
        if (SelectedProcess == null || _asrEngine == null || _vadEngine == null) return;

        try
        {
            // 创建转录流水线
            _pipeline = new TranscriptionPipeline(_asrEngine, _vadEngine);
            _pipeline.TranscriptionReceived += OnTranscriptionReceived;
            _pipeline.PipelineError += OnPipelineError;

            // 创建音频捕获器
            if (_isProcessLoopbackSupported)
            {
                _appCapturer = new ProcessLoopbackCapture();
            }
            else
            {
                _appCapturer = new SystemLoopbackCapture();
            }

            _appCapturer.AudioDataAvailable += OnAudioDataAvailable;
            _appCapturer.CaptureError += OnCaptureError;

            // 启动流水线和捕获
            _pipeline.Start();
            _appCapturer.StartCapture(SelectedProcess.ProcessId);

            // 如果麦克风已启用，同时启动麦克风
            if (IsMicrophoneEnabled)
            {
                StartMicrophone();
            }

            IsTranscribing = true;
            StatusMessage = $"▶ 正在转录: {SelectedProcess.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ 启动失败: {ex.Message}";
            StopTranscriptionInternal();
        }
    }

    private bool CanStartTranscription() =>
        !IsTranscribing && SelectedProcess != null && IsModelLoaded;

    /// <summary>
    /// 停止转录
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopTranscription))]
    private void StopTranscription()
    {
        StopTranscriptionInternal();
        StatusMessage = "■ 转录已停止";
    }

    private bool CanStopTranscription() => IsTranscribing;

    private void StopTranscriptionInternal()
    {
        _appCapturer?.StopCapture();
        _appCapturer?.Dispose();
        _appCapturer = null;

        StopMicrophone();

        _pipeline?.Stop();
        _pipeline?.Dispose();
        _pipeline = null;

        IsTranscribing = false;
    }

    /// <summary>
    /// 刷新进程列表
    /// </summary>
    [RelayCommand]
    private void RefreshProcessList()
    {
        var processes = _processScanner.GetAudioProcesses();
        var currentSelectedPid = SelectedProcess?.ProcessId;

        AudioProcesses.Clear();
        foreach (var p in processes)
        {
            AudioProcesses.Add(p);
        }

        // 恢复之前的选择
        if (currentSelectedPid.HasValue)
        {
            SelectedProcess = AudioProcesses.FirstOrDefault(p => p.ProcessId == currentSelectedPid.Value);
        }
    }

    /// <summary>
    /// 刷新麦克风设备列表
    /// </summary>
    [RelayCommand]
    private void RefreshMicrophoneDevices()
    {
        var devices = MicrophoneCapture.GetAvailableDevices();
        MicrophoneDevices.Clear();
        foreach (var d in devices)
        {
            MicrophoneDevices.Add(d);
        }

        if (MicrophoneDevices.Count > 0 && SelectedMicrophone == null)
        {
            SelectedMicrophone = MicrophoneDevices[0];
        }
    }

    /// <summary>
    /// 清空转录内容
    /// </summary>
    [RelayCommand]
    private void ClearTranscription()
    {
        TranscriptionEntries.Clear();
    }

    /// <summary>
    /// 导出转录结果
    /// </summary>
    [RelayCommand]
    private void ExportTranscription()
    {
        if (TranscriptionEntries.Count == 0)
        {
            StatusMessage = "没有可导出的转录内容";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出转录结果",
            Filter = "SRT 字幕文件 (*.srt)|*.srt|文本文件 (*.txt)|*.txt",
            DefaultExt = ".srt",
            FileName = $"EchoScribe_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var entries = TranscriptionEntries.ToList();

                if (dialog.FileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                {
                    TranscriptionExporter.ExportToSrt(dialog.FileName, entries);
                }
                else
                {
                    TranscriptionExporter.ExportToText(dialog.FileName, entries);
                }

                StatusMessage = $"✓ 已导出到: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ 导出失败: {ex.Message}";
            }
        }
    }

    // === 麦克风开关 ===

    partial void OnIsMicrophoneEnabledChanged(bool value)
    {
        if (IsTranscribing)
        {
            if (value)
                StartMicrophone();
            else
                StopMicrophone();
        }
    }

    private void StartMicrophone()
    {
        if (_micCapturer != null) return;

        try
        {
            _micCapturer = new MicrophoneCapture();
            _micCapturer.AudioDataAvailable += OnAudioDataAvailable;
            _micCapturer.CaptureError += OnCaptureError;

            var deviceIndex = SelectedMicrophone?.DeviceIndex ?? 0;
            _micCapturer.StartCaptureFromDevice(deviceIndex);
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ 麦克风启动失败: {ex.Message}";
            _micCapturer?.Dispose();
            _micCapturer = null;
        }
    }

    private void StopMicrophone()
    {
        _micCapturer?.StopCapture();
        _micCapturer?.Dispose();
        _micCapturer = null;
        MicAudioLevel = 0;
    }

    // === 事件处理 ===

    private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
    {
        _pipeline?.FeedAudio(e.Samples);
    }

    private void OnTranscriptionReceived(object? sender, TranscriptionResultEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            TranscriptionEntries.Add(new TranscriptionEntry
            {
                Timestamp = e.Timestamp,
                Duration = e.Duration,
                Text = e.Text
            });
        });
    }

    private void OnCaptureError(object? sender, Exception e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"⚠ 音频错误: {e.Message}";
        });
    }

    private void OnPipelineError(object? sender, Exception e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"⚠ 识别错误: {e.Message}";
        });
    }

    private void UpdateAudioLevels()
    {
        if (_appCapturer != null)
        {
            AppAudioLevel = Math.Min(1.0, _appCapturer.CurrentLevel * 5); // 放大显示
        }
        else
        {
            AppAudioLevel = 0;
        }

        if (_micCapturer != null)
        {
            MicAudioLevel = Math.Min(1.0, _micCapturer.CurrentLevel * 5);
        }
        else
        {
            MicAudioLevel = 0;
        }
    }

    // === 属性变更通知 ===

    partial void OnSelectedProcessChanged(AudioProcessInfo? value)
    {
        StartTranscriptionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTranscribingChanged(bool value)
    {
        StartTranscriptionCommand.NotifyCanExecuteChanged();
        StopTranscriptionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsModelLoadedChanged(bool value)
    {
        StartTranscriptionCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _scanTimer?.Stop();
        _levelTimer?.Stop();
        StopTranscriptionInternal();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _asrEngine?.Dispose();
        _vadEngine?.Dispose();
    }
}
