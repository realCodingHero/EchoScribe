using System.Text;
using EchoScribe.Asr;

namespace EchoScribe.Export;

/// <summary>
/// 转录结果导出器
/// 支持 .txt（带时间戳）和 .srt 字幕格式
/// </summary>
public static class TranscriptionExporter
{
    /// <summary>
    /// 导出为带时间戳的纯文本
    /// </summary>
    public static void ExportToText(string filePath, IReadOnlyList<TranscriptionEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine($"  EchoScribe 转录记录");
        sb.AppendLine($"  导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.AppendLine($"[{FormatTimestamp(entry.Timestamp)}] {entry.Text}");
        }

        sb.AppendLine();
        sb.AppendLine($"═══════════════════════════════════════");
        sb.AppendLine($"  总计 {entries.Count} 条转录，时长 {FormatTimestamp(entries.LastOrDefault()?.EndTimestamp ?? TimeSpan.Zero)}");
        sb.AppendLine($"═══════════════════════════════════════");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出为 SRT 字幕格式
    /// </summary>
    public static void ExportToSrt(string filePath, IReadOnlyList<TranscriptionEntry> entries)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // 序号（从 1 开始）
            sb.AppendLine((i + 1).ToString());

            // 时间轴: 00:00:01,000 --> 00:00:03,500
            sb.AppendLine($"{FormatSrtTimestamp(entry.Timestamp)} --> {FormatSrtTimestamp(entry.EndTimestamp)}");

            // 字幕文本
            sb.AppendLine(entry.Text);

            // 空行分隔
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 格式化时间戳为 HH:MM:SS
    /// </summary>
    private static string FormatTimestamp(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// 格式化时间戳为 SRT 格式: HH:MM:SS,mmm
    /// </summary>
    private static string FormatSrtTimestamp(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}

/// <summary>
/// 转录条目（带时间戳）
/// </summary>
public class TranscriptionEntry
{
    /// <summary>片段开始时间</summary>
    public TimeSpan Timestamp { get; set; }

    /// <summary>片段时长</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>片段结束时间</summary>
    public TimeSpan EndTimestamp => Timestamp + Duration;

    /// <summary>转录文本</summary>
    public string Text { get; set; } = "";

    /// <summary>显示用的时间戳文本</summary>
    public string TimestampDisplay => $"[{(int)Timestamp.TotalHours:D2}:{Timestamp.Minutes:D2}:{Timestamp.Seconds:D2}]";
}
