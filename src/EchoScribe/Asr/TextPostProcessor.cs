using System.Text.RegularExpressions;

namespace EchoScribe.Asr;

/// <summary>
/// SenseVoice 输出文字后处理器
/// 清理特殊标记、规范化标点
/// </summary>
public static partial class TextPostProcessor
{
    /// <summary>
    /// 处理 SenseVoice 原始输出文本
    /// </summary>
    public static string Process(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "";

        var text = rawText;

        // 1. 移除语言标签 <|zh|> <|en|> <|ja|> <|ko|> <|yue|>
        text = LanguageTagRegex().Replace(text, "");

        // 2. 移除情感/事件标签 <|HAPPY|> <|SAD|> <|ANGRY|> <|NEUTRAL|> <|BGM|> <|Speech|> 等
        text = EmotionTagRegex().Replace(text, "");

        // 3. 移除其他尖括号标签
        text = GenericTagRegex().Replace(text, "");

        // 4. 规范化空格
        text = MultipleSpacesRegex().Replace(text, " ");

        // 5. 去除前后空白
        text = text.Trim();

        return text;
    }

    /// <summary>
    /// 提取情感标签（如果用户需要显示情感信息）
    /// </summary>
    public static string? ExtractEmotion(string rawText)
    {
        var match = EmotionExtractRegex().Match(rawText);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"<\|(?:zh|en|ja|ko|yue|nospeech)\|>", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageTagRegex();

    [GeneratedRegex(@"<\|(?:HAPPY|SAD|ANGRY|NEUTRAL|FEARFUL|DISGUSTED|SURPRISED|BGM|Speech|Applause|Laughter|Silence|OTHER)\|>", RegexOptions.IgnoreCase)]
    private static partial Regex EmotionTagRegex();

    [GeneratedRegex(@"<\|[^>]*\|>")]
    private static partial Regex GenericTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"<\|(HAPPY|SAD|ANGRY|NEUTRAL|FEARFUL|DISGUSTED|SURPRISED)\|>", RegexOptions.IgnoreCase)]
    private static partial Regex EmotionExtractRegex();
}
