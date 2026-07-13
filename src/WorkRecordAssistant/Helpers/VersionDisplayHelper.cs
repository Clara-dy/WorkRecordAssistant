namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 版本号与时间展示、复制格式。
/// </summary>
public static class VersionDisplayHelper
{
    public static string BuildTimeVersionHint(
        DateTime createdAt, bool showTime, string? versionNumber, string? versionInfo)
    {
        var parts = new List<string>();

        if (showTime || !string.IsNullOrWhiteSpace(versionNumber))
            parts.Add(createdAt.ToString("HH:mm"));

        if (!string.IsNullOrWhiteSpace(versionNumber))
            parts.Add(versionNumber.Trim());

        var line = string.Join(" ", parts);
        if (!string.IsNullOrWhiteSpace(versionInfo))
        {
            var info = versionInfo.Trim();
            line = string.IsNullOrEmpty(line) ? info : $"{line} · {info}";
        }

        return line;
    }

    public static bool ShouldShowTimeVersionHint(
        bool showTime, string? versionNumber, string? versionInfo) =>
        showTime
        || !string.IsNullOrWhiteSpace(versionNumber)
        || !string.IsNullOrWhiteSpace(versionInfo);

    public static string BuildCopyLine(
        string content, string? versionNumber, string? versionInfo, string prefix = "")
    {
        var text = content.Trim();
        if (!string.IsNullOrWhiteSpace(versionNumber))
            text = $"{text} {versionNumber.Trim()}";
        if (!string.IsNullOrWhiteSpace(versionInfo))
            text = $"{text} {versionInfo.Trim()}";
        return prefix + text;
    }

    public static bool IsValidVersionNumber(string? value) =>
        string.IsNullOrEmpty(value) || value.All(char.IsDigit);
}
