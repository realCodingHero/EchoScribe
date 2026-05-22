using System.Net.Http;

namespace EchoScribe.Models;

/// <summary>
/// 模型文件管理器
/// 检查模型文件是否存在，支持自动下载
/// </summary>
public class ModelManager
{
    private readonly string _modelBaseDir;

    /// <summary>
    /// SenseVoice 模型目录名
    /// </summary>
    public const string SenseVoiceModelDirName = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue";

    /// <summary>
    /// 所需的模型文件及其下载 URL（从 Hugging Face 单文件下载）
    /// </summary>
    private static readonly ModelFileInfo[] RequiredModelFiles =
    [
        new("model.onnx",
            "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/model.onnx",
            "ASR 语音识别模型",
            约230_000_000), // ~230MB

        new("tokens.txt",
            "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/tokens.txt",
            "词表文件",
            约200_000), // ~200KB
    ];

    /// <summary>
    /// VAD 模型文件信息
    /// </summary>
    private static readonly ModelFileInfo VadModelFileInfo = new(
        "silero_vad.onnx",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx",
        "VAD 语音活动检测模型",
        约2_000_000); // ~2MB

    // 用中文"约"命名的常量辅助字段 — 仅用于预估大小显示
    private const long 约230_000_000 = 230_000_000;
    private const long 约200_000 = 200_000;
    private const long 约2_000_000 = 2_000_000;

    public string ModelDir => Path.Combine(_modelBaseDir, SenseVoiceModelDirName);

    public ModelManager(string? modelBaseDir = null)
    {
        _modelBaseDir = modelBaseDir ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "models");
    }

    /// <summary>
    /// 检查所有必需的模型文件是否存在
    /// </summary>
    public ModelCheckResult CheckModels()
    {
        var result = new ModelCheckResult
        {
            ModelDir = ModelDir,
            IsBaseDirectoryExists = Directory.Exists(_modelBaseDir),
            IsModelDirectoryExists = Directory.Exists(ModelDir)
        };

        if (!result.IsModelDirectoryExists)
        {
            foreach (var f in RequiredModelFiles)
                result.MissingFiles.Add(f.FileName);
            return result;
        }

        foreach (var f in RequiredModelFiles)
        {
            var path = Path.Combine(ModelDir, f.FileName);
            if (File.Exists(path))
                result.FoundFiles.Add(f.FileName);
            else
                result.MissingFiles.Add(f.FileName);
        }

        result.HasVadModel = File.Exists(Path.Combine(ModelDir, VadModelFileInfo.FileName));

        return result;
    }

    /// <summary>
    /// 确保模型目录存在
    /// </summary>
    public void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(ModelDir);
    }

    /// <summary>
    /// 获取所有需要下载的文件信息（含 VAD）
    /// </summary>
    public List<ModelFileInfo> GetFilesToDownload()
    {
        var files = new List<ModelFileInfo>();

        foreach (var f in RequiredModelFiles)
        {
            if (!File.Exists(Path.Combine(ModelDir, f.FileName)))
                files.Add(f);
        }

        if (!File.Exists(Path.Combine(ModelDir, VadModelFileInfo.FileName)))
            files.Add(VadModelFileInfo);

        return files;
    }

    /// <summary>
    /// 下载所有缺失的模型文件
    /// </summary>
    /// <param name="progress">进度回调: (当前文件名, 文件进度百分比 0-100, 总体进度百分比 0-100, 状态文本)</param>
    /// <param name="ct">取消令牌</param>
    public async Task DownloadModelsAsync(
        Action<ModelDownloadProgress> progress,
        CancellationToken ct = default)
    {
        EnsureDirectoryExists();

        var filesToDownload = GetFilesToDownload();
        if (filesToDownload.Count == 0)
        {
            progress(new ModelDownloadProgress("", 100, 100, "所有模型文件已就绪"));
            return;
        }

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);
        // 某些 CDN 需要 User-Agent
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EchoScribe/1.0");

        long totalEstimatedSize = filesToDownload.Sum(f => f.EstimatedSize);
        long totalDownloaded = 0;

        for (int i = 0; i < filesToDownload.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = filesToDownload[i];
            var destPath = Path.Combine(ModelDir, fileInfo.FileName);
            var tempPath = destPath + ".downloading";

            progress(new ModelDownloadProgress(
                fileInfo.FileName, 0,
                (int)(totalDownloaded * 100 / totalEstimatedSize),
                $"正在下载 {fileInfo.Description} ({i + 1}/{filesToDownload.Count})..."));

            try
            {
                using var response = await httpClient.GetAsync(fileInfo.Url,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? fileInfo.EstimatedSize;

                // 使用显式 using 块确保流在 File.Move 之前完全释放
                {
                    var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    try
                    {
                        var fileStream = new FileStream(tempPath,
                            FileMode.Create, FileAccess.Write, FileShare.None,
                            bufferSize: 81920, useAsync: true);
                        try
                        {
                            var buffer = new byte[81920];
                            long fileDownloaded = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                            {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                                fileDownloaded += bytesRead;

                                var filePercent = contentLength > 0
                                    ? (int)(fileDownloaded * 100 / contentLength)
                                    : -1;

                                var overallPercent = (int)((totalDownloaded + fileDownloaded) * 100 / totalEstimatedSize);

                                progress(new ModelDownloadProgress(
                                    fileInfo.FileName, filePercent,
                                    Math.Min(overallPercent, 99),
                                    $"正在下载 {fileInfo.Description} ({FormatSize(fileDownloaded)}/{FormatSize(contentLength)})"));
                            }

                            totalDownloaded += fileDownloaded;
                        }
                        finally
                        {
                            await fileStream.FlushAsync(ct);
                            await fileStream.DisposeAsync();
                        }
                    }
                    finally
                    {
                        contentStream.Dispose();
                    }
                }

                // 流已完全释放，安全重命名
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(tempPath, destPath);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // 清理临时文件
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 忽略清理错误 */ }
                throw;
            }
        }

        progress(new ModelDownloadProgress("", 100, 100, "✓ 所有模型下载完成！"));
    }

    /// <summary>
    /// 格式化文件大小显示
    /// </summary>
    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}

/// <summary>
/// 模型文件信息
/// </summary>
public record ModelFileInfo(
    string FileName,
    string Url,
    string Description,
    long EstimatedSize);

/// <summary>
/// 模型下载进度
/// </summary>
public record ModelDownloadProgress(
    string CurrentFile,
    int FilePercent,
    int OverallPercent,
    string StatusText);

/// <summary>
/// 模型检查结果
/// </summary>
public class ModelCheckResult
{
    public string ModelDir { get; set; } = "";
    public bool IsBaseDirectoryExists { get; set; }
    public bool IsModelDirectoryExists { get; set; }
    public List<string> FoundFiles { get; set; } = [];
    public List<string> MissingFiles { get; set; } = [];
    public bool HasVadModel { get; set; }

    public bool IsReady => MissingFiles.Count == 0 && FoundFiles.Count > 0;
}
